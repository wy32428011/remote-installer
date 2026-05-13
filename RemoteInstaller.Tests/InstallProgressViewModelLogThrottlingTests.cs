using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class InstallProgressViewModelLogThrottlingTests
{
    [Fact]
    public void TaskLogEntriesCollectionChanged_RequestsThrottledRefreshInsteadOfRebuildingImmediately()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallProgressViewModel.cs");
        var method = ExtractMethod(source, "private void TaskLogEntries_CollectionChanged");

        Assert.Contains("RequestLogEntriesRefresh(taskLogs, e);", method);
        Assert.DoesNotContain("LogEntries.Clear();", method);
        Assert.DoesNotContain("Application.Current.Dispatcher.Invoke", method);
    }

    [Fact]
    public void FlushLogEntriesRefresh_AppliesPendingLogChangesInsteadOfReloadingWholeCollection()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallProgressViewModel.cs");
        var method = ExtractMethod(source, "private void FlushLogEntriesRefresh");

        Assert.Contains("ApplyPendingLogChanges", method);
        Assert.Contains("if (requiresFullReload)", method);
        Assert.Contains("ApplyPendingLogChanges(entriesToRemoveFromStart, entriesToAdd);", method);
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
