using System.IO;
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
    public void ConfigurationService_DefinesTraefikConfigPaths()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("[\"Traefik\"] = new()", service);
        Assert.Contains("/etc/traefik/traefik.toml", service);
    }

    [Fact]
    public void ConfigurationService_DefinesSwitchableTraefikConfigFiles()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("/etc/traefik/dynamic.toml", service);
        Assert.Contains("GetSwitchableConfigFilesAsync", service);
    }

    [Fact]
    public void MainViewModel_PassesSwitchableFilesToTraefikConfigEditor()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("GetSwitchableConfigFilesAsync(app.Name", viewModel);
        Assert.Contains("switchableFiles", viewModel);
    }

    [Fact]
    public void ConfigEditorViewModel_DefinesFileSwitchingStateForTraefik()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

        Assert.Contains("AvailableFiles", viewModel);
        Assert.Contains("SelectedFile", viewModel);
        Assert.Contains("SupportsFileSwitch", viewModel);
        Assert.Contains("SwitchToFileAsync", viewModel);
    }

    [Fact]
    public void ConfigEditorViewModel_ProtectsFileSwitchConsistency()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

        Assert.Contains("EnsureCurrentFileOption", viewModel);
        Assert.Contains("_fileSwitchVersion", viewModel);
        Assert.Contains("if (switchVersion != _fileSwitchVersion)", viewModel);
        Assert.Contains("if (!IsModified)", viewModel);
        Assert.Contains("await SaveAsync();", viewModel);
        Assert.Contains("CloseAction?.Invoke();", viewModel);
    }

    [Fact]
    public void ConfigEditorDialog_ShowsFileSwitcherForTraefik()
    {
        var dialog = ReadProjectFile("RemoteInstaller", "Views", "Dialogs", "ConfigEditorDialog.xaml");

        Assert.Contains("ItemsSource=\"{Binding AvailableFiles}\"", dialog);
        Assert.Contains("SelectedItem=\"{Binding SelectedFile, Mode=TwoWay}\"", dialog);
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", dialog);
        Assert.Contains("Visibility=\"{Binding SupportsFileSwitch, Converter={StaticResource BoolToVisibilityConverter}}\"", dialog);
    }

    [Fact]
    public void TraefikInstallScript_GrantsLowPortBindingCapability()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Traefik", "install_linux.sh");

        Assert.Contains("AmbientCapabilities=CAP_NET_BIND_SERVICE", script);
        Assert.Contains("CapabilityBoundingSet=CAP_NET_BIND_SERVICE", script);
        Assert.Contains("NoNewPrivileges=true", script);
    }
}
