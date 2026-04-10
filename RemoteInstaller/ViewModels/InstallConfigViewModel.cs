using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 安装配置对话框 ViewModel
/// </summary>
public partial class InstallConfigViewModel : BaseViewModel
{
    private readonly ApplicationInfo _application;
    private readonly RemoteHost _host;
    private readonly ILogger _logger;
    private readonly Dictionary<string, string> _parameterValues = new();

    [ObservableProperty]
    private string _dialogTitle;

    [ObservableProperty]
    private string _applicationName;

    [ObservableProperty]
    private string _applicationVersion;

    [ObservableProperty]
    private string _targetHost;

    [ObservableProperty]
    private string _packageSource = "local";

    [ObservableProperty]
    private string _localPackagePath = string.Empty;

    [ObservableProperty]
    private bool _isInstallMode = true;

    /// <summary>
    /// 可用版本列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new();

    /// <summary>
    /// 选中的版本
    /// </summary>
    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    /// <summary>
    /// 是否本地包
    /// </summary>
    [ObservableProperty]
    private bool _isLocalPackage;

    /// <summary>
    /// 本地包版本（从文件名提取）
    /// </summary>
    [ObservableProperty]
    private string _localPackageVersion = string.Empty;

    [ObservableProperty]
    private string _localResourceHint = "请将安装资源放入 Scripts 对应应用目录后点击刷新检测。";

    public ObservableCollection<ParameterViewModel> ParameterViewModels { get; } = new();



    /// <summary>
    /// 详细输出 CanConfirm 的检查结果
    /// </summary>
    private void LogCanConfirmDetails()
    {
    }

    /// <summary>
    /// 初始化版本列表
    /// </summary>
    private void InitializeVersions()
    {
        if (_application.Versions != null && _application.Versions.Count > 0)
        {
            foreach (var version in _application.Versions)
            {
                AvailableVersions.Add(version);
            }

            // 默认选中第一个版本或应用的 SelectedVersion
            if (!string.IsNullOrEmpty(_application.SelectedVersion) && AvailableVersions.Contains(_application.SelectedVersion))
            {
                SelectedVersion = _application.SelectedVersion;
            }
            else if (AvailableVersions.Count > 0)
            {
                SelectedVersion = AvailableVersions[0];
            }
        }
        else
        {
            // 如果没有预置版本列表，使用应用默认版本
            if (!string.IsNullOrEmpty(_application.Version))
            {
                AvailableVersions.Add(_application.Version);
                SelectedVersion = _application.Version;
            }
        }
    }

    /// <summary>
    /// 从本地包文件名提取版本
    /// </summary>
    private void ExtractVersionFromLocalPackage()
    {
        if (string.IsNullOrEmpty(LocalPackagePath))
        {
            LocalPackageVersion = string.Empty;
            return;
        }

        try
        {
            var fileName = Path.GetFileName(LocalPackagePath).ToLower();

            // 尝试从文件名中提取版本号（格式：appname-version.ext 或 appname_version.ext）
            // 例如：mysql-8.0.35.rpm, redis_7.2.3.deb, nginx-1.24.0.tar.gz
            var version = ExtractVersionFromFileName(fileName);

            if (!string.IsNullOrEmpty(version))
            {
                LocalPackageVersion = version;

                // 如果是本地包模式，自动同步到 SelectedVersion 供脚本匹配使用
                if (!AvailableVersions.Contains(version))
                {
                    AvailableVersions.Add(version);
                }
                SelectedVersion = version;
            }
            else
            {
                LocalPackageVersion = "未知版本";
                // 此时不重置 SelectedVersion，保持用户之前的选择（如果有）
            }
        }
        catch (Exception ex)
        {
            LocalPackageVersion = "未知版本";
        }
    }

    /// <summary>
    /// 从文件名提取版本号
    /// </summary>
    private string ExtractVersionFromFileName(string fileName)
    {
        // 移除扩展名
        var nameWithoutExt = fileName;
        var extIndices = new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".deb", ".rpm" };
        foreach (var ext in extIndices)
        {
            if (nameWithoutExt.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - ext.Length);
                break;
            }
        }

        // 尝试匹配版本号模式（数字。数字.数字）
        // 例如：elasticsearch-8.11.0, redis_7.2.3, mysql-8.0.35
        var separators = new[] { '-', '_' };
        var parts = nameWithoutExt.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        // 查找看起来像版本号的最后一个部分（包含点）
        foreach (var part in parts.Reverse())
        {
            // 处理 v1.2.3 这种情况
            var cleanPart = part.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? part.Substring(1) : part;

            // 版本号通常包含点和数字，如 8.0.35, 7.2.3, 1.24.0
            // 排除架构信息（如 amd64, x86_64）
            if (cleanPart.Contains('.') &&
                cleanPart.All(c => char.IsDigit(c) || c == '.') &&
                !part.Contains("amd64") &&
                !part.Contains("x86_64") &&
                !part.Contains("i386") &&
                !part.Contains("arm64"))
            {
                return part;
            }
        }

        // 如果没有找到，尝试使用正则表达式匹配
        var versionPattern = System.Text.RegularExpressions.Regex.Match(nameWithoutExt, @"\d+\.\d+(?:\.\d+)?");
        if (versionPattern.Success)
        {
            var version = versionPattern.Value;
            return version;
        }

        return string.Empty;
    }

    /// <summary>
    /// SelectedVersion 改变时的处理
    /// </summary>
    partial void OnSelectedVersionChanged(string value)
    {
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// LocalPackageVersion 改变时的处理
    /// </summary>
    partial void OnLocalPackageVersionChanged(string value)
    {
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// PackageSource 改变时的处理
    /// </summary>
    partial void OnPackageSourceChanged(string value)
    {
        if (value == "local")
        {
            IsLocalPackage = true;
            // 本地包时，版本从文件名提取，不可修改
            if (!string.IsNullOrEmpty(LocalPackagePath))
            {
                ExtractVersionFromLocalPackage();
            }
        }
        else
        {
            IsLocalPackage = false;
            LocalPackageVersion = string.Empty;
        }

        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// LocalPackagePath 改变时的处理
    /// </summary>
    partial void OnLocalPackagePathChanged(string value)
    {
        if (PackageSource == "local" && !string.IsNullOrEmpty(value))
        {
            ExtractVersionFromLocalPackage();
        }

        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 初始化参数
    /// </summary>
    private void InitializeParameters()
    {
        if (_application.Parameters == null)
        {
            _application.Parameters = new List<InstallParameter>();
        }

        foreach (var param in _application.Parameters)
        {
            var paramVm = new ParameterViewModel(param);
            // 设置回调，当参数值变化时触发 CanExecute 重新评估
            paramVm.OnValueChangeNotify = () =>
            {
                NotifyCanExecuteChanged();
            };
            ParameterViewModels.Add(paramVm);
            _parameterValues[param.Key] = param.DefaultValue ?? string.Empty;
        }
    }

    private void InitializeLocalResourceDefaults()
    {
        if (TryResolveLocalPackage(out var packagePath, out var hint))
        {
            PackageSource = "local";
            LocalPackagePath = packagePath;
            _application.LocalPackagePath = packagePath;
            _application.UseLocalPackage = true;
            LocalResourceHint = hint;
            _logger?.Info($"默认使用 Scripts 本地资源：{packagePath}");
            return;
        }

        LocalPackagePath = string.Empty;
        LocalPackageVersion = string.Empty;
        _application.LocalPackagePath = string.Empty;
        _application.UseLocalPackage = false;
        LocalResourceHint = hint;
    }

    private bool TryResolveLocalPackage(out string packagePath, out string hint)
    {
        if (IsRabbitMq)
        {
            return TryResolveRabbitMqLocalPackage(out packagePath, out hint);
        }

        if (IsRedis)
        {
            return TryResolveRedisLocalPackage(out packagePath, out hint);
        }

        if (IsNginx)
        {
            return TryResolveNginxLocalPackage(out packagePath, out hint);
        }

        if (IsMySql)
        {
            return TryResolveMySqlLocalPackage(out packagePath, out hint);
        }

        if (IsConsul)
        {
            return TryResolveConsulLocalPackage(out packagePath, out hint);
        }

        if (IsTraefik)
        {
            return TryResolveTraefikLocalPackage(out packagePath, out hint);
        }

        var roots = GetApplicationScriptRoots();
        var packageExtensions = GetPackageExtensions();

        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var packageFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => packageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .ToList();

                var selectedPath = packageFiles.FirstOrDefault();
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    packagePath = selectedPath;
                    hint = $"已从 Scripts 目录自动匹配本地资源：{selectedPath}";
                    return true;
                }
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts 对应目录找到本地资源，请将安装包放入 Scripts/{_application.Name} 或 Scripts/{_application.Id} 后点击刷新检测。";
        return false;
    }

    private bool TryResolveRedisLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "redis-centos7",
            OperatingSystemType.Ubuntu => "redis-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Redis 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "redis-*.rpm",
            OperatingSystemType.Ubuntu => "redis-server*.deb",
            _ => string.Empty
        };

        foreach (var root in GetRedisScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 Redis 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Redis/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveNginxLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "nginx-centos7",
            OperatingSystemType.Ubuntu => "nginx-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Nginx 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "nginx-*.rpm",
            OperatingSystemType.Ubuntu => "nginx*.deb",
            _ => string.Empty
        };

        foreach (var root in GetNginxScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 Nginx 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Nginx/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveMySqlLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "mysql-centos7",
            OperatingSystemType.Ubuntu => "mysql-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 MySQL 本地资源目录。";
            return false;
        }

        string[] packagePatterns = _host.OsType switch
        {
            OperatingSystemType.CentOS => new[] { "mysql-community-server-*.rpm", "mysql-server-*.rpm" },
            OperatingSystemType.Ubuntu => new[] { "mysql-server*.deb", "mysql-community-server*.deb" },
            _ => Array.Empty<string>()
        };

        foreach (var root in GetMySqlScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = packagePatterns
                    .SelectMany(pattern => Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 MySQL 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/MySQL/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveRabbitMqLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "rabbitmq-centos7",
            OperatingSystemType.Ubuntu => "rabbitmq-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 RabbitMQ 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "rabbitmq-server-*.el7*.rpm",
            OperatingSystemType.Ubuntu => "rabbitmq-server*.deb",
            _ => string.Empty
        };

        foreach (var root in GetRabbitMqScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = selectedPath;
                hint = $"已从 Scripts 目录自动匹配 RabbitMQ 本地资源：{selectedPath}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/RabbitMQ/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveConsulLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "consul-centos7",
            OperatingSystemType.Ubuntu => "consul-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Consul 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "consul_*_linux_amd64.zip",
            OperatingSystemType.Ubuntu => "consul_*_linux_amd64.zip",
            _ => string.Empty
        };

        foreach (var root in GetConsulScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = "已从 Scripts 目录自动匹配 Consul 本地资源目录：" + root;
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Consul/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveTraefikLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "traefik-centos7",
            OperatingSystemType.Ubuntu => "traefik-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Traefik 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "traefik_v*_linux_amd64.tar.gz",
            OperatingSystemType.Ubuntu => "traefik_v*_linux_amd64.tar.gz",
            _ => string.Empty
        };

        foreach (var root in GetTraefikScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = "已从 Scripts 目录自动匹配 Traefik 本地资源目录：" + root;
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Traefik/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private IEnumerable<string> GetApplicationScriptRoots()
    {
        var appFolders = new[] { _application.Name, _application.Id }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts")
        };

        return baseRoots
            .SelectMany(root => appFolders.Select(folder => Path.Combine(root, folder)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetRabbitMqScriptRoots(string offlineFolder)
    {
        var rabbitMqRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "RabbitMQ", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "RabbitMQ", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "RabbitMQ", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "RabbitMQ", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "RabbitMQ", offlineFolder)
        };

        return rabbitMqRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetRedisScriptRoots(string offlineFolder)
    {
        var redisRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Redis", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Redis", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Redis", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Redis", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Redis", offlineFolder)
        };

        return redisRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetNginxScriptRoots(string offlineFolder)
    {
        var nginxRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Nginx", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Nginx", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Nginx", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Nginx", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Nginx", offlineFolder)
        };

        return nginxRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetMySqlScriptRoots(string offlineFolder)
    {
        var mySqlRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "MySQL", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "MySQL", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "MySQL", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "MySQL", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "MySQL", offlineFolder)
        };

        return mySqlRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetConsulScriptRoots(string offlineFolder)
    {
        var consulRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Consul", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Consul", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Consul", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Consul", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Consul", offlineFolder)
        };

        return consulRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetTraefikScriptRoots(string offlineFolder)
    {
        var traefikRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Traefik", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Traefik", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Traefik", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Traefik", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Traefik", offlineFolder)
        };

        return traefikRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string[] GetPackageExtensions()
    {
        return _host.OsType == OperatingSystemType.Windows
            ? new[] { ".zip", ".msi", ".exe" }
            : new[] { ".tar.gz", ".tar.xz", ".tgz", ".zip", ".rpm", ".deb" };
    }

    private bool IsVersionMatch(string path, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return Path.GetFileName(path).Contains(version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取参数值
    /// </summary>
    public Dictionary<string, string> GetParameters()
    {
        var result = new Dictionary<string, string>();

        foreach (var paramVm in ParameterViewModels)
        {
            // 处理布尔类型参数
            if (paramVm.Type == ParameterType.Boolean)
            {
                result[paramVm.Key] = paramVm.BoolValue ? "true" : "false";
            }
            else
            {
                result[paramVm.Key] = paramVm.Value;
            }
        }

        // 添加版本信息
        if (IsLocalPackage)
        {
            result["version"] = LocalPackageVersion;
        }
        else
        {
            result["version"] = SelectedVersion;
        }

        return result;
    }

    /// <summary>
    /// 确认安装
    /// </summary>
    public RelayCommand ConfirmCommand { get; private set; }

    public InstallConfigViewModel(ApplicationInfo application, RemoteHost host, ILogger logger)
    {
        _application = application;
        _host = host;
        _logger = logger;

        DialogTitle = $"安装 {application.Name} {application.Version}";
        ApplicationName = application.Name;
        ApplicationVersion = application.Version;
        TargetHost = host.Name;

        // 先初始化版本和参数，再创建命令
        InitializeVersions();
        InitializeParameters();
        InitializeLocalResourceDefaults();

        // 初始化 ConfirmCommand（在 InitializeVersions 和 InitializeParameters 之后）
        ConfirmCommand = new RelayCommand(Confirm, CanConfirmGetter);

        // 监听 IsBusy 变化（因为 IsBusy 在基类中定义）
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
            {
                NotifyCanExecuteChanged();
            }
        };
    }

    /// <summary>
    /// CanConfirm 的 getter 方法（用于 RelayCommand）
    /// </summary>
    private bool CanConfirmGetter()
    {
        bool canExecute = !IsBusy && ValidateParameters();
        return canExecute;
    }

    /// <summary>
    /// 是否允许确认（用于 UI 按钮启用/禁用）
    /// </summary>
    public bool CanConfirm => CanConfirmGetter();

    /// <summary>
    /// 通知命令重新评估 CanExecute
    /// </summary>
    private void NotifyCanExecuteChanged()
    {
        // 通过 IRelayCommand 接口调用，避免重复创建实例
        (ConfirmCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    private void Confirm()
    {
        if (!ValidateParameters())
        {
            return;
        }

        // 设置版本信息到 ApplicationInfo
        if (IsLocalPackage)
        {
            _application.LocalPackagePath = LocalPackagePath;
            _application.UseLocalPackage = true;
            _application.SelectedVersion = LocalPackageVersion;
            _logger?.Info($"使用本地安装包：{LocalPackagePath}");
        }
        else
        {
            _application.LocalPackagePath = string.Empty;
            _application.UseLocalPackage = false;
            _application.SelectedVersion = SelectedVersion;
            _logger?.Info($"在线安装版本：{SelectedVersion}");
        }

        // 关闭对话框，返回 true 表示确认
        CloseDialog(true);
    }

    /// <summary>
    /// 判断是否是 Redis 应用
    /// </summary>
    private bool IsRedis => _application.Id?.ToLower() == "redis" || _application.Name?.ToLower() == "redis";

    /// <summary>
    /// 判断是否是 RabbitMQ 应用
    /// </summary>
    private bool IsRabbitMq => _application.Id?.ToLower() == "rabbitmq" || _application.Name?.ToLower() == "rabbitmq";

    /// <summary>
    /// 判断是否是 Nginx 应用
    /// </summary>
    private bool IsNginx => _application.Id?.ToLower() == "nginx" || _application.Name?.ToLower() == "nginx";

    /// <summary>
    /// 判断是否是 MySQL 应用
    /// </summary>
    private bool IsMySql => _application.Id?.ToLower() == "mysql" || _application.Name?.ToLower() == "mysql";

    /// <summary>
    /// 判断是否是 Consul 应用
    /// </summary>
    private bool IsConsul => _application.Id?.ToLower() == "consul" || _application.Name?.ToLower() == "consul";

    /// <summary>
    /// 判断是否是 Traefik 应用
    /// </summary>
    private bool IsTraefik => _application.Id?.ToLower() == "traefik" || _application.Name?.ToLower() == "traefik";

    /// <summary>
    /// 验证参数
    /// </summary>
    private bool ValidateParameters()
    {
        bool hasError = false;
        string? errorMessage = null;

        if (PackageSource == "local")
        {
            if (string.IsNullOrWhiteSpace(LocalPackagePath))
            {
                SetError(LocalResourceHint);
                return false;
            }

            if (!File.Exists(LocalPackagePath) && !Directory.Exists(LocalPackagePath))
            {
                SetError($"Scripts 对应目录中的本地资源不存在：{LocalPackagePath}");
                return false;
            }
        }

        foreach (var paramVm in ParameterViewModels)
        {
            // 检查是否必填
            if (paramVm.Parameter.Required && string.IsNullOrWhiteSpace(paramVm.Value))
            {
                errorMessage = $"{paramVm.Parameter.Name} 是必填项";
                hasError = true;
                break;
            }

            // 对于 Redis 的密码字段，即使 Required=true 也跳过验证（Redis 默认不需要密码）
            if (IsRedis && paramVm.Key?.ToLower() == "password" && string.IsNullOrWhiteSpace(paramVm.Value))
            {
                continue;
            }

            // 端口范围验证
            if (ParameterType.Port == paramVm.Parameter.Type)
            {
                if (!string.IsNullOrWhiteSpace(paramVm.Value) && int.TryParse(paramVm.Value, out var port))
                {
                    if (paramVm.Parameter.MinValue > 0 && paramVm.Parameter.MaxValue > 0)
                    {
                        if (port < paramVm.Parameter.MinValue || port > paramVm.Parameter.MaxValue)
                        {
                            errorMessage = $"{paramVm.Parameter.Name} 范围：{paramVm.Parameter.MinValue}-{paramVm.Parameter.MaxValue}";
                            hasError = true;
                            break;
                        }
                    }
                }
            }
        }

        if (hasError)
        {
            SetError(errorMessage);
            return false;
        }

        ClearError();
        return true;
    }

    /// <summary>
    /// 刷新 Scripts 本地资源检测结果
    /// </summary>
    [RelayCommand]
    private void BrowsePackagePath()
    {
        try
        {
            InitializeLocalResourceDefaults();
            NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger?.Error($"刷新本地资源检测失败：{ex.Message}");
            MessageBox.Show($"刷新本地资源检测失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 取消
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 关闭对话框，返回 false 表示取消
        CloseDialog(false);
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    private void CloseDialog(bool? result)
    {
        foreach (var window in Application.Current.Windows)
        {
            if (window is Views.InstallConfigDialog dialog && dialog.ViewModel == this)
            {
                dialog.DialogResult = result;  // 设置 DialogResult
                dialog.Close();
                return;
            }
        }
    }
}

/// <summary>
/// 参数 ViewModel
/// </summary>
public partial class ParameterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private ParameterType _type;

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private string _hint;

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"选择 {Name}",
            Filter = "所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            Value = dialog.FileName;
        }
    }

    public InstallParameter Parameter { get; }

    /// <summary>
    /// 父 ViewModel 的回调，用于通知 CanExecute 变化
    /// </summary>
    public Action? OnValueChangeNotify { get; set; }

    public ParameterViewModel(InstallParameter parameter)
    {
        Parameter = parameter;
        Key = parameter.Key;
        Name = parameter.Name;
        Description = parameter.Description;
        Type = parameter.Type;
        Required = parameter.Required;
        Value = parameter.DefaultValue ?? string.Empty;

        // 处理布尔类型参数
        if (parameter.Type == ParameterType.Boolean)
        {
            BoolValue = bool.TryParse(parameter.DefaultValue, out var boolVal) ? boolVal : true;
        }

        Hint = BuildHint();

        // 监听 Value 属性变化
        PropertyChanged += ParameterViewModel_PropertyChanged;
    }

    private void ParameterViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Value))
        {
            OnValueChangeNotify?.Invoke();
        }
    }

    private string BuildHint()
    {
        var hints = new List<string>();
        
        if (Required) hints.Add("*必填");
        if (!string.IsNullOrEmpty(Parameter.DefaultValue)) hints.Add($"默认：{Parameter.DefaultValue}");
        if (Parameter.Type == ParameterType.Port && Parameter.MinValue > 0 && Parameter.MaxValue > 0)
        {
            hints.Add($"范围：{Parameter.MinValue}-{Parameter.MaxValue}");
        }

        return string.Join(" | ", hints);
    }
}
