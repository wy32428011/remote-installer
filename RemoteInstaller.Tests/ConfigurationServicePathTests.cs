using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace RemoteInstaller.Tests;

public class ConfigurationServicePathTests
{
    private static string GetProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RemoteInstaller.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 RemoteInstaller.sln，无法定位项目根目录。");
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ConfigurationService_DefinesConsulConfigPaths()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("[\"Consul\"] = new()", service);
        Assert.Contains("/etc/consul.d/consul.hcl", service);
    }

    [Fact]
    public void ConfigurationService_DefinesMariaDbConfigPaths()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("[\"MariaDB\"] = new()", service);
        Assert.Contains("/etc/mysql/mariadb.conf.d/50-server.cnf", service);
        Assert.Contains("/etc/my.cnf.d/server.cnf", service);
    }

    [Fact]
    public void ConfigurationService_DefinesTraefikConfigPaths()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("[\"Traefik\"] = new()", service);
        Assert.Contains("/etc/traefik/traefik.yml", service);
        Assert.Contains("/usr/local/etc/traefik/traefik.yml", service);
        Assert.Contains("/etc/traefik/traefik.toml", service);
    }

    [Fact]
    public void ConfigurationService_DefinesMosquittoConfigPaths()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("[\"Mosquitto\"] = new()", service);
        Assert.Contains("/etc/mosquitto/mosquitto.conf", service);
        Assert.Contains("/etc/mosquitto/conf.d/remote-installer.conf", service);
        Assert.Contains(@"C:\Program Files\mosquitto\mosquitto.conf", service);
    }

    [Fact]
    public void ConfigurationService_NormalizesMosquittoDisplayNameForConfigLookupAndRestart()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("NormalizeSoftwareName", service);
        Assert.Contains("normalized.Contains(\"mosquitto\"", service);
        Assert.Contains("GetServiceName(softwareName)", service);
        Assert.Contains("\"Mosquitto\" => \"mosquitto\"", service);
    }

    [Fact]
    public void ConfigurationService_DefinesSwitchableTraefikConfigFiles()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("/etc/traefik/dynamic.yml", service);
        Assert.Contains("/usr/local/etc/traefik/dynamic.yml", service);
        Assert.Contains("/etc/traefik/dynamic.toml", service);
        Assert.Contains("/usr/local/etc/traefik/dynamic.toml", service);
        Assert.Contains("GetSwitchableConfigFilesAsync", service);
    }

    [Fact]
    public void ConfigurationService_DefinesSwitchableElasticsearchConfigFiles()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        var switchableBlockStart = service.IndexOf("[\"Elasticsearch\"] =", StringComparison.Ordinal);
        var switchableListEnd = service.IndexOf("        ]", switchableBlockStart, StringComparison.Ordinal);
        var switchableBlockEnd = service.IndexOf("    };", switchableListEnd, StringComparison.Ordinal);
        var elasticsearchSwitchableBlock = service.Substring(switchableBlockStart, switchableBlockEnd - switchableBlockStart);

        Assert.Contains("/etc/elasticsearch/elasticsearch.yml", elasticsearchSwitchableBlock);
        Assert.Contains("/opt/elasticsearch/config/elasticsearch.yml", elasticsearchSwitchableBlock);
        Assert.Contains("/usr/share/elasticsearch/config/elasticsearch.yml", elasticsearchSwitchableBlock);
        Assert.Contains("/etc/elasticsearch/jvm.options", elasticsearchSwitchableBlock);
        Assert.Contains("/opt/elasticsearch/config/jvm.options", elasticsearchSwitchableBlock);
        Assert.Contains("/usr/share/elasticsearch/config/jvm.options", elasticsearchSwitchableBlock);
        Assert.Contains("/etc/systemd/system/elasticsearch.service", elasticsearchSwitchableBlock);
        Assert.Contains("/usr/lib/systemd/system/elasticsearch.service", elasticsearchSwitchableBlock);
        Assert.Contains("/lib/systemd/system/elasticsearch.service", elasticsearchSwitchableBlock);
        Assert.Contains("主配置 (/etc)", elasticsearchSwitchableBlock);
        Assert.Contains("主配置 (/opt)", elasticsearchSwitchableBlock);
        Assert.Contains("主配置 (/usr/share)", elasticsearchSwitchableBlock);
        Assert.Contains("JVM 配置 (/etc)", elasticsearchSwitchableBlock);
        Assert.Contains("JVM 配置 (/opt)", elasticsearchSwitchableBlock);
        Assert.Contains("JVM 配置 (/usr/share)", elasticsearchSwitchableBlock);
        Assert.Contains("服务配置 (/etc/systemd)", elasticsearchSwitchableBlock);
    }

    [Fact]
    public void AppConfiguration_DefinesMosquittoApplication()
    {
        var appConfiguration = ReadProjectFile("Scripts", "app-configuration.json");

        Assert.Contains("\"id\": \"mosquitto\"", appConfiguration);
        Assert.Contains("\"name\": \"Mosquitto\"", appConfiguration);
        Assert.Contains("Scripts/Mosquitto/install_linux.sh", appConfiguration);
        Assert.Contains("PASSWORD_FILE={password_file}", appConfiguration);
        Assert.Contains("-PasswordFile {password_file}", appConfiguration);
        Assert.Contains("\"version\": \"2.0.22\"", appConfiguration);
        Assert.Contains("\"version\": \"2.1.2\"", appConfiguration);
        Assert.Contains("\"version\": \"1.6.10\"", appConfiguration);
        Assert.Contains("Scripts/Mosquitto/check_status_linux.sh", appConfiguration);
        Assert.Contains("\"osSupport\": [\"CentOS\", \"Ubuntu\", \"Windows\"]", appConfiguration);
        Assert.Contains("\"required\": false", appConfiguration);
        Assert.Contains("用户名（留空则启用匿名访问；启用认证时需与密码同时填写）", appConfiguration);
        Assert.Contains("密码（留空则启用匿名访问；启用认证时需与用户名同时填写）", appConfiguration);
    }

    [Fact]
    public void AppConfiguration_UsesMachineReadableLinuxStatusScripts()
    {
        var projectRoot = GetProjectRoot();
        using var document = JsonDocument.Parse(ReadProjectFile("Scripts", "app-configuration.json"));
        var scriptFolders = Directory.GetDirectories(Path.Combine(projectRoot, "RemoteInstaller", "Scripts"))
            .Where(directory => File.Exists(Path.Combine(directory, "check_status_linux.sh")))
            .Select(directory => new DirectoryInfo(directory).Name)
            .ToList();

        foreach (var app in document.RootElement.GetProperty("applications").EnumerateArray())
        {
            var id = app.GetProperty("id").GetString() ?? string.Empty;
            var name = app.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? id : id;
            var scriptFolder = scriptFolders.FirstOrDefault(folder => IsSameAppName(folder, id) || IsSameAppName(folder, name));
            if (scriptFolder is null)
            {
                continue;
            }

            var linuxDetect = app
                .GetProperty("scripts")
                .GetProperty("detect")
                .GetProperty("linux")
                .GetString() ?? string.Empty;

            Assert.Contains($"Scripts/{scriptFolder}/check_status_linux.sh", linuxDetect);
        }
    }

    [Fact]
    public void AppConfiguration_UsesBundledLinuxUninstallScriptsWhenAvailable()
    {
        var projectRoot = GetProjectRoot();
        using var document = JsonDocument.Parse(ReadProjectFile("Scripts", "app-configuration.json"));
        var scriptFolders = Directory.GetDirectories(Path.Combine(projectRoot, "RemoteInstaller", "Scripts"))
            .Where(directory => File.Exists(Path.Combine(directory, "uninstall_linux.sh")))
            .Select(directory => new DirectoryInfo(directory).Name)
            .ToList();

        foreach (var app in document.RootElement.GetProperty("applications").EnumerateArray())
        {
            var id = app.GetProperty("id").GetString() ?? string.Empty;
            var name = app.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? id : id;
            var scriptFolder = scriptFolders.FirstOrDefault(folder => IsSameAppName(folder, id) || IsSameAppName(folder, name));
            if (scriptFolder is null)
            {
                continue;
            }

            var linuxUninstall = app
                .GetProperty("scripts")
                .GetProperty("uninstall")
                .GetProperty("linux")
                .GetString() ?? string.Empty;

            Assert.Contains($"Scripts/{scriptFolder}/uninstall_linux.sh", linuxUninstall);
        }
    }

    private static bool IsSameAppName(string left, string right)
    {
        static string Normalize(string value) => new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return Normalize(left) == Normalize(right);
    }

    [Fact]
    public void MainViewModel_DefinesMosquittoFallback()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("Id = \"mosquitto\"", viewModel);
        Assert.Contains("Name = \"Mosquitto\"", viewModel);
        Assert.Contains("Scripts/Mosquitto/check_status_linux.sh", viewModel);
        Assert.Contains("Scripts\\Mosquitto\\check_status_windows.ps1", viewModel);
        Assert.Contains("Versions = new List<string> { \"2.0.21\", \"2.0.22\", \"2.1.2\", \"1.6.10\" }", viewModel);
        Assert.Contains("DefaultValue = \"\"", viewModel);
        Assert.Contains("Required = false", viewModel);
        Assert.Contains("Mosquitto 登录用户名（留空则启用匿名访问；启用认证时需与密码同时填写）", viewModel);
    }

    [Fact]
    public void AppConfigurationService_DefinesMosquittoDefaults()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "AppConfigurationService.cs");

        Assert.Contains("[\"mosquitto\"] = new(StringComparer.OrdinalIgnoreCase)", service);
        Assert.Contains("MQTT TCP 端口", service);
        Assert.Contains("轻量级 MQTT 消息代理，支持离线安装与基础认证", service);
        Assert.Contains("app.Id.Equals(\"mosquitto\"", service);
        Assert.Contains("parameter.Required = false;", service);
        Assert.Contains("parameter.Default = string.Empty;", service);
        Assert.DoesNotContain("MQTT WebSocket 端口", service);
    }

    [Fact]
    public void ScriptsReadme_DefinesMosquittoOfflineLayout()
    {
        var readme = ReadProjectFile("RemoteInstaller", "Scripts", "README.md");

        Assert.Contains("RemoteInstaller/Scripts/Mosquitto/windows", readme);
        Assert.Contains("RemoteInstaller/Scripts/Mosquitto/mosquitto-ubuntu/22", readme);
        Assert.Contains("RemoteInstaller/Scripts/Mosquitto/mosquitto-ubuntu/24", readme);
        Assert.Contains("RemoteInstaller/Scripts/Mosquitto/mosquitto-centos7", readme);
        Assert.Contains("mosquitto_*.deb", readme);
        Assert.Contains("mosquitto-*.rpm", readme);
        Assert.Contains("mosquitto-*.zip", readme);
    }

    [Fact]
    public void InstallConfigViewModel_UsesOsVersionToValidateMySqlOfflineCompatibility()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("TryGetCompatibleMySqlOfflineFolder", viewModel);
        Assert.Contains("_host.OsVersion", viewModel);
        Assert.Contains("ubuntuMajor == 22", viewModel);
        Assert.Contains("ubuntuMajor == 24", viewModel);
        Assert.Contains("Ubuntu 22.04 和 Ubuntu 24.04", viewModel);
        Assert.Contains("Path.Combine(\"mysql-ubuntu\", \"22\")", viewModel);
        Assert.Contains("Path.Combine(\"mysql-ubuntu\", \"24\")", viewModel);
        Assert.Contains("centOsMajor != 7", viewModel);
        Assert.Contains("mysql-centos7", viewModel);
    }

    [Fact]
    public void InstallConfigViewModel_AllowsMySqlOfflineDirectoryToUseMysqlSubdirectory()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("Path.Combine(root, \"mysql\")", viewModel);
        Assert.Contains("SearchOption.TopDirectoryOnly", viewModel);
        Assert.Contains("packagePath = root;", viewModel);
    }

    [Fact]
    public void InstallConfigViewModel_UsesOsVersionToValidateMariaDbOfflineCompatibility()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("TryGetCompatibleMariaDbOfflineFolder", viewModel);
        Assert.Contains("Path.Combine(\"mariadb-ubuntu\", \"22\")", viewModel);
        Assert.Contains("Path.Combine(\"mariadb-ubuntu\", \"24\")", viewModel);
    }
}
