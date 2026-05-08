using System;
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class InstallerServiceUninstallDecisionPolicyTests
{
    [Fact]
    public void UninstallPathUsesOperationDecisionPolicy()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var uninstallAsync = ExtractMethod(source, "public async Task<InstallTask> UninstallAsync");

        Assert.Contains("OperationDecisionPolicy.DecideUninstall", uninstallAsync);
        Assert.Contains("RemoteCommandResult?", uninstallAsync);
        Assert.Contains("ApplicationStatusNormalizer.BuildEvidence", uninstallAsync);
        Assert.Contains("task.Fail(decision.Message)", uninstallAsync);
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
