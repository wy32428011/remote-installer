using System.IO;
using System.Collections.Generic;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class RabbitMqOfflineSelectionTests
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
    public void NormalApplication_ScriptsDirectoryPackage_IsAutoSelectedAsLocalResource()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx");
        var backupRoot = scriptsRoot + ".test-backup";
        var createdDirectory = false;
        var movedOriginalDirectory = false;
        var packagePath = Path.Combine(scriptsRoot, "nginx-1.24.0.tar.gz");

        try
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, true);
            }

            if (Directory.Exists(scriptsRoot))
            {
                Directory.Move(scriptsRoot, backupRoot);
                movedOriginalDirectory = true;
            }

            Directory.CreateDirectory(scriptsRoot);
            createdDirectory = true;
            File.WriteAllText(packagePath, string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu-host",
                OsType = OperatingSystemType.Ubuntu
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.Equal(packagePath, viewModel.LocalPackagePath);
            Assert.Equal("1.24.0", viewModel.LocalPackageVersion);
            Assert.True(application.UseLocalPackage);
        }
        finally
        {
            if (createdDirectory && Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }

            if (movedOriginalDirectory && Directory.Exists(backupRoot))
            {
                Directory.Move(backupRoot, scriptsRoot);
            }
        }
    }

    [Fact]
    public void NormalApplication_WithoutScriptsDirectoryPackage_StaysLocalButDoesNotAutoSelectPackage()
    {
        var application = new ApplicationInfo
        {
            Id = "nginx",
            Name = "Nginx",
            Version = "1.24.0",
            Versions = new List<string> { "1.24.0" }
        };

        var host = new RemoteHost
        {
            Name = "ubuntu-host",
            OsType = OperatingSystemType.Ubuntu
        };

        var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

        Assert.Equal("local", viewModel.PackageSource);
        Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
        Assert.False(application.UseLocalPackage);
    }

    [Fact]
    public void UbuntuRabbitMqOfflineDirectory_WithoutRequiredSystemDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "RabbitMQ", "rabbitmq-ubuntu");
        var backupRoot = scriptsRoot + ".test-backup";
        var createdDirectory = false;
        var movedOriginalDirectory = false;

        try
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, true);
            }

            if (Directory.Exists(scriptsRoot))
            {
                Directory.Move(scriptsRoot, backupRoot);
                movedOriginalDirectory = true;
            }

            Directory.CreateDirectory(scriptsRoot);
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "rabbitmq",
                Name = "RabbitMQ",
                Version = "3.12.0",
                Versions = new List<string> { "3.12.0" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu-host",
                OsType = OperatingSystemType.Ubuntu
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
        }
        finally
        {
            if (createdDirectory && Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }

            if (movedOriginalDirectory && Directory.Exists(backupRoot))
            {
                Directory.Move(backupRoot, scriptsRoot);
            }
        }
    }
}
