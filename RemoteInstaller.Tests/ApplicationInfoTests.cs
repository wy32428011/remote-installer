using RemoteInstaller.Models;
using Xunit;

namespace RemoteInstaller.Tests;

/// <summary>
/// 应用信息模型测试
/// </summary>
public class ApplicationInfoTests
{
    [Fact]
    public void SupportsOs_ReturnsTrue_ForSupportedOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            SupportWindows = true,
            SupportCentOS = true,
            SupportUbuntu = true
        };

        // Act & Assert
        Assert.True(app.SupportsOs(OperatingSystemType.Windows));
        Assert.True(app.SupportsOs(OperatingSystemType.CentOS));
        Assert.True(app.SupportsOs(OperatingSystemType.Ubuntu));
    }

    [Fact]
    public void SupportsOs_ReturnsFalse_ForUnsupportedOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            SupportWindows = false,
            SupportCentOS = true,
            SupportUbuntu = false
        };

        // Act & Assert
        Assert.False(app.SupportsOs(OperatingSystemType.Windows));
        Assert.True(app.SupportsOs(OperatingSystemType.CentOS));
        Assert.False(app.SupportsOs(OperatingSystemType.Ubuntu));
    }

    [Fact]
    public void GetInstallScript_ReturnsLinuxScript_ForLinuxOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            InstallScriptLinux = "linux_install.sh",
            InstallScriptWindows = "windows_install.ps1"
        };

        // Act
        var linuxResult = app.GetInstallScript(OperatingSystemType.CentOS);
        var ubuntuResult = app.GetInstallScript(OperatingSystemType.Ubuntu);

        // Assert
        Assert.Equal("linux_install.sh", linuxResult);
        Assert.Equal("linux_install.sh", ubuntuResult);
    }

    [Fact]
    public void GetInstallScript_ReturnsWindowsScript_ForWindowsOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            InstallScriptLinux = "linux_install.sh",
            InstallScriptWindows = "windows_install.ps1"
        };

        // Act
        var result = app.GetInstallScript(OperatingSystemType.Windows);

        // Assert
        Assert.Equal("windows_install.ps1", result);
    }

    [Fact]
    public void GetUninstallScript_ReturnsCorrectScript_BasedOnOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            UninstallScriptLinux = "linux_uninstall.sh",
            UninstallScriptWindows = "windows_uninstall.ps1"
        };

        // Act & Assert
        Assert.Equal("linux_uninstall.sh", app.GetUninstallScript(OperatingSystemType.CentOS));
        Assert.Equal("windows_uninstall.ps1", app.GetUninstallScript(OperatingSystemType.Windows));
    }

    [Fact]
    public void GetCheckScript_ReturnsCorrectScript_BasedOnOS()
    {
        // Arrange
        var app = new ApplicationInfo
        {
            CheckScriptLinux = "linux_check.sh",
            CheckScriptWindows = "windows_check.ps1"
        };

        // Act & Assert
        Assert.Equal("linux_check.sh", app.GetCheckScript(OperatingSystemType.Ubuntu));
        Assert.Equal("windows_check.ps1", app.GetCheckScript(OperatingSystemType.Windows));
    }
}
