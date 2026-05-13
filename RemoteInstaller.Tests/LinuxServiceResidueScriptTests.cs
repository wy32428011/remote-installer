using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class LinuxServiceResidueScriptTests
{
    public static TheoryData<string, string> ServiceBackedStatusScripts => new()
    {
        { "Consul", "Consul 服务定义存在，但未发现二进制、安装目录、进程或端口，按残留服务处理" },
        { "Elasticsearch", "Elasticsearch 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理" },
        { "MariaDB", "MariaDB 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理" },
        { "Mosquitto", "Mosquitto 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理" },
        { "Nacos", "Nacos 服务定义存在，但未发现安装目录、进程或端口，按残留服务处理" },
        { "RabbitMQ", "RabbitMQ 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理" },
        { "Redis", "Redis 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理" },
        { "Traefik", "Traefik 服务定义存在，但未发现二进制、安装目录、进程或端口，按残留服务处理" }
    };

    public static TheoryData<string, string[]> SystemdUninstallScripts => new()
    {
        { "Consul", new[] { "consul.service" } },
        { "Elasticsearch", new[] { "elasticsearch.service" } },
        { "MariaDB", new[] { "mariadb.service", "mysql.service", "mysqld.service" } },
        { "Mosquitto", new[] { "mosquitto.service" } },
        { "MySQL", new[] { "mysql.service", "mysqld.service", "mariadb.service" } },
        { "Nacos", new[] { "nacos.service" } },
        { "Nginx", new[] { "nginx.service" } },
        { "RabbitMQ", new[] { "rabbitmq-server.service", "rabbitmq.service" } },
        { "Redis", new[] { "redis-server.service", "redis.service" } },
        { "Traefik", new[] { "traefik.service" } }
    };

    public static TheoryData<string, string[]> SysVInitBackedUninstallScripts => new()
    {
        { "Elasticsearch", new[] { "/etc/init.d/elasticsearch" } },
        { "MariaDB", new[] { "/etc/init.d/mariadb", "/etc/init.d/mysql", "/etc/init.d/mysqld" } },
        { "Mosquitto", new[] { "/etc/init.d/mosquitto" } },
        { "MySQL", new[] { "/etc/init.d/mysql", "/etc/init.d/mysqld", "/etc/init.d/mariadb" } },
        { "Nginx", new[] { "/etc/init.d/nginx" } },
        { "RabbitMQ", new[] { "/etc/init.d/rabbitmq-server", "/etc/init.d/rabbitmq" } },
        { "Redis", new[] { "/etc/init.d/redis-server", "/etc/init.d/redis" } }
    };

    [Theory]
    [MemberData(nameof(ServiceBackedStatusScripts))]
    public void StatusScripts_DoNotTreatOrphanedSystemdServiceAsInstalled(string appName, string staleMessage)
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", appName, "check_status_linux.sh");

        Assert.Contains("service_only_stale=\"false\"", script);
        Assert.Contains(staleMessage, script);
        Assert.Contains("SERVICE_ONLY_STALE: ${service_only_stale:-false}", script);
    }

    [Theory]
    [MemberData(nameof(SystemdUninstallScripts))]
    public void UninstallScripts_RemoveSystemdWantsAndGeneratedServiceUnits(string appName, string[] serviceFiles)
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", appName, "uninstall_linux.sh");

        Assert.Contains("SYSTEMD_SERVICE_GLOBS=(", script);

        foreach (var serviceFile in serviceFiles)
        {
            Assert.Contains($"/etc/systemd/system/*.wants/{serviceFile}", script);
            Assert.Contains($"/run/systemd/generator*/{serviceFile}", script);
        }
    }

    [Theory]
    [MemberData(nameof(SysVInitBackedUninstallScripts))]
    public void UninstallScripts_RemoveSysVInitScriptsBeforeSystemdCanRegenerateUnits(string appName, string[] initScripts)
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", appName, "uninstall_linux.sh");

        Assert.Contains("INIT_SCRIPTS=(", script);
        Assert.Contains("update-rc.d -f \"$service_name\" remove", script);
        Assert.Contains("chkconfig --del \"$service_name\"", script);

        foreach (var initScript in initScripts)
        {
            Assert.Contains(initScript, script);
        }
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

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
}
