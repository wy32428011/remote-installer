using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class RedisOfflineSelectionTests
{
    private static ApplicationInfo CreateRedisApplication() => new()
    {
        Id = "redis",
        Name = "Redis",
        Version = "7.0.15",
        Versions = new List<string> { "7.0.15" },
        Parameters = new List<InstallParameter>()
    };

    [Fact]
    public void Ubuntu22RedisOfflineSelection_DoesNotUseBundledUbuntu24Packages()
    {
        var host = new RemoteHost
        {
            Name = "ubuntu22-host",
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "22.04"
        };

        var application = CreateRedisApplication();
        var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

        Assert.Equal("local", viewModel.PackageSource);
        Assert.False(application.UseLocalPackage);
        Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
        Assert.Contains("Redis Ubuntu 22", viewModel.LocalResourceHint);
    }

    [Fact]
    public void Ubuntu22RedisManualLegacyUbuntuDirectory_IsRejectedAsIncompatible()
    {
        var legacyUbuntu24Directory = Path.Combine(GetProjectRoot(), "RemoteInstaller", "Scripts", "Redis", "redis-ubuntu");
        Assert.True(Directory.Exists(legacyUbuntu24Directory), $"Redis 离线目录不存在：{legacyUbuntu24Directory}");

        var host = new RemoteHost
        {
            Name = "ubuntu22-host",
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "22.04"
        };

        var application = CreateRedisApplication();
        var viewModel = new InstallConfigViewModel(application, host, new LoggerService())
        {
            LocalPackagePath = legacyUbuntu24Directory
        };

        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void BatchInstall_RedisResolvesScriptsOfflinePackageBeforeStartingInstall()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var method = ExtractMethod(source, "private async Task<BatchTaskResult> InstallApplicationToHost");

        Assert.Contains("TryResolveRedisBatchLocalPackage", source);
        Assert.Contains("TryResolveRedisBatchLocalPackage(executionAppInfo, host", method);
        Assert.Contains("批量任务已匹配 Redis 本地资源", method);
    }

    [Fact]
    public void InstallLinuxScript_DoesNotRunGlobalDpkgConfigureAllDuringOfflineDebInstall()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Redis", "install_linux.sh");

        Assert.DoesNotContain("--configure -a", script);
        Assert.Contains("dpkg --force-confdef --force-confold -i \"${redis_tools_debs[@]}\"", script);
        Assert.Contains("dpkg --force-confdef --force-confold -i \"${redis_server_debs[@]}\"", script);
    }

    [Fact]
    public void CheckStatusAsync_LinuxLocalShellScriptRunsThroughBashStdin()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var scriptResolver = ReadProjectFile("RemoteInstaller", "Services", "Operations", "ScriptResolver.cs");
        var method = ExtractMethod(source, "public async Task<ApplicationStatus> CheckStatusAsync");

        Assert.Contains("BuildLinuxShellScriptCommand", method);
        Assert.Contains("bash -s <<'REMOTE_INSTALLER_CHECK_STATUS_SCRIPT'", scriptResolver);
        Assert.Contains("TrimStart('\\uFEFF')", scriptResolver);
    }

    [Fact]
    public void RedisMarketplaceConfiguration_UsesMachineReadableStatusScripts()
    {
        var config = ReadProjectFile("Scripts", "app-configuration.json");
        var root = JsonNode.Parse(config);
        var redis = root?["applications"]?.AsArray()
            .FirstOrDefault(app => string.Equals((string?)app?["id"], "redis", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(redis);
        Assert.Equal("Scripts/Redis/check_status_linux.sh", (string?)redis!["scripts"]?["detect"]?["linux"]);
        Assert.Equal("Scripts/Redis/check_status_windows.ps1", (string?)redis["scripts"]?["detect"]?["windows"]);
    }

    [Fact]
    public void RedisMarketplaceConfiguration_UsesBundledUninstallScripts()
    {
        var config = ReadProjectFile("Scripts", "app-configuration.json");
        var root = JsonNode.Parse(config);
        var redis = root?["applications"]?.AsArray()
            .FirstOrDefault(app => string.Equals((string?)app?["id"], "redis", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(redis);
        var linuxUninstall = (string?)redis!["scripts"]?["uninstall"]?["linux"] ?? string.Empty;
        Assert.Equal("bash Scripts/Redis/uninstall_linux.sh", linuxUninstall);
        Assert.DoesNotContain("cd /opt/redis-*", linuxUninstall);
        Assert.DoesNotContain("make uninstall", linuxUninstall);
    }

    [Fact]
    public void UninstallAsync_LoadsBundledScriptReferenceFromConfigurationFallback()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var method = ExtractMethod(source, "public async Task<InstallTask> UninstallAsync");

        Assert.Contains("TryResolveConfiguredScriptFilePath", source);
        Assert.Contains("var configuredScript = app.GetUninstallScript(host.OsType);", method);
        Assert.Contains("TryResolveConfiguredScriptFilePath(configuredScript", method);
        Assert.Contains("从配置引用加载卸载脚本", method);
    }

    [Fact]
    public void UninstallLinuxScript_RemovesGeneratedAndSymlinkedRedisSystemdUnits()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Redis", "uninstall_linux.sh");

        Assert.Contains("INIT_SCRIPTS=(", script);
        Assert.Contains("/etc/init.d/redis-server", script);
        Assert.Contains("update-rc.d -f \"$service_name\" remove", script);
        Assert.Contains("SYSTEMD_SERVICE_GLOBS=(", script);
        Assert.Contains("/etc/systemd/system/*.wants/redis-server.service", script);
        Assert.Contains("/run/systemd/generator*/redis-server.service", script);
    }

    [Fact]
    public void CheckStatusLinuxScript_DoesNotTreatOrphanedRedisServiceUnitAsInstalled()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Redis", "check_status_linux.sh");

        Assert.Contains("package_installed=\"false\"", script);
        Assert.Contains("service_only_stale=\"false\"", script);
        Assert.Contains("Redis 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理", script);
        Assert.Contains("SERVICE_ONLY_STALE: ${service_only_stale:-false}", script);
    }

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

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(braceStart, i - braceStart + 1);
                }
            }
        }

        throw new InvalidDataException($"Could not extract method body for: {signature}");
    }
}
