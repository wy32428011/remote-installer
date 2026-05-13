using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class PerformanceRefactorPlanTests
{
    [Fact]
    public void MainViewModel_UsesIsolatedInstallerServicesForInstallAndStatusChecks()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var fetchSnapshot = ExtractMethod(source, "private async Task<HostStatusSnapshot> FetchHostStatusSnapshotAsync");
        var installToHost = ExtractMethod(source, "private async Task<BatchTaskResult> InstallApplicationToHost");
        var executeInstall = ExtractMethod(source, "private async Task ExecuteInstallAsync");

        Assert.Contains("private InstallerService CreateIsolatedInstallerService()", source);
        Assert.DoesNotContain("new InstallerService(_sshService, _logger)", fetchSnapshot);
        Assert.Contains("using var installerService = CreateIsolatedInstallerService();", fetchSnapshot);
        Assert.Contains("using var installerService = CreateIsolatedInstallerService();", installToHost);
        Assert.Contains("using var installerService = CreateIsolatedInstallerService();", executeInstall);
    }

    [Fact]
    public void MainViewModel_PostInstallAndPostUninstallRefreshOnlyMutatedApplications()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var executeInstall = ExtractMethod(source, "private async Task ExecuteInstallAsync");
        var uninstallApplication = ExtractMethod(source, "private async Task UninstallApplication");

        Assert.Contains("RefreshApplicationStatusAfterMutationAsync", source);
        Assert.DoesNotContain("HostStatusRefreshReason.PostInstall", executeInstall);
        Assert.DoesNotContain("HostStatusRefreshReason.PostUninstall", uninstallApplication);
        Assert.Contains("await RefreshApplicationStatusAfterMutationAsync(selectedHostViewModel, refreshedAppCard", executeInstall);
        Assert.Contains("await RefreshApplicationStatusAfterMutationAsync(SelectedHost, app", uninstallApplication);
    }

    [Fact]
    public void SshService_ConnectAsync_DoesNotEagerlyConnectSftp()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "SshService.cs");
        var connectAsync = ExtractMethod(source, "public async Task ConnectAsync");

        Assert.DoesNotContain("EnsureSftpConnectedAsync", connectAsync);
    }

    [Fact]
    public void LogViews_UseVirtualizedListsInsteadOfScrollViewerItemsControl()
    {
        var mainWindow = ReadProjectFile("RemoteInstaller", "MainWindow.xaml");
        var progressDialog = ReadProjectFile("RemoteInstaller", "Views", "Dialogs", "InstallProgressDialog.xaml");

        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", mainWindow);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", progressDialog);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding Logs}\">", mainWindow);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding LogEntries}\">", progressDialog);
    }

    [Fact]
    public void DatabaseService_CreatesIndexesForFrequentLogAndHistoryQueries()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "DatabaseService.cs");

        Assert.Contains("idx_logs_task_timestamp", source);
        Assert.Contains("idx_logs_task_level_timestamp", source);
        Assert.Contains("idx_tasks_start_time", source);
        Assert.Contains("idx_install_history_start_time", source);
        Assert.Contains("idx_install_history_host_start_time", source);
        Assert.Contains("idx_install_history_application_start_time", source);
        Assert.Contains("EnsurePerformanceIndexes(connection);", source);
    }

    [Fact]
    public void LongWaitInstallScripts_ReportIncrementalProgressDuringServiceWaits()
    {
        var elasticsearch = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "install_linux.sh");
        var rabbitMq = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");
        var nacos = ReadProjectFile("RemoteInstaller", "Scripts", "Nacos", "install_linux.sh");
        var mysql = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "install_linux.sh");

        Assert.Contains("PROGRESS:Verifying:$((85 + COUNT * 10 / 40))", elasticsearch);
        Assert.Contains("PROGRESS:WaitingForService:$((85 + COUNT * 2 / 30))", rabbitMq);
        Assert.Contains("PROGRESS:CheckingManagementPort:$((87 + MGMT_COUNT * 2 / 20))", rabbitMq);
        Assert.Contains("PROGRESS:Starting:$((80 + COUNT * 15 / 60))", nacos);
        Assert.Contains("PROGRESS:Starting:$((65 + i * 10 / 30))", mysql);
        Assert.Contains("PROGRESS:Verifying:$((88 + i * 7 / 20))", mysql);
    }

    [Fact]
    public void MainViewModel_DelegatesHostStatusSnapshotFetchingToService()
    {
        var mainViewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var service = ReadProjectFile("RemoteInstaller", "Services", "HostApplicationStatusService.cs");
        var fetchSnapshot = ExtractMethod(mainViewModel, "private async Task<HostStatusSnapshot> FetchHostStatusSnapshotAsync");

        Assert.Contains("private readonly HostApplicationStatusService _hostApplicationStatusService;", mainViewModel);
        Assert.Contains("_hostApplicationStatusService.FetchSnapshotAsync", fetchSnapshot);
        Assert.Contains("public sealed class HostApplicationStatusService", service);
        Assert.Contains("BuiltInApplicationStatusRequest", service);
        Assert.Contains("CustomApplicationStatusRequest", service);
        Assert.DoesNotContain("new SemaphoreSlim(3)", fetchSnapshot);
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
