using RemoteInstaller.Models;
using Xunit;

namespace RemoteInstaller.Tests;

/// <summary>
/// 远程主机模型测试
/// </summary>
public class RemoteHostTests
{
    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        // Act
        var host = new RemoteHost();

        // Assert
        Assert.NotEqual(string.Empty, host.Id);
        Assert.Equal(22, host.Port);
        Assert.Equal(AuthType.Password, host.AuthType);
        Assert.Equal(OperatingSystemType.CentOS, host.OsType);
        Assert.Equal(HostStatus.Unknown, host.Status);
    }

    [Fact]
    public void DisplayText_ReturnsCorrectFormat()
    {
        // Arrange
        var host = new RemoteHost
        {
            Name = "TestServer",
            IpAddress = "192.168.1.100"
        };

        // Act
        var result = host.DisplayText;

        // Assert
        Assert.Equal("TestServer (192.168.1.100)", result);
    }

    [Fact]
    public void OsDisplayName_ReturnsCorrectName_ForWindows()
    {
        // Arrange
        var host = new RemoteHost { OsType = OperatingSystemType.Windows };

        // Act
        var result = host.OsDisplayName;

        // Assert
        Assert.Equal("Windows Server", result);
    }

    [Fact]
    public void OsDisplayName_ReturnsCorrectName_ForCentOS()
    {
        // Arrange
        var host = new RemoteHost { OsType = OperatingSystemType.CentOS };

        // Act
        var result = host.OsDisplayName;

        // Assert
        Assert.Equal("CentOS", result);
    }

    [Fact]
    public void OsDisplayName_ReturnsCorrectName_ForUbuntu()
    {
        // Arrange
        var host = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

        // Act
        var result = host.OsDisplayName;

        // Assert
        Assert.Equal("Ubuntu", result);
    }

    [Fact]
    public void StatusDisplayText_ReturnsCorrectText_ForOnline()
    {
        // Arrange
        var host = new RemoteHost { Status = HostStatus.Online };

        // Act
        var result = host.StatusDisplayText;

        // Assert
        Assert.Equal("🟢 在线", result);
    }

    [Fact]
    public void StatusDisplayText_ReturnsCorrectText_ForOffline()
    {
        // Arrange
        var host = new RemoteHost { Status = HostStatus.Offline };

        // Act
        var result = host.StatusDisplayText;

        // Assert
        Assert.Equal("🔴 离线", result);
    }

    [Fact]
    public void IsLinux_ReturnsTrue_ForLinuxOS()
    {
        // Arrange
        var centosHost = new RemoteHost { OsType = OperatingSystemType.CentOS };
        var ubuntuHost = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

        // Act & Assert
        Assert.True(centosHost.IsLinux);
        Assert.True(ubuntuHost.IsLinux);
    }

    [Fact]
    public void IsLinux_ReturnsFalse_ForWindows()
    {
        // Arrange
        var windowsHost = new RemoteHost { OsType = OperatingSystemType.Windows };

        // Act & Assert
        Assert.False(windowsHost.IsLinux);
    }

    [Fact]
    public void UpdateModifiedTime_UpdatesUpdatedAt()
    {
        // Arrange
        var host = new RemoteHost();
        var beforeTime = host.UpdatedAt;
        
        // Wait a small amount to ensure time difference
        System.Threading.Thread.Sleep(10);

        // Act
        host.UpdateModifiedTime();

        // Assert
        Assert.True(host.UpdatedAt >= beforeTime);
    }
}
