using System.Reflection;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class AddHostViewModelTests
{
    [Fact]
    public void AddHostViewModel_LoadHost_PreservesOsVersion()
    {
        var host = new RemoteHost
        {
            Name = "ubuntu-host",
            IpAddress = "192.168.1.10",
            Port = 22,
            Username = "root",
            EncryptedPassword = EncryptionService.Encrypt("password"),
            AuthType = AuthType.Password,
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "22.04",
            GroupName = "default"
        };

        var viewModel = new AddHostViewModel(new SshService(), new DatabaseService(), new LoggerService(), host);

        Assert.Equal(OperatingSystemType.Ubuntu, viewModel.OsType);
        Assert.Equal("22.04", viewModel.OsVersion);
    }

    [Fact]
    public void AddHostViewModel_CreateHost_RetainsLoadedOsVersion()
    {
        var host = new RemoteHost
        {
            Name = "centos-host",
            IpAddress = "192.168.1.20",
            Port = 22,
            Username = "root",
            EncryptedPassword = EncryptionService.Encrypt("password"),
            AuthType = AuthType.Password,
            OsType = OperatingSystemType.CentOS,
            OsVersion = "7.9",
            GroupName = "default"
        };

        var viewModel = new AddHostViewModel(new SshService(), new DatabaseService(), new LoggerService(), host);
        var method = typeof(AddHostViewModel).GetMethod("CreateHost", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var createdHost = (RemoteHost?)method!.Invoke(viewModel, null);

        Assert.NotNull(createdHost);
        Assert.Equal(OperatingSystemType.CentOS, createdHost!.OsType);
        Assert.Equal("7.9", createdHost.OsVersion);
    }

    [Fact]
    public void AddHostViewModel_EditMode_RequiresRetestAfterConnectionInfoChanges()
    {
        var host = new RemoteHost
        {
            Name = "ubuntu-host",
            IpAddress = "192.168.1.10",
            Port = 22,
            Username = "root",
            EncryptedPassword = EncryptionService.Encrypt("password"),
            AuthType = AuthType.Password,
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "22.04",
            GroupName = "default"
        };

        var viewModel = new AddHostViewModel(new SshService(), new DatabaseService(), new LoggerService(), host);

        Assert.True(viewModel.CanSave);

        viewModel.IpAddress = "192.168.1.11";

        Assert.False(viewModel.CanSave);
    }
}
