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
    public void Ubuntu22NginxOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx", "nginx-ubuntu", "22");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx_1.24.0-1~jammy_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "adduser_3.118ubuntu5_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "lsb-base_11.1.0ubuntu4_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libssl3_3.0.2-0ubuntu1.20_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.35-0ubuntu3.8_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libcrypt1_1.1.0-1build4_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libpcre2-8-0_10.39-3ubuntu0.1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "zlib1g_1.2.11.dfsg-2ubuntu9.2_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu22-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("1.24.0", viewModel.LocalPackageVersion);
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
    public void Ubuntu24NginxOfflineDirectory_WithoutRequiredSystemDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx", "nginx-ubuntu", "24");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx_1.24.0-2ubuntu7.6_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx-common_1.24.0-2ubuntu7.6_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "iproute2_6.1.0-1ubuntu6.2_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.39-0ubuntu8.7_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libcrypt1_4.4.36-4build1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libpcre2-8-0_10.42-4ubuntu2.1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "zlib1g_1.3.dfsg-3.1ubuntu2.1_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu24-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "24.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("libssl3t64", viewModel.LocalResourceHint);
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
    public void Ubuntu22NginxOfflineDirectory_WithoutRequiredSystemDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx", "nginx-ubuntu", "22");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx_1.24.0-1~jammy_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "lsb-base_11.1.0ubuntu4_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libssl3_3.0.2-0ubuntu1.20_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.35-0ubuntu3.8_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libcrypt1_1.1.0-1build4_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libpcre2-8-0_10.39-3ubuntu0.1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "zlib1g_1.2.11.dfsg-2ubuntu9.2_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu22-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("adduser", viewModel.LocalResourceHint);
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
    public void CentOs7NginxOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx", "nginx-centos7");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx-1.24.0-1.el7.ngx.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "pcre2-10.23-2.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "openssl-libs-1.0.2k-26.el7_9.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "glibc-2.17-326.el7_9.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "procps-ng-3.3.10-28.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "shadow-utils-4.6-5.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "systemd-219-78.el7_9.9.x86_64.rpm"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("1.24.0", viewModel.LocalPackageVersion);
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
    public void CentOs7NginxOfflineDirectory_WithoutRequiredRpmDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Nginx", "nginx-centos7");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "nginx-1.24.0-1.el7.ngx.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "openssl-libs-1.0.2k-26.el7_9.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "glibc-2.17-326.el7_9.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "procps-ng-3.3.10-28.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "shadow-utils-4.6-5.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "systemd-219-78.el7_9.9.x86_64.rpm"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.24.0" }
            };

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("pcre2-", viewModel.LocalResourceHint);
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
    public void UbuntuRabbitMqOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerRabbitMqTests_{Guid.NewGuid():N}");
        var scriptsRoot = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");

        try
        {
            InstallConfigViewModel.ScriptRootOverridesFactory = () => new[] { tempScriptsRoot };
            Directory.CreateDirectory(scriptsRoot);
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            File.WriteAllText(Path.Combine(scriptsRoot, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "logrotate_3.19.0-1ubuntu1.1_amd64.deb"), string.Empty);

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
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("3.12.0", viewModel.LocalPackageVersion);
        }
        finally
        {
            InstallConfigViewModel.ScriptRootOverridesFactory = null;
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }

    [Fact]
    public void UbuntuRabbitMqOfflineDirectory_WithoutRequiredSystemDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerRabbitMqTests_{Guid.NewGuid():N}");
        var scriptsRoot = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");

        try
        {
            InstallConfigViewModel.ScriptRootOverridesFactory = () => new[] { tempScriptsRoot };
            Directory.CreateDirectory(scriptsRoot);
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
            InstallConfigViewModel.ScriptRootOverridesFactory = null;
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }

    [Fact]
    public void Ubuntu22ElasticsearchOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Elasticsearch", "elasticsearch-ubuntu", "22");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "elasticsearch-8.5.3-amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "bash_5.1-6ubuntu1.1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "lsb-base_11.1.0ubuntu4_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.35-0ubuntu3.13_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "adduser_3.118ubuntu5_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "coreutils_8.32-4.1ubuntu1_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.5.3",
                Versions = new List<string> { "8.5.3" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu22-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("8.5.3", viewModel.LocalPackageVersion);
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
    public void Ubuntu24ElasticsearchOfflineDirectory_WithoutRequiredSystemDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Elasticsearch", "elasticsearch-ubuntu", "24");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "elasticsearch-8.5.3-amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "bash_5.2.21-2ubuntu4_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "lsb-base_11.6_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.39-0ubuntu8.7_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "adduser_3.137ubuntu1_all.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.5.3",
                Versions = new List<string> { "8.5.3" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu24-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "24.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("coreutils", viewModel.LocalResourceHint);
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
    public void Ubuntu22ElasticsearchOfflineDirectory_WithoutMainPackage_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Elasticsearch", "elasticsearch-ubuntu", "22");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "bash_5.1-6ubuntu1.1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "lsb-base_11.1.0ubuntu4_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "libc6_2.35-0ubuntu3.13_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "adduser_3.118ubuntu5_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "coreutils_8.32-4.1ubuntu1_amd64.deb"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.5.3",
                Versions = new List<string> { "8.5.3" }
            };

            var host = new RemoteHost
            {
                Name = "ubuntu22-host",
                OsType = OperatingSystemType.Ubuntu,
                OsVersion = "22.04"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("elasticsearch-8.5.3*.deb", viewModel.LocalResourceHint);
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
    public void CentOs7ElasticsearchOfflineDirectory_WithRequiredPackages_IsAutoSelectedAsDirectoryResource()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Elasticsearch", "elasticsearch-centos7");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "elasticsearch-8.5.3-x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "bash-4.2.46-34.el7.x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "coreutils-8.22-24.el7.x86_64.rpm"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.5.3",
                Versions = new List<string> { "8.5.3" }
            };

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.True(application.UseLocalPackage);
            Assert.Equal(scriptsRoot, viewModel.LocalPackagePath);
            Assert.Equal("8.5.3", viewModel.LocalPackageVersion);
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
    public void CentOs7ElasticsearchOfflineDirectory_WithoutRequiredRpmDependency_StaysLocalButDoesNotAutoSelectPackage()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scriptsRoot = Path.Combine(baseDirectory, "Scripts", "Elasticsearch", "elasticsearch-centos7");
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
            Directory.CreateDirectory(Path.Combine(scriptsRoot, "deps"));
            createdDirectory = true;
            File.WriteAllText(Path.Combine(scriptsRoot, "elasticsearch-8.5.3-x86_64.rpm"), string.Empty);
            File.WriteAllText(Path.Combine(scriptsRoot, "deps", "bash-4.2.46-34.el7.x86_64.rpm"), string.Empty);

            var application = new ApplicationInfo
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.5.3",
                Versions = new List<string> { "8.5.3" }
            };

            var host = new RemoteHost
            {
                Name = "centos7-host",
                OsType = OperatingSystemType.CentOS,
                OsVersion = "7.9"
            };

            var viewModel = new InstallConfigViewModel(application, host, new LoggerService());

            Assert.Equal("local", viewModel.PackageSource);
            Assert.False(application.UseLocalPackage);
            Assert.True(string.IsNullOrEmpty(viewModel.LocalPackagePath));
            Assert.Contains("coreutils-", viewModel.LocalResourceHint);
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
