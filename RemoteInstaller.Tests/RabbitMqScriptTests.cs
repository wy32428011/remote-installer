using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class RabbitMqScriptTests
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
    public void InstallLinuxScript_RemoteAccessEnabled_UsesExplicitRemoteBindings()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.Contains("listeners.tcp.1 = 0.0.0.0:${AMQP_PORT}", script);
        Assert.Contains("management.tcp.ip = 0.0.0.0", script);
        Assert.Contains("loopback_users = none", script);
    }

    [Fact]
    public void InstallLinuxScript_RemoteAccessDisabled_DoesNotClaimLoopbackUsersNoneForLocalOnlyMode()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.DoesNotContain("loopback_users = none\nEOF", script);
        Assert.Contains("listeners.tcp.1 = 127.0.0.1:${AMQP_PORT}", script);
        Assert.Contains("management.tcp.ip = 127.0.0.1", script);
    }

    [Fact]
    public void CheckStatusLinuxScript_ReportsManagementPluginStateExplicitly()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "check_status_linux.sh");

        Assert.Contains("MANAGEMENT_PLUGIN_ENABLED:", script);
        Assert.Contains("rabbitmq_management", script);
        Assert.Contains("rabbitmq-plugins list -E -m", script);
    }

    [Fact]
    public void CheckStatusLinuxScript_ReportsManagementHttpAvailabilityExplicitly()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "check_status_linux.sh");

        Assert.Contains("MANAGEMENT_HTTP_READY:", script);
        Assert.Contains("/api/overview", script);
        Assert.Contains("curl -s -o /dev/null -w \"%{http_code}\"", script);
    }

    [Fact]
    public void InstallLinuxScript_UbuntuOfflineMainPackageOnly_FailsFastWithDependencyMessage()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.Contains("Ubuntu RabbitMQ 离线安装需要完整的 Erlang/依赖 .deb 包集合", script);
    }

    [Fact]
    public void InstallLinuxScript_UbuntuOffline_InstallsErlangBeforeRabbitMq()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.Contains("ERLANG_DEB_FILES=()", script);
        Assert.Contains("RABBITMQ_DEB_FILES=()", script);
        Assert.Contains("先安装 Erlang 离线包", script);
        Assert.Contains("再安装 RabbitMQ 离线包", script);
    }

    [Fact]
    public void InstallLinuxScript_UbuntuOffline_ValidatesRabbitMqAndErlangCompatibility()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.Contains("RabbitMQ 3.12.x 仅支持 Erlang 25.x 或 26.x", script);
        Assert.Contains("ERLANG_BASE_VERSION", script);
        Assert.Contains("RABBITMQ_PACKAGE_NAME", script);
    }

    [Fact]
    public void InstallLinuxScript_ValidatesManagementHttpAvailability()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "RabbitMQ", "install_linux.sh");

        Assert.Contains("curl -s -o /dev/null -w \"%{http_code}\"", script);
        Assert.Contains("/api/overview", script);
        Assert.Contains("MANAGEMENT_HTTP_READY", script);
    }

    [Fact]
    public void InstallerService_UploadsLinuxInstallScriptUsingOriginalInstallLinuxFileName()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("Path.GetFileName(localScriptPath)", installerService);
        Assert.DoesNotContain("install_{app.Id}.{scriptExt}", installerService);
    }

    [Fact]
    public void InstallerService_DoesNotKeepRabbitMqSingleFileDependencyUploadSpecialCase()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.DoesNotContain("RabbitMQ Ubuntu 离线安装缺少依赖", installerService);
        Assert.DoesNotContain("RabbitMQ 离线依赖文件列表", installerService);
        Assert.DoesNotContain("dependencyFiles", installerService);
    }

    [Fact]
    public void InstallConfigViewModel_UsesRabbitMqOfflineDirectoryInsteadOfSinglePackageFile()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");
        var packageResolver = ReadProjectFile("RemoteInstaller", "Services", "Operations", "DefaultPackageResolver.cs");

        Assert.Contains("new DefaultPackageResolver", viewModel);
        Assert.Contains("return resolution.Found;", viewModel);
        Assert.Contains("return PackageResolution.FoundPackage(", packageResolver);
        Assert.Contains("已从 Scripts 目录自动匹配 RabbitMQ 本地资源目录", packageResolver);
        Assert.Contains("RabbitMQ Ubuntu 离线资源目录缺少依赖", packageResolver);
    }

    [Fact]
    public void InstallConfigViewModel_DefaultsPackageSourceToLocal()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("private string _packageSource = \"local\";", viewModel);
    }

    [Fact]
    public void InstallConfigDialog_PrioritizesLocalResourcesInLabelsAndOrder()
    {
        var xaml = ReadProjectFile("RemoteInstaller", "Views", "InstallConfigDialog.xaml");

        Assert.Contains("Content=\"本地资源\"", xaml);
        Assert.Contains("Content=\"在线安装\"", xaml);
        Assert.True(xaml.IndexOf("Content=\"本地资源\"", System.StringComparison.Ordinal) < xaml.IndexOf("Content=\"在线安装\"", System.StringComparison.Ordinal));
        Assert.Contains("推荐优先使用本地资源安装", xaml);
        Assert.Contains("Scripts", xaml);
        Assert.Contains("Content=\"刷新检测\"", xaml);
        Assert.DoesNotContain("Content=\"浏览\"", xaml);
        Assert.DoesNotContain("Text=\"本地文件\"", xaml);
        Assert.DoesNotContain("请选择本地安装文件", xaml);
    }
}
