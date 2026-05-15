using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class MainViewModelOperationPipelineTests
{
    [Fact]
    public void MainViewModel_CreatesOperationExecutorForOperations()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("CreateOperationExecutor", source);
        Assert.Contains("new OperationRequest(", source);
        Assert.Contains("OperationType.Install", source);
        Assert.Contains("OperationType.CheckStatus", source);
        Assert.Contains("OperationType.Uninstall", source);
    }

    [Fact]
    public void MainViewModel_UsesOperationEventBufferForOperationUpdates()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("UiOperationEventBuffer", source);
        Assert.Contains("FlushOperationEvents", source);
        Assert.Contains("ApplyOperationEventBatch", source);
        Assert.Contains("CreateTaskOperationEventHandler", source);
    }

    [Fact]
    public void RemoteOperationCallbacks_DoNotUseSynchronousDispatcherInvoke()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var operationRegionStart = source.IndexOf("private async Task ExecuteInstallAsync", StringComparison.Ordinal);
        Assert.True(operationRegionStart >= 0, "Expected ExecuteInstallAsync to exist.");
        var operationRegion = source[operationRegionStart..];

        Assert.DoesNotContain("Dispatcher.Invoke(()", operationRegion);
        Assert.Contains("InvokeAsync", source);
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
