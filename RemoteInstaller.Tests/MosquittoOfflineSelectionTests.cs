using System;
using System.Collections.Generic;
using System.IO;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class MosquittoOfflineSelectionTests : IDisposable
{
    private readonly string _tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"mosquitto-scripts-{Guid.NewGuid():N}");

    private static ApplicationInfo CreateMosquittoApplication() => new()
    {
        Id = "mosquitto",
        Name = "Mosquitto",
        Version = "2.0.21",
        Versions = new List<string> { "2.0.21", "2.0.22", "2.1.2", "1.6.10" },
        Parameters = new List<InstallParameter>()
    };

    public MosquittoOfflineSelectionTests()
    {
        InstallConfigViewModel.ScriptRootOverridesFactory = () => new[] { _tempScriptsRoot };
    }

    public void Dispose()
    {
        InstallConfigViewModel.ScriptRootOverridesFactory = null;
        if (Directory.Exists(_tempScriptsRoot))
        {
            Directory.Delete(_tempScriptsRoot, true);
        }
    }

    [Fact]
    public void Ubuntu22MosquittoOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-ubuntu", "22");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.0.22_amd64.deb"), string.Empty);

            var host = new RemoteHost
            {
                Name = "ubuntu22-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("2.0.22", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void Ubuntu24MosquittoOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-ubuntu", "24");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.1.2_amd64.deb"), string.Empty);

            var host = new RemoteHost
            {
                Name = "ubuntu24-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "24.04"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("2.1.2", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void Ubuntu24MosquittoOfflineDirectory_WithoutRequiredPackage_StaysLocalButDoesNotAutoSelectPackage()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-ubuntu", "24");
        try
        {
            Directory.CreateDirectory(scriptsRoot);

            var host = new RemoteHost
            {
                Name = "ubuntu24-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "24.04"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("Mosquitto Ubuntu 24", viewModel.LocalResourceHint);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void Ubuntu22MosquittoOfflineDirectory_WithMixedArchitectures_UsesHostArchitectureForVersionSelection()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-ubuntu", "22");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.0.22_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.1.0_arm64.deb"), string.Empty);

            var host = new RemoteHost
            {
                Name = "ubuntu22-amd64-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04",
                CpuArchitecture = "amd64"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("2.0.22", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void Ubuntu22MosquittoOfflineDirectory_WithUnknownArchitectureAndMixedPackages_DoesNotAutoSelectPackage()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-ubuntu", "22");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.0.21_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto_2.0.21_arm64.deb"), string.Empty);

            var host = new RemoteHost
            {
                Name = "ubuntu22-unknown-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("CPU 架构未知", viewModel.LocalResourceHint);
            Assert.Contains("amd64", viewModel.LocalResourceHint);
            Assert.Contains("arm64", viewModel.LocalResourceHint);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void CentOs7MosquittoOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-centos7");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto-1.6.10-1.el7.x86_64.rpm"), string.Empty);

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("1.6.10", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void CentOs7MosquittoOfflineDirectory_WithoutRequiredPackage_StaysLocalButDoesNotAutoSelectPackage()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-centos7");
        try
        {
            Directory.CreateDirectory(scriptsRoot);

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("Mosquitto CentOS 7", viewModel.LocalResourceHint);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void CentOs7MosquittoOfflineDirectory_WithMixedArchitectures_UsesHostArchitectureForVersionSelection()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "mosquitto-centos7");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto-2.0.21-1.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto-2.1.0-1.el7.aarch64.rpm"), string.Empty);

            var host = new RemoteHost
            {
                Name = "centos7-arm64-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9",
                CpuArchitecture = "arm64"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("2.1.0", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void WindowsMosquittoOfflineZip_WithVersionMatch_IsAutoSelected()
    {
        var scriptsRoot = Path.Combine(_tempScriptsRoot, "Mosquitto", "windows");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(scriptsRoot, "mosquitto-2.0.21-windows-x64.zip"), string.Empty);

            var host = new RemoteHost
            {
                Name = "windows-host",
                OsType = OperatingSystemType.Windows,
                OsVersion = "10.0"
            };

            var application = CreateMosquittoApplication();
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(Path.Combine(scriptsRoot, "mosquitto-2.0.21-windows-x64.zip"), viewModel.LocalPackagePath);
            Assert.Equal("2.0.21", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(scriptsRoot))
            {
                Directory.Delete(scriptsRoot, true);
            }
        }
    }

    [Fact]
    public void MosquittoVersionExtraction_IgnoresNestedDirectoryVersions()
    {
        var application = CreateMosquittoApplication();
        var host = new RemoteHost
        {
            Name = "ubuntu24-host",
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "24.04"
        };

        var tempRoot = Path.Combine(Path.GetTempPath(), $"mosquitto-offline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "mosquitto_2.1.2_amd64.deb"), string.Empty);
        Directory.CreateDirectory(Path.Combine(tempRoot, "deps"));
        File.WriteAllText(Path.Combine(tempRoot, "deps", "ignored_1.2.3.deb"), string.Empty);

        try
        {
            var viewModel = new InstallConfigViewModel(application, host, new LoggerService())
            {
                LocalPackagePath = tempRoot
            };

            Assert.Equal("2.1.2", viewModel.LocalPackageVersion);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void MosquittoCredentialPairValidation_AllowsEmptyAndPairedValues_ButRejectsSingleSide()
    {
        var application = CreateMosquittoApplication();
        application.Parameters = new List<InstallParameter>
        {
            new() { Key = "MQTT_PORT", Name = "MQTT Port", Type = ParameterType.Port, DefaultValue = "1883", Required = true, MinValue = 1, MaxValue = 65535 },
            new() { Key = "USERNAME", Name = "用户名", Type = ParameterType.Text, DefaultValue = "", Required = false },
            new() { Key = "PASSWORD", Name = "密码", Type = ParameterType.Password, DefaultValue = "", Required = false }
        };

        var host = new RemoteHost
        {
            Name = "ubuntu24-host",
            OsType = OperatingSystemType.Ubuntu,
            OsVersion = "24.04"
        };

        var viewModel = new InstallConfigViewModel(application, host, new LoggerService());
        var username = Assert.Single(viewModel.ParameterViewModels, param => param.Key == "USERNAME");
        var password = Assert.Single(viewModel.ParameterViewModels, param => param.Key == "PASSWORD");

        Assert.False(username.Required);
        Assert.False(password.Required);
        Assert.Equal(string.Empty, username.Value);
        Assert.Equal(string.Empty, password.Value);
        Assert.True(viewModel.CanConfirm);

        username.Value = "mqttadmin";
        password.Value = "secret";
        Assert.True(viewModel.CanConfirm);

        username.Value = "mqttadmin";
        password.Value = "";
        Assert.False(viewModel.CanConfirm);
        Assert.Equal("Mosquitto 用户名和密码必须同时填写，或同时留空以启用匿名访问。", viewModel.ErrorMessage);

        username.Value = "   ";
        password.Value = "   ";
        Assert.True(viewModel.CanConfirm);
    }
}
