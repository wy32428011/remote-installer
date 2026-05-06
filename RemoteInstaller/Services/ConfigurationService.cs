using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 配置文件管理服务
/// 负责远程服务器上中间件配置文件的查找、读取、备份和保存
/// </summary>
public class ConfigurationService
{
    public sealed class ConfigFileOption
    {
        public string DisplayName { get; init; } = string.Empty;
        public string RemotePath { get; init; } = string.Empty;
    }

    private readonly SshService _sshService;
    private readonly ILogger? _logger;

    /// <summary>
    /// 各中间件配置文件路径映射
    /// 键：软件名称，值：不同操作系统下的配置文件路径列表（按优先级排序）
    /// </summary>
    private readonly Dictionary<string, Dictionary<OperatingSystemType, List<string>>> _configPaths = new()
    {
        ["MySQL"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/mysql/mysql.conf.d/mysqld.cnf",
                "/etc/mysql/my.cnf",
                "/etc/my.cnf",
                "/etc/my.cnf.d/mysql-server.cnf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/my.cnf",
                "/etc/my.cnf.d/mysql-server.cnf",
                "/etc/mysql/my.cnf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/mysql/mysql.conf.d/mysqld.cnf",
                "/etc/mysql/my.cnf",
                "/etc/my.cnf"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"C:\ProgramData\MySQL\MySQL Server *\my.ini",
                @"C:\Program Files\MySQL\MySQL Server *\my.ini"
            }
        },
        ["MariaDB"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/mysql/mariadb.conf.d/50-server.cnf",
                "/etc/my.cnf",
                "/etc/mysql/my.cnf",
                "/etc/my.cnf.d/server.cnf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/my.cnf",
                "/etc/my.cnf.d/server.cnf",
                "/etc/mysql/my.cnf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/mysql/mariadb.conf.d/50-server.cnf",
                "/etc/mysql/my.cnf",
                "/etc/my.cnf"
            }
        },
        ["Redis"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/redis/redis.conf",
                "/etc/redis.conf",
                "/etc/redis-server/redis.conf",
                "/usr/local/etc/redis.conf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/redis.conf",
                "/etc/redis/redis.conf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/redis/redis.conf",
                "/etc/redis-server/redis.conf"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"C:\Program Files\Redis\redis.windows.conf",
                @"C:\Program Files\Redis\redis.conf"
            }
        },
        ["Nginx"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/nginx/nginx.conf",
                "/usr/local/nginx/conf/nginx.conf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/nginx/nginx.conf",
                "/usr/local/nginx/conf/nginx.conf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/nginx/nginx.conf",
                "/etc/nginx/sites-available/default"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"C:\nginx\conf\nginx.conf",
                @"C:\Program Files\nginx\conf\nginx.conf"
            }
        },
        ["Elasticsearch"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/elasticsearch/elasticsearch.yml",
                "/opt/elasticsearch/config/elasticsearch.yml",
                "/usr/share/elasticsearch/config/elasticsearch.yml"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/elasticsearch/elasticsearch.yml",
                "/opt/elasticsearch/config/elasticsearch.yml"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/elasticsearch/elasticsearch.yml",
                "/opt/elasticsearch/config/elasticsearch.yml"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"C:\Program Files\Elastic\Elasticsearch\config\elasticsearch.yml",
                @"C:\elasticsearch\config\elasticsearch.yml"
            }
        },
        ["RabbitMQ"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/rabbitmq/rabbitmq.conf",
                "/usr/local/etc/rabbitmq/rabbitmq.conf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/rabbitmq/rabbitmq.conf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/rabbitmq/rabbitmq.conf"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"%APPDATA%\RabbitMQ\rabbitmq.conf",
                @"C:\Program Files\RabbitMQ Server\rabbitmq_server-*\etc\rabbitmq\rabbitmq.conf"
            }
        },
        ["Mosquitto"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/mosquitto/mosquitto.conf",
                "/etc/mosquitto/conf.d/remote-installer.conf"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/mosquitto/mosquitto.conf",
                "/etc/mosquitto/conf.d/remote-installer.conf"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/mosquitto/mosquitto.conf",
                "/etc/mosquitto/conf.d/remote-installer.conf"
            },
            [OperatingSystemType.Windows] = new List<string>
            {
                @"C:\Program Files\mosquitto\mosquitto.conf"
            }
        },
        ["Consul"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                "/etc/consul.d/consul.hcl",
                "/etc/consul.hcl"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                "/etc/consul.d/consul.hcl",
                "/etc/consul.hcl"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                "/etc/consul.d/consul.hcl",
                "/etc/consul.hcl"
            }
        },
        ["Traefik"] = new()
        {
            [OperatingSystemType.Linux] = new List<string>
            {
                TraefikMainConfigPath,
                "/usr/local/etc/traefik/traefik.yml",
                "/etc/traefik.yml",
                "/etc/traefik/traefik.toml",
                "/usr/local/etc/traefik/traefik.toml",
                "/etc/traefik.toml"
            },
            [OperatingSystemType.CentOS] = new List<string>
            {
                TraefikMainConfigPath,
                "/etc/traefik.yml",
                "/etc/traefik/traefik.toml",
                "/etc/traefik.toml"
            },
            [OperatingSystemType.Ubuntu] = new List<string>
            {
                TraefikMainConfigPath,
                "/etc/traefik.yml",
                "/etc/traefik/traefik.toml",
                "/etc/traefik.toml"
            }
        }
    };

    private const string TraefikMainConfigPath = "/etc/traefik/traefik.yml";
    private const string TraefikDynamicConfigPath = "/etc/traefik/dynamic.yml";
    private const string ElasticsearchMainConfigPath = "/etc/elasticsearch/elasticsearch.yml";
    private const string ElasticsearchJvmConfigPath = "/etc/elasticsearch/jvm.options";
    private const string ElasticsearchServiceConfigPath = "/etc/systemd/system/elasticsearch.service";
    private const string ElasticsearchUsrLibServiceConfigPath = "/usr/lib/systemd/system/elasticsearch.service";
    private const string ElasticsearchLibServiceConfigPath = "/lib/systemd/system/elasticsearch.service";
    private const string ElasticsearchAlternativeMainConfigPath = "/opt/elasticsearch/config/elasticsearch.yml";
    private const string ElasticsearchAlternativeJvmConfigPath = "/opt/elasticsearch/config/jvm.options";
    private const string ElasticsearchShareMainConfigPath = "/usr/share/elasticsearch/config/elasticsearch.yml";
    private const string ElasticsearchShareJvmConfigPath = "/usr/share/elasticsearch/config/jvm.options";

    private static readonly Dictionary<string, List<ConfigFileOption>> SwitchableConfigFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Traefik"] =
        [
            new() { DisplayName = "主配置 (traefik.yml)", RemotePath = TraefikMainConfigPath },
            new() { DisplayName = "动态配置 (dynamic.yml)", RemotePath = TraefikDynamicConfigPath },
            new() { DisplayName = "主配置 (/usr/local/etc, traefik.yml)", RemotePath = "/usr/local/etc/traefik/traefik.yml" },
            new() { DisplayName = "动态配置 (/usr/local/etc, dynamic.yml)", RemotePath = "/usr/local/etc/traefik/dynamic.yml" },
            new() { DisplayName = "主配置 (traefik.toml，旧版)", RemotePath = "/etc/traefik/traefik.toml" },
            new() { DisplayName = "动态配置 (dynamic.toml，旧版)", RemotePath = "/etc/traefik/dynamic.toml" },
            new() { DisplayName = "主配置 (/usr/local/etc, traefik.toml，旧版)", RemotePath = "/usr/local/etc/traefik/traefik.toml" },
            new() { DisplayName = "动态配置 (/usr/local/etc, dynamic.toml，旧版)", RemotePath = "/usr/local/etc/traefik/dynamic.toml" }
        ],
        ["Elasticsearch"] =
        [
            new() { DisplayName = "主配置 (/etc)", RemotePath = ElasticsearchMainConfigPath },
            new() { DisplayName = "JVM 配置 (/etc)", RemotePath = ElasticsearchJvmConfigPath },
            new() { DisplayName = "服务配置 (/etc/systemd)", RemotePath = ElasticsearchServiceConfigPath },
            new() { DisplayName = "服务配置 (/usr/lib/systemd)", RemotePath = ElasticsearchUsrLibServiceConfigPath },
            new() { DisplayName = "服务配置 (/lib/systemd)", RemotePath = ElasticsearchLibServiceConfigPath },
            new() { DisplayName = "主配置 (/opt)", RemotePath = ElasticsearchAlternativeMainConfigPath },
            new() { DisplayName = "JVM 配置 (/opt)", RemotePath = ElasticsearchAlternativeJvmConfigPath },
            new() { DisplayName = "主配置 (/usr/share)", RemotePath = ElasticsearchShareMainConfigPath },
            new() { DisplayName = "JVM 配置 (/usr/share)", RemotePath = ElasticsearchShareJvmConfigPath }
        ]
    };

    public ConfigurationService(SshService sshService, ILogger? logger = null)
    {
        _sshService = sshService;
        _logger = logger;
    }

    /// <summary>
    /// 获取指定中间件的配置文件路径
    /// </summary>
    /// <param name="host">远程主机</param>
    /// <param name="softwareName">软件名称</param>
    /// <param name="osType">操作系统类型</param>
    /// <returns>找到的配置文件路径，未找到返回null</returns>
    public async Task<string?> GetConfigFilePathAsync(RemoteHost host, string softwareName, OperatingSystemType osType, CancellationToken cancellationToken = default)
    {
        var normalizedSoftwareName = NormalizeSoftwareName(softwareName);
        if (!_configPaths.TryGetValue(normalizedSoftwareName, out var osPaths))
        {
            _logger?.Warning($"未找到软件 {softwareName} 的配置路径定义");
            return null;
        }

        // 先尝试特定操作系统的路径，再尝试通用Linux路径
        var pathsToCheck = new List<string>();
        if (osPaths.TryGetValue(osType, out var specificPaths))
        {
            pathsToCheck.AddRange(specificPaths);
        }

        if (osType != OperatingSystemType.Linux && osType != OperatingSystemType.Windows &&
            osPaths.TryGetValue(OperatingSystemType.Linux, out var linuxPaths))
        {
            pathsToCheck.AddRange(linuxPaths);
        }

        foreach (var path in pathsToCheck)
        {
            try
            {
                if (await _sshService.FileExistsAsync(path, cancellationToken))
                {
                    _logger?.Info($"找到 {softwareName} 配置文件: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"检查配置文件路径 {path} 失败: {ex.Message}");
            }
        }

        _logger?.Warning($"未找到 {softwareName} 的配置文件");
        return null;
    }

    public async Task<List<ConfigFileOption>> GetSwitchableConfigFilesAsync(string softwareName, CancellationToken cancellationToken = default)
    {
        var normalizedSoftwareName = NormalizeSoftwareName(softwareName);
        if (!SwitchableConfigFiles.TryGetValue(normalizedSoftwareName, out var candidates))
        {
            return new List<ConfigFileOption>();
        }

        var results = new List<ConfigFileOption>();
        foreach (var option in candidates)
        {
            try
            {
                if (await _sshService.FileExistsAsync(option.RemotePath, cancellationToken))
                {
                    results.Add(new ConfigFileOption
                    {
                        DisplayName = option.DisplayName,
                        RemotePath = option.RemotePath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"检查可切换配置文件路径 {option.RemotePath} 失败: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// 读取配置文件内容
    /// </summary>
    /// <param name="remotePath">远程配置文件路径</param>
    /// <returns>配置文件内容</returns>
    public async Task<string> ReadConfigAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        _logger?.Info($"读取配置文件: {remotePath}");
        return await _sshService.ReadTextFileAsync(remotePath, cancellationToken);
    }

    public Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        return _sshService.FileExistsAsync(remotePath, cancellationToken);
    }

    /// <summary>
    /// 备份配置文件
    /// </summary>
    /// <param name="remotePath">远程配置文件路径</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> BackupConfigAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var backupPath = $"{remotePath}.bak.{DateTime.Now:yyyyMMddHHmmss}";
        _logger?.Info($"备份配置文件 {remotePath} 到 {backupPath}");

        // 使用cp命令备份
        await _sshService.ExecuteCommandAsync($"cp -f \"{remotePath.Replace("\"", "\\\"")}\" \"{backupPath.Replace("\"", "\\\"")}\"",
            cancellationToken: cancellationToken, throwOnError: true);

        return backupPath;
    }

    /// <summary>
    /// 保存配置文件
    /// </summary>
    /// <param name="host">远程主机</param>
    /// <param name="remotePath">远程配置文件路径</param>
    /// <param name="content">配置内容</param>
    /// <param name="osType">操作系统类型</param>
    /// <param name="backup">是否自动备份</param>
    /// <returns>备份文件路径（如果执行了备份）</returns>
    public async Task<string?> SaveConfigAsync(RemoteHost host, string remotePath, string content, OperatingSystemType osType,
        bool backup = true, CancellationToken cancellationToken = default)
    {
        string? backupPath = null;

        if (backup)
        {
            backupPath = await BackupConfigAsync(remotePath, cancellationToken);
        }

        _logger?.Info($"保存配置文件: {remotePath}");
        await _sshService.UploadTextAsync(content, remotePath, osType, cancellationToken);

        // 设置正确的权限（保留原文件权限）
        try
        {
            var statResult = await _sshService.ExecuteCommandAsync($"stat -c \"%a\" \"{remotePath.Replace("\"", "\\\"")}\"",
                cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(statResult) && int.TryParse(statResult.Trim(), out var permissions))
            {
                await _sshService.ExecuteCommandAsync($"chmod {permissions} \"{remotePath.Replace("\"", "\\\"")}\"",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"设置文件权限失败: {ex.Message}");
        }

        return backupPath;
    }

    /// <summary>
    /// 重启对应服务使配置生效
    /// </summary>
    /// <param name="softwareName">软件名称</param>
    public async Task RestartServiceAsync(string softwareName, CancellationToken cancellationToken = default)
    {
        _logger?.Info($"重启 {softwareName} 服务以应用配置");

        var serviceName = GetServiceName(softwareName);

        var commands = new List<string>
        {
            $"$service = Get-Service -Name '{serviceName}' -ErrorAction SilentlyContinue; if ($service) {{ if ($service.Status -eq 'Running') {{ Restart-Service -Name '{serviceName}' -Force -ErrorAction Stop }} else {{ Start-Service -Name '{serviceName}' -ErrorAction Stop }}; Write-Output 'success' }}",
            $"systemctl restart {serviceName} && echo success",
            $"service {serviceName} restart && echo success",
            $"/etc/init.d/{serviceName} restart && echo success"
        };

        foreach (var cmd in commands)
        {
            try
            {
                var result = await _sshService.ExecuteCommandAsync(cmd, cancellationToken: cancellationToken, throwOnError: false);
                if (!string.IsNullOrWhiteSpace(result) && result.Contains("success", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.Success($"{softwareName} 服务重启成功");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"执行重启命令 '{cmd}' 失败: {ex.Message}");
            }
        }

        throw new Exception($"{softwareName} 服务重启失败，请手动重启");
    }

    private static string NormalizeSoftwareName(string softwareName)
    {
        if (string.IsNullOrWhiteSpace(softwareName))
        {
            return string.Empty;
        }

        var normalized = softwareName.Trim();
        return normalized switch
        {
            _ when normalized.Contains("mysql", StringComparison.OrdinalIgnoreCase) => "MySQL",
            _ when normalized.Contains("mariadb", StringComparison.OrdinalIgnoreCase) => "MariaDB",
            _ when normalized.Contains("redis", StringComparison.OrdinalIgnoreCase) => "Redis",
            _ when normalized.Contains("nginx", StringComparison.OrdinalIgnoreCase) => "Nginx",
            _ when normalized.Contains("elasticsearch", StringComparison.OrdinalIgnoreCase) => "Elasticsearch",
            _ when normalized.Contains("rabbitmq", StringComparison.OrdinalIgnoreCase) => "RabbitMQ",
            _ when normalized.Contains("mosquitto", StringComparison.OrdinalIgnoreCase) => "Mosquitto",
            _ when normalized.Contains("consul", StringComparison.OrdinalIgnoreCase) => "Consul",
            _ when normalized.Contains("traefik", StringComparison.OrdinalIgnoreCase) => "Traefik",
            _ => normalized
        };
    }

    private static string GetServiceName(string softwareName)
    {
        return NormalizeSoftwareName(softwareName) switch
        {
            "MySQL" => "mysql",
            "MariaDB" => "mariadb",
            "Redis" => "redis",
            "Nginx" => "nginx",
            "Elasticsearch" => "elasticsearch",
            "RabbitMQ" => "rabbitmq-server",
            "Mosquitto" => "mosquitto",
            "Consul" => "consul",
            "Traefik" => "traefik",
            _ => softwareName.Trim().ToLowerInvariant()
        };
    }
}