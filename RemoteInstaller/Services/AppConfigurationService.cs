using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

public class AppConfigurationService : IDisposable
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly System.Timers.Timer _debounceTimer;
    private ApplicationConfiguration? _configuration;
    private FileSystemWatcher? _fileWatcher;
    private bool _disposed;

    public AppConfigurationService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var configCandidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "app-configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "app-configuration.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Scripts", "app-configuration.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "app-configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Scripts", "app-configuration.json"),
            Path.Combine("Scripts", "app-configuration.json")
        }
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        _configFilePath = configCandidates.FirstOrDefault(File.Exists)
            ?? configCandidates[0];

        _debounceTimer = new System.Timers.Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += async (_, _) => await Task.Run(LoadConfiguration);

        LoadConfiguration();
        StartFileWatcher();
    }

    public void LoadConfiguration()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _configuration = new ApplicationConfiguration();
                    return;
                }

                var json = File.ReadAllText(_configFilePath);
                var configuration = TryDeserializeConfiguration(json);

                if (configuration == null)
                {
                    var recoveredJson = RecoverUtf8Text(json);
                    configuration = TryDeserializeConfiguration(recoveredJson);
                }

                configuration ??= new ApplicationConfiguration();
                SanitizeConfiguration(configuration);
                _configuration = configuration;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppConfigurationService] Failed to load configuration: {ex.Message}");
                _configuration = new ApplicationConfiguration();
            }
        }
    }

    private void StartFileWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            var fileName = Path.GetFileName(_configFilePath);

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            _fileWatcher = new FileSystemWatcher(directory)
            {
                Filter = fileName,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnConfigFileChanged;
            _fileWatcher.Created += OnConfigFileChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppConfigurationService] Failed to start file watcher: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_debounceTimer.Enabled)
            {
                _debounceTimer.Stop();
            }

            _debounceTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppConfigurationService] Failed to process file change: {ex.Message}");
        }
    }

    public ObservableCollection<ApplicationConfig> GetApplications()
    {
        lock (_lock)
        {
            return _configuration?.Applications ?? new ObservableCollection<ApplicationConfig>();
        }
    }

    public ApplicationConfig? GetApplicationById(string appId)
    {
        lock (_lock)
        {
            return _configuration?.Applications?.FirstOrDefault(a => a.Id == appId);
        }
    }

    public ApplicationConfig? GetApplicationByName(string appName)
    {
        lock (_lock)
        {
            return _configuration?.Applications?.FirstOrDefault(a =>
                a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ObservableCollection<VersionConfig> GetVersions(string appId)
    {
        lock (_lock)
        {
            var app = GetApplicationById(appId);
            return app?.Versions ?? new ObservableCollection<VersionConfig>();
        }
    }

    public ObservableCollection<ParameterConfig> GetParameters(string appId, string version)
    {
        lock (_lock)
        {
            var app = GetApplicationById(appId);
            if (app == null)
            {
                return new ObservableCollection<ParameterConfig>();
            }

            var versionConfig = app.Versions?.FirstOrDefault(v => v.Version == version);
            return versionConfig?.Parameters ?? new ObservableCollection<ParameterConfig>();
        }
    }

    public string GetInstallScript(string appId, OperatingSystemType osType)
    {
        lock (_lock)
        {
            var app = GetApplicationById(appId);
            if (app == null)
            {
                return string.Empty;
            }

            return osType == OperatingSystemType.Windows
                ? app.Scripts?.Install?.Windows ?? string.Empty
                : app.Scripts?.Install?.Linux ?? string.Empty;
        }
    }

    public string GetUninstallScript(string appId, OperatingSystemType osType)
    {
        lock (_lock)
        {
            var app = GetApplicationById(appId);
            if (app == null)
            {
                return string.Empty;
            }

            return osType == OperatingSystemType.Windows
                ? app.Scripts?.Uninstall?.Windows ?? string.Empty
                : app.Scripts?.Uninstall?.Linux ?? string.Empty;
        }
    }

    public string GetDetectScript(string appId, OperatingSystemType osType)
    {
        lock (_lock)
        {
            var app = GetApplicationById(appId);
            if (app == null)
            {
                return string.Empty;
            }

            return osType == OperatingSystemType.Windows
                ? app.Scripts?.Detect?.Windows ?? string.Empty
                : app.Scripts?.Detect?.Linux ?? string.Empty;
        }
    }

    public ObservableCollection<string> GetCategories()
    {
        lock (_lock)
        {
            var categories = new HashSet<string>();
            foreach (var app in _configuration?.Applications ?? [])
            {
                if (!string.IsNullOrEmpty(app.Category))
                {
                    categories.Add(app.Category);
                }
            }

            return new ObservableCollection<string>(categories);
        }
    }

    public ObservableCollection<ApplicationConfig> GetApplicationsByCategory(string category)
    {
        lock (_lock)
        {
            if (category == "\u5168\u90e8")
            {
                return _configuration?.Applications ?? new ObservableCollection<ApplicationConfig>();
            }

            var apps = _configuration?.Applications;
            if (apps == null)
            {
                return new ObservableCollection<ApplicationConfig>();
            }

            return new ObservableCollection<ApplicationConfig>(apps.Where(a => a.Category == category));
        }
    }

    public ObservableCollection<ApplicationConfig> SearchApplications(string searchText)
    {
        lock (_lock)
        {
            var apps = _configuration?.Applications;
            if (apps == null)
            {
                return new ObservableCollection<ApplicationConfig>();
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return apps;
            }

            var search = searchText.ToLower();
            return new ObservableCollection<ApplicationConfig>(apps.Where(a =>
                a.Name.ToLower().Contains(search) ||
                a.Description.ToLower().Contains(search) ||
                a.Category.ToLower().Contains(search)));
        }
    }

    private ApplicationConfiguration? TryDeserializeConfiguration(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ApplicationConfiguration>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string RecoverUtf8Text(string text)
    {
        try
        {
            return Encoding.UTF8.GetString(Encoding.GetEncoding(936).GetBytes(text));
        }
        catch
        {
            return text;
        }
    }

    private static void SanitizeConfiguration(ApplicationConfiguration configuration)
    {
        var appDefaults = new Dictionary<string, (string Category, string Description, string Icon)>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = ("\u6570\u636e\u5e93", "\u6d41\u884c\u7684\u5f00\u6e90\u5173\u7cfb\u578b\u6570\u636e\u5e93\u7ba1\u7406\u7cfb\u7edf", "\U0001F42C"),
            ["mariadb"] = ("\u6570\u636e\u5e93", "\u517c\u5bb9 MySQL \u534f\u8bae\u7684\u5f00\u6e90\u5173\u7cfb\u578b\u6570\u636e\u5e93", "\U0001F9AD"),
            ["redis"] = ("\u6570\u636e\u5e93", "\u5f00\u6e90\u5185\u5b58\u6570\u636e\u7ed3\u6784\u5b58\u50a8\uff0c\u7528\u4f5c\u6570\u636e\u5e93\u3001\u7f13\u5b58\u548c\u6d88\u606f\u4ee3\u7406", "\U0001F534"),
            ["nginx"] = ("Web \u670d\u52a1", "\u9ad8\u6027\u80fd HTTP \u548c\u53cd\u5411\u4ee3\u7406\u670d\u52a1\u5668", "\U0001F310"),
            ["elasticsearch"] = ("\u6570\u636e\u5e93", "\u5206\u5e03\u5f0f RESTful \u641c\u7d22\u548c\u5206\u6790\u5f15\u64ce", "\U0001F50D"),
            ["rabbitmq"] = ("\u4e2d\u95f4\u4ef6", "\u5f00\u6e90\u6d88\u606f\u4ee3\u7406\u8f6f\u4ef6", "\U0001F430"),
            ["mosquitto"] = ("中间件", "轻量级 MQTT 消息代理，支持离线安装与基础认证", "📡"),
            ["consul"] = ("\u4e2d\u95f4\u4ef6", "\u670d\u52a1\u53d1\u73b0\u3001KV \u5b58\u50a8\u548c\u5065\u5eb7\u68c0\u67e5\u5e73\u53f0", "\U0001F9ED"),
            ["traefik"] = ("Web \u670d\u52a1", "\u4e91\u539f\u751f\u53cd\u5411\u4ee3\u7406\u4e0e\u8fb9\u7f18\u7f51\u5173", "\U0001F6E3\uFE0F")
        };

        var parameterDefaults = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Port"] = "MySQL \u670d\u52a1\u7aef\u53e3",
                ["Root Password"] = "root \u7528\u6237\u5bc6\u7801",
                ["Data Directory"] = "\u6570\u636e\u76ee\u5f55\u8def\u5f84"
            },
            ["mariadb"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Port"] = "MariaDB \u670d\u52a1\u7aef\u53e3",
                ["Root Password"] = "root \u7528\u6237\u5bc6\u7801",
                ["Allow Remote"] = "\u662f\u5426\u5141\u8bb8 root \u7528\u6237\u8fdc\u7a0b\u8fde\u63a5",
                ["Data Directory"] = "\u6570\u636e\u76ee\u5f55\u8def\u5f84"
            },
            ["redis"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Port"] = "Redis \u670d\u52a1\u7aef\u53e3",
                ["Max Memory"] = "\u6700\u5927\u5185\u5b58\u9650\u5236",
                ["Password"] = "\u8bbf\u95ee\u5bc6\u7801\uff08\u53ef\u9009\uff09"
            },
            ["nginx"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTP Port"] = "HTTP \u7aef\u53e3",
                ["HTTPS Port"] = "HTTPS \u7aef\u53e3",
                ["Worker Processes"] = "\u5de5\u4f5c\u8fdb\u7a0b\u6570"
            },
            ["elasticsearch"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTP Port"] = "HTTP \u7aef\u53e3",
                ["Transport Port"] = "\u4f20\u8f93\u7aef\u53e3",
                ["Heap Size"] = "\u5806\u5185\u5b58\u5927\u5c0f"
            },
            ["rabbitmq"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AMQP Port"] = "AMQP \u7aef\u53e3",
                ["Management Port"] = "\u7ba1\u7406\u754c\u9762\u7aef\u53e3",
                ["Username"] = "\u7528\u6237\u540d",
                ["Password"] = "\u5bc6\u7801",
                ["Enable Remote Access"] = "\u662f\u5426\u5141\u8bb8\u8fdc\u7a0b\u8bbf\u95ee\uff08\u9ed8\u8ba4\u542f\u7528\uff09"
            },
            ["mosquitto"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["MQTT Port"] = "MQTT TCP 端口",
                ["Username"] = "\u7528\u6237\u540d",
                ["Password"] = "\u5bc6\u7801"
            },
            ["consul"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTP Port"] = "Consul HTTP API 端口",
                ["DNS Port"] = "Consul DNS 端口",
                ["Bind Addr"] = "Consul 监听地址",
                ["Data Dir"] = "Consul 数据目录",
                ["Node Name"] = "Consul 节点名称",
                ["UI Enabled"] = "是否启用 Consul Web UI"
            },
            ["traefik"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTP Port"] = "Traefik HTTP 入口端口",
                ["HTTPS Port"] = "Traefik HTTPS 入口端口",
                ["Dashboard Port"] = "Traefik Dashboard 端口",
                ["Install Dir"] = "Traefik 安装目录",
                ["Config Dir"] = "Traefik 配置目录",
                ["Enable Dashboard"] = "是否启用 Dashboard"
            }
        };

        foreach (var app in configuration.Applications)
        {
            if (appDefaults.TryGetValue(app.Id, out var appMeta))
            {
                app.Category = appMeta.Category;
                app.Description = appMeta.Description;
                app.Icon = appMeta.Icon;
            }

            if (!parameterDefaults.TryGetValue(app.Id, out var parameterDescriptions))
            {
                continue;
            }

            foreach (var version in app.Versions)
            {
                foreach (var parameter in version.Parameters)
                {
                    if (parameterDescriptions.TryGetValue(parameter.Name, out var description))
                    {
                        parameter.Description = description;
                    }

                    if (app.Id.Equals("mosquitto", StringComparison.OrdinalIgnoreCase)
                        && (parameter.Name.Equals("Username", StringComparison.OrdinalIgnoreCase)
                            || parameter.Name.Equals("Password", StringComparison.OrdinalIgnoreCase)))
                    {
                        parameter.Required = false;
                        parameter.Default = string.Empty;
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fileWatcher?.EnableRaisingEvents = false;
        _fileWatcher?.Dispose();
        _debounceTimer.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}


