using System;
using System.Linq;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class MainViewModelHostOsPersistenceTests
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

    [Fact]
    public void MainViewModel_LoadHosts_PreservesCentOsDisplayAndVersion()
    {
        var hostId = Guid.NewGuid().ToString("N");
        var hostIp = $"10.10.{Random.Shared.Next(1, 200)}.{Random.Shared.Next(1, 200)}";
        using var databaseService = new DatabaseService();

        try
        {
            databaseService.SaveHost(new RemoteHost
            {
                Id = hostId,
                Name = "centos-host",
                IpAddress = hostIp,
                Port = 22,
                Username = "root",
                AuthType = AuthType.Password,
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9",
                CpuArchitecture = "amd64",
                Status = HostStatus.Offline,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            var viewModel = CreateMainViewModel(databaseService);
            var hostViewModel = viewModel.Hosts.Single(host => host.Id == hostId);
            var osVersion = hostViewModel.GetType().GetProperty("OsVersion")?.GetValue(hostViewModel)?.ToString();

            Assert.Equal("CentOS", hostViewModel.OsType);
            Assert.Equal("7.9", osVersion);
        }
        finally
        {
            CleanupHosts(databaseService, hostIp);
        }
    }

    [Fact]
    public void MainViewModel_SaveHost_PreservesExistingOsTypeAndVersion()
    {
        var hostId = Guid.NewGuid().ToString("N");
        var hostIp = $"10.20.{Random.Shared.Next(1, 200)}.{Random.Shared.Next(1, 200)}";
        using var databaseService = new DatabaseService();

        try
        {
            databaseService.SaveHost(new RemoteHost
            {
                Id = hostId,
                Name = "centos-host",
                IpAddress = hostIp,
                Port = 22,
                Username = "root",
                AuthType = AuthType.Password,
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9",
                CpuArchitecture = "amd64",
                Status = HostStatus.Offline,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            var viewModel = CreateMainViewModel(databaseService);
            var hostViewModel = viewModel.Hosts.Single(host => host.Id == hostId);
            hostViewModel.Name = "updated-centos-host";

            viewModel.SaveHost(hostViewModel);

            var savedHosts = databaseService.GetAllHosts()
                .Where(host => host.IpAddress == hostIp)
                .ToList();

            Assert.Single(savedHosts);
            Assert.Equal(hostId, savedHosts[0].Id);
            Assert.Equal("updated-centos-host", savedHosts[0].Name);
            Assert.Equal(OperatingSystemType.CentOS, savedHosts[0].OsType);
            Assert.Equal("7.9", savedHosts[0].OsVersion);
            Assert.Equal("amd64", savedHosts[0].CpuArchitecture);
        }
        finally
        {
            CleanupHosts(databaseService, hostIp);
        }
    }

    [Fact]
    public void HostManagerService_TestConnectionAsync_PersistsDetectedHostMetadata()
    {
        var service = File.ReadAllText(Path.Combine(GetProjectRoot(), "RemoteInstaller", "Services", "HostManagerService.cs"));

        Assert.Contains("DetectedOsVersion", service);
        Assert.Contains("DetectedCpuArchitecture", service);
        Assert.Contains("host.OsVersion", service);
        Assert.Contains("host.CpuArchitecture", service);
        Assert.Contains("_databaseService.SaveHost(host)", service);
    }

    private static MainViewModel CreateMainViewModel(DatabaseService databaseService)
    {
        var sshService = new SshService();
        var logger = new LoggerService();
        var configurationService = new ConfigurationService(sshService, logger);
        var hostStatusRefreshCoordinator = new HostStatusRefreshCoordinator();
        var appConfigurationService = new AppConfigurationService();

        return new MainViewModel(
            sshService,
            databaseService,
            logger,
            configurationService,
            hostStatusRefreshCoordinator,
            appConfigurationService);
    }

    private static void CleanupHosts(DatabaseService databaseService, string ipAddress)
    {
        var hostIds = databaseService.GetAllHosts()
            .Where(host => host.IpAddress == ipAddress)
            .Select(host => host.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        foreach (var hostId in hostIds)
        {
            databaseService.DeleteHost(hostId!);
        }
    }
}
