using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class PackageResolverTests
{
    [Fact]
    public void ResolveRabbitMqUbuntu_WithRequiredPackages_ReturnsDirectory()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerPackageResolver_{Guid.NewGuid():N}");
        try
        {
            var root = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");
            Directory.CreateDirectory(Path.Combine(root, "deps"));
            File.WriteAllText(Path.Combine(root, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "deps", "logrotate_3.19.0-1ubuntu1.1_amd64.deb"), string.Empty);

            var resolver = new DefaultPackageResolver(() => new[] { tempScriptsRoot });
            var app = new ApplicationInfo { Id = "rabbitmq", Name = "RabbitMQ", Version = "3.12.0" };
            var host = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

            var result = resolver.Resolve(app, host);

            Assert.True(result.Found);
            Assert.Equal(root, result.Path);
        }
        finally
        {
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }

    [Fact]
    public void ResolveRabbitMqUbuntu_WithoutLogrotate_ReturnsMissingDependency()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerPackageResolver_{Guid.NewGuid():N}");
        try
        {
            var root = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);

            var resolver = new DefaultPackageResolver(() => new[] { tempScriptsRoot });
            var app = new ApplicationInfo { Id = "rabbitmq", Name = "RabbitMQ", Version = "3.12.0" };
            var host = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

            var result = resolver.Resolve(app, host);

            Assert.False(result.Found);
            Assert.Contains("logrotate", result.MissingDependencies);
        }
        finally
        {
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }
}
