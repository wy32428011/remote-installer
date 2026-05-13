using System;
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class ElasticsearchStatusTests
{
    [Fact]
    public void CheckStatusLinuxScript_NormalizesRunningStateAsInstalledBeforeMachineReadableOutput()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "check_status_linux.sh");

        var normalizeIndex = script.IndexOf("if [ \"$is_running\" = \"true\" ] && [ \"$is_installed\" = \"false\" ]; then", StringComparison.Ordinal);
        var machineReadableIndex = script.IndexOf("echo \"--- MACHINE READABLE ---\"", StringComparison.Ordinal);

        Assert.True(normalizeIndex >= 0, "Elasticsearch 状态脚本应在输出前将 RUNNING=true 归一为 INSTALLED=true。");
        Assert.True(machineReadableIndex >= 0, "未找到 Elasticsearch 状态脚本的机器可读输出段。");
        Assert.True(normalizeIndex < machineReadableIndex, "归一化逻辑必须发生在机器可读状态输出之前。");
        Assert.Contains("is_installed=\"true\"", script);
    }

    [Fact]
    public void InstallerService_NormalizesParsedRunningStateAsInstalled()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        var parseCheckOutput = ExtractMethod(installerService, "private void ParseCheckOutput");

        Assert.Contains("ApplicationStatusNormalizer.ApplyStatusEvents(status, events);", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.BuildEvidence(events);", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.Normalize(status, evidence);", parseCheckOutput);
        Assert.DoesNotContain("NormalizeApplicationStatus", installerService);
    }

    [Fact]
    public void CheckStatusLinuxScript_DoesNotUseRawPgrepThatCanMatchItsOwnHereDocCommand()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "check_status_linux.sh");

        Assert.DoesNotContain("es_pid=$(pgrep -f \"elasticsearch\"", script);
        Assert.Contains("find_es_pids()", script);
        Assert.Contains("STATUS_SCRIPT_PID=$$", script);
        Assert.Contains("/proc/$pid/cmdline", script);
        Assert.Contains("org\\.elasticsearch\\.bootstrap\\.Elasticsearch", script);
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
