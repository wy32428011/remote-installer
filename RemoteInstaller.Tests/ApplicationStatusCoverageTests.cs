using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace RemoteInstaller.Tests;

public class ApplicationStatusCoverageTests
{
    [Fact]
    public void AppConfiguration_IncludesEveryBundledLinuxStatusApplication()
    {
        var projectRoot = GetProjectRoot();
        using var document = JsonDocument.Parse(ReadProjectFile("Scripts", "app-configuration.json"));
        var configuredIds = document.RootElement
            .GetProperty("applications")
            .EnumerateArray()
            .Select(app => app.GetProperty("id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Directory
            .EnumerateDirectories(Path.Combine(projectRoot, "RemoteInstaller", "Scripts"))
            .Where(directory => File.Exists(Path.Combine(directory, "check_status_linux.sh")))
            .Select(directory => new DirectoryInfo(directory).Name)
            .Where(appName => !configuredIds.Contains(appName))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "有 Linux 状态脚本的内置应用必须进入 app-configuration.json，否则安装运行后不会进入 UI 状态刷新范围: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void MainViewModel_HardcodedFallbackIncludesEveryBundledLinuxStatusApplication()
    {
        var projectRoot = GetProjectRoot();
        var viewModelSource = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        var missing = Directory
            .EnumerateDirectories(Path.Combine(projectRoot, "RemoteInstaller", "Scripts"))
            .Where(directory => File.Exists(Path.Combine(directory, "check_status_linux.sh")))
            .Select(directory => new DirectoryInfo(directory).Name)
            .Where(appName =>
                !viewModelSource.Contains($"Id = \"{appName}\"", StringComparison.OrdinalIgnoreCase) ||
                !viewModelSource.Contains($"\"{appName.ToLowerInvariant()}\" => new ApplicationInfo", StringComparison.OrdinalIgnoreCase) ||
                !viewModelSource.Contains($"Scripts/{appName}/check_status_linux.sh", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "硬编码兜底也必须覆盖所有内置状态脚本应用，避免配置加载失败时应用不显示: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void InstallerService_VerifiesRemoteStatusAfterInstallScriptThrowsBeforeFailingTask()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var installAsync = ExtractMethod(source, "public async Task<InstallTask> InstallAsync");

        Assert.Contains("RemoteCommandResult? scriptResult = null", installAsync);
        Assert.Contains("catch (Exception ex)", installAsync);
        Assert.Contains("scriptResult = new RemoteCommandResult", installAsync);
        Assert.Contains("OperationDecisionPolicy.DecideInstall", installAsync);
        Assert.Contains("decision.HasWarning", installAsync);
        Assert.Contains("task.Fail(decision.Message)", installAsync);
    }

    [Fact]
    public void InstallerService_InstallPathUsesOperationDecisionPolicy()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var installAsync = ExtractMethod(source, "public async Task<InstallTask> InstallAsync");

        Assert.Contains("OperationDecisionPolicy.DecideInstall", installAsync);
        Assert.Contains("RemoteCommandResult?", installAsync);
        Assert.Contains("scriptResult", installAsync);
    }

    [Fact]
    public void RedisInstallScript_EmitsFinalMachineReadableInstalledAndRunningState()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Redis", "install_linux.sh");

        Assert.Contains("--- MACHINE READABLE ---", script);
        Assert.Contains("INSTALLED: true", script);
        Assert.Contains("RUNNING:", script);
        Assert.Contains("STAGE:SUCCESS", script);
    }

    [Fact]
    public void NacosStatusScript_DoesNotUseRawPgrepThatCanMatchItsOwnHereDocCommand()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nacos", "check_status_linux.sh");

        Assert.DoesNotContain("nacos_pid=$(pgrep -f", script);
        Assert.Contains("find_nacos_pids()", script);
        Assert.Contains("STATUS_SCRIPT_PID=$$", script);
        Assert.Contains("/proc/$pid/cmdline", script);
        Assert.Contains("nacos-server\\.jar", script);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到方法签名：{signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"未找到方法体起始：{signature}");

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
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        throw new InvalidOperationException($"未找到方法体结束：{signature}");
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
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
}
