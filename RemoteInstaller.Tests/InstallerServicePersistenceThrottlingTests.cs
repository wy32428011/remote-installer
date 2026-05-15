using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class InstallerServicePersistenceThrottlingTests
{
    [Fact]
    public void InstallerService_UsesThrottledTaskPersistenceInProgressCallbacks()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("RequestTaskPersistence", source);
        Assert.Contains("FlushTaskPersistence", source);
        Assert.DoesNotContain("_databaseService.SaveTask(task)", ExtractInstallProgressHandler(source));
        Assert.DoesNotContain("_databaseService.SaveTask(task)", ExtractUninstallProgressHandler(source));
    }

    private static string ExtractInstallProgressHandler(string source)
    {
        var start = source.IndexOf("logCollector.ProgressUpdated +=", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find install progress handler.");
        var end = source.IndexOf("logCollector.LogReceived", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find end of install progress handler.");
        return source[start..end];
    }

    private static string ExtractUninstallProgressHandler(string source)
    {
        var installHandlerStart = source.IndexOf("logCollector.ProgressUpdated +=", StringComparison.Ordinal);
        var start = source.IndexOf("logCollector.ProgressUpdated +=", installHandlerStart + 1, StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find uninstall progress handler.");
        var end = source.IndexOf("logCollector.LogReceived", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find end of uninstall progress handler.");
        return source[start..end];
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
