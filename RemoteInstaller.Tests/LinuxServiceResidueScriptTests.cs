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

    [Fact]
    public void MySqlUninstallScript_RemovesOnlyCanonicalSafeCustomDataDirectory()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "uninstall_linux.sh");

        Assert.Contains("DATA_DIRECTORY=${DATA_DIRECTORY:-}", script);
        Assert.Contains("realpath -m \"$path\"", script);
        Assert.Contains("rm -rf \"$DATA_DIRECTORY_RESOLVED\"", script);
        Assert.Contains("RESIDUE_PATHS+=(\"$DATA_DIRECTORY_RESOLVED\")", script);
        Assert.Contains("if [ \"$KEEP_DATA\" = false ]; then", script);
    }

    [Theory]
    [InlineData("/var/lib/mysql-validation-ubuntu24", true)]
    [InlineData("/var/lib/mysql/custom", true)]
    [InlineData("/opt/mysql-validation", true)]
    [InlineData("/data/mysql-validation", true)]
    [InlineData("/srv/mysql-validation", true)]
    [InlineData("/var/lib/mysql/../../home", false)]
    [InlineData("/var/lib", false)]
    [InlineData("/opt", false)]
    [InlineData("/tmp/mysql-validation", false)]
    [InlineData("/data/mysql-*", false)]
    [InlineData("/data/mysql/foo?bar", false)]
    [InlineData("/data/mysql/foo[bar]", false)]
    [InlineData("/data/mysql/foo bar", false)]
    [InlineData("/data/mysql/foo\nbar", false)]
    [InlineData("/data/mysql/foo\rbar", false)]
    public void MySqlUninstallScript_CustomDataDirectoryGuardRejectsUnsafePaths(string path, bool expectedSafe)
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "uninstall_linux.sh");
        Assert.Contains("if [[ ! \"$resolved_path\" =~ ^[A-Za-z0-9._/-]+$ ]]; then", script);
        Assert.Contains("自定义数据目录: <不安全路径，已拒绝>", script);
        Assert.Contains("跳过不安全的自定义数据目录清理：<不安全路径，已拒绝>", script);
        Assert.DoesNotContain("跳过不安全的自定义数据目录清理：$DATA_DIRECTORY", script);

        var resolved = NormalizeUnixPath(path);
        var isSafe = IsExpectedSafeMySqlDataDirectory(resolved);

        Assert.Equal(expectedSafe, isSafe);
        Assert.DoesNotContain("..", resolved);
    }

    [Fact]
    public void MySqlUninstallScript_KeepDataBranchDoesNotRemoveCustomDataDirectory()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "uninstall_linux.sh").Replace("\r\n", "\n");
        var keepDataBranch = script.Substring(script.IndexOf("else\n    echo \"保留数据模式已开启", StringComparison.Ordinal));

        Assert.DoesNotContain("DATA_DIRECTORY_RESOLVED", keepDataBranch.Split("SYSTEMD_SERVICE_GLOBS=(", 2)[0]);
    }

    [Fact]
    public void MySqlInstallScript_DoesNotPrintRootPassword()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "install_common.sh");
        var logOutputLines = script
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith("echo ", StringComparison.Ordinal) ||
                           line.StartsWith("printf ", StringComparison.Ordinal))
            .Where(line => !line.Contains("| debconf-set-selections", StringComparison.Ordinal));

        Assert.DoesNotContain("Password=$ROOT_PASSWORD", script);
        Assert.DoesNotContain(logOutputLines, line => line.Contains("ROOT_PASSWORD", StringComparison.Ordinal));
        Assert.Contains("Password=<已隐藏>", script);
        Assert.Contains("ROOT_PASSWORD=${ROOT_PASSWORD:-}", script);
        Assert.Contains("ALLOW_REMOTE=${ALLOW_REMOTE:-false}", script);
        Assert.Contains("错误：必须显式提供非默认 MySQL root 密码", script);
        Assert.DoesNotContain("ROOT_PASSWORD=${ROOT_PASSWORD:-MySql@123}", script);
        Assert.DoesNotContain("ALLOW_REMOTE=${ALLOW_REMOTE:-true}", script);
        Assert.Contains("current_bind_address=$(grep -E", script);
        Assert.Contains("sed -E 's/^[[:space:]]+|[[:space:]]+$//g'", script);
        Assert.Contains("错误：已启用远程访问，但 bind-address 未配置为 0.0.0.0", script);
    }

    [Fact]
    public void MariaDbInstallScript_DoesNotPrintRootPassword()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MariaDB", "install_common.sh");
        var logOutputLines = script
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith("echo ", StringComparison.Ordinal) ||
                           line.StartsWith("printf ", StringComparison.Ordinal));

        Assert.DoesNotContain("Password=$ROOT_PASSWORD", script);
        Assert.DoesNotContain(logOutputLines, line => line.Contains("ROOT_PASSWORD", StringComparison.Ordinal));
        Assert.Contains("Password=<已隐藏>", script);
    }

    [Fact]
    public void MySqlInstallScript_PreparesOnlyResolvedSafeCustomDataDirectoryBeforeSettingDatadir()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "install_common.sh");

        Assert.Contains("resolve_safe_custom_data_directory()", script);
        Assert.Contains("realpath -m \"$path\"", script);
        Assert.Contains("if [[ ! \"$resolved_path\" =~ ^[A-Za-z0-9._/-]+$ ]]; then", script);
        Assert.Contains("错误：MySQL 自定义数据目录包含不安全字符：<不安全路径，已拒绝>", script);
        Assert.Contains("错误：不安全的 MySQL 自定义数据目录：<不安全路径，已拒绝>", script);
        Assert.DoesNotContain("错误：MySQL 自定义数据目录包含不安全字符：$path", script);
        Assert.DoesNotContain("错误：不安全的 MySQL 自定义数据目录：$path", script);
        Assert.Contains("DATA_DIRECTORY_RESOLVED=\"$resolved_path\"", script);
        Assert.Contains("/var/lib/mysql/*|/var/lib/mysql-*|/opt/mysql/*|/opt/mysql-*|/data/mysql/*|/data/mysql-*|/srv/mysql/*|/srv/mysql-*", script);
        Assert.Contains("configure_custom_data_directory()", script);
        Assert.Contains("rsync -a \"$source_dir\"/ \"$target_dir\"/", script);
        Assert.Contains("cp -a \"$source_dir\"/. \"$target_dir\"/", script);
        Assert.Contains("chown -R mysql:mysql \"$target_dir\"", script);
        Assert.Contains("apply_mysql_data_directory_security_context()", script);
        Assert.Contains("getenforce", script);
        Assert.Contains("escaped_target_dir=\"${target_dir//./\\\\.}\"", script);
        Assert.Contains("context_pattern=\"${escaped_target_dir}(/.*)?\"", script);
        Assert.Contains("validate_mysql_port()", script);
        Assert.Contains("if [[ ! \"$port\" =~ ^[0-9]+$ ]] || [ \"$port\" -lt 1 ] || [ \"$port\" -gt 65535 ]; then", script);
        Assert.Contains("错误：无效的 MySQL 端口：<不安全端口，已拒绝>", script);
        Assert.Contains("validate_mysql_port \"$PORT\"", script);
        Assert.True(script.IndexOf("validate_mysql_port \"$PORT\"", StringComparison.Ordinal) <
                    script.IndexOf("ensure_mysqld_option \"$CONFIG_FILE\" port \"$PORT\"", StringComparison.Ordinal));
        Assert.Contains("警告：SELinux 已启用但缺少 semanage，无法持久授权 MySQL 端口 $port，正在尝试加载端口级 MySQL 策略模块", script);
        Assert.Contains("install_mysql_port_selinux_policy_module()", script);
        Assert.Contains("remote_installer_mysql_port_${port}", script);
        Assert.Contains("portcon tcp $port system_u:object_r:mysqld_port_t:s0;", script);
        Assert.Contains("(portcon tcp $port (system_u object_r mysqld_port_t ((s0)(s0))))", script);
        Assert.Contains("semodule -i \"$cil_file\"", script);
        Assert.Contains("semodule -i \"$pp_file\"", script);
        Assert.Contains("extract_dir=$(mktemp -d \"/tmp/remote-installer-mysql.XXXXXX\")", script);
        Assert.Contains("chmod 700 \"$extract_dir\"", script);
        Assert.DoesNotContain("/tmp/mysql_extract_$(date +%s)", script);
        Assert.Contains("write_mysql_selinux_state_file", script);
        Assert.Contains("chmod 700 \"$state_dir\"", script);
        Assert.Contains("chmod 600 \"$state_file\"", script);
        Assert.Contains("module_created=$module_created", script);
        Assert.Contains("write_mysql_selinux_state_file \"$state_file\" \"$module_name\" \"$port\" true", script);
        Assert.Contains("跳过本安装器所有权登记", script);
        Assert.Contains("selinux_port_has_any_type", script);
        Assert.Contains("错误：SELinux 端口 $port 已被其他类型占用，拒绝改写现有策略", script);
        Assert.Contains("错误：SELinux Enforcing 下无法授权 MySQL 端口 $port", script);
        Assert.DoesNotContain("allow mysqld_t unreserved_port_t:tcp_socket name_bind;", script);
        Assert.DoesNotContain("(allow mysqld_t unreserved_port_t (tcp_socket (name_bind)))", script);
        Assert.DoesNotContain("semanage port -m -t mysqld_port_t -p tcp \"$port\"", script);
        Assert.Contains("configure_mysql_systemd_selinux_context_override()", script);
        Assert.Contains("remote-installer-selinux.conf", script);
        Assert.Contains("ExecStartPre=", script);
        Assert.Contains("ExecStartPre=/usr/bin/mysqld_pre_systemd", script);
        Assert.Contains("ExecStartPre=/usr/bin/chcon -R -t mysqld_db_t $target_dir", script);
        Assert.Contains("configure_mysql_systemd_selinux_context_override \"$SERVICE_NAME\" \"$DATA_DIRECTORY_RESOLVED\"", script);
        Assert.True(script.IndexOf("configure_mysql_systemd_selinux_context_override \"$SERVICE_NAME\" \"$DATA_DIRECTORY_RESOLVED\"", StringComparison.Ordinal) <
                    script.IndexOf("systemctl daemon-reload || true", StringComparison.Ordinal));
        Assert.Contains("mysql_data_directory_has_selinux_context()", script);
        Assert.Contains("ls -Zd \"$target_dir\"", script);
        Assert.Contains("MySQL 自定义数据目录 SELinux 上下文仍不是 mysqld_db_t", script);
        Assert.Contains("apply_mysql_port_security_context()", script);
        Assert.Contains("semanage port -a -t mysqld_port_t -p tcp \"$port\"", script);
        Assert.Contains("apply_mysql_port_security_context \"$PORT\"", script);
        Assert.Contains("semanage fcontext -a -t mysqld_db_t \"$context_pattern\"", script);
        Assert.Contains("semanage fcontext -m -t mysqld_db_t \"$context_pattern\"", script);
        Assert.Contains("restorecon -R \"$target_dir\"", script);
        Assert.Contains("chcon -R -t mysqld_db_t \"$target_dir\"", script);
        Assert.True(script.IndexOf("chown -R mysql:mysql \"$target_dir\"", StringComparison.Ordinal) <
                    script.IndexOf("apply_mysql_data_directory_security_context \"$target_dir\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("apply_mysql_data_directory_security_context \"$target_dir\"", StringComparison.Ordinal) <
                    script.IndexOf("/etc/apparmor.d/local/usr.sbin.mysqld", StringComparison.Ordinal));
        Assert.Contains("/etc/apparmor.d/local/usr.sbin.mysqld", script);
        Assert.Contains("apparmor_parser -r /etc/apparmor.d/usr.sbin.mysqld", script);
        Assert.Contains("resolve_safe_custom_data_directory \"$DATA_DIRECTORY\"", script);
        Assert.Contains("configure_custom_data_directory \"$DATA_DIRECTORY_RESOLVED\"", script);
        Assert.Contains("ensure_mysqld_option \"$CONFIG_FILE\" datadir \"$DATA_DIRECTORY_RESOLVED\"", script);
        Assert.True(script.IndexOf("resolve_safe_custom_data_directory \"$DATA_DIRECTORY\"", StringComparison.Ordinal) <
                    script.IndexOf("configure_custom_data_directory \"$DATA_DIRECTORY_RESOLVED\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("configure_custom_data_directory \"$DATA_DIRECTORY_RESOLVED\"", StringComparison.Ordinal) <
                    script.IndexOf("ensure_mysqld_option \"$CONFIG_FILE\" datadir \"$DATA_DIRECTORY_RESOLVED\"", StringComparison.Ordinal));
        Assert.DoesNotContain("configure_custom_data_directory \"$DATA_DIRECTORY\"", script);
        Assert.DoesNotContain("datadir \"$DATA_DIRECTORY\"", script);
    }

    [Fact]
    public void MySqlInstallScript_LeavesVersionedRpmRequirementsToYumTransactionTest()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "install_common.sh");

        Assert.Contains("\"\"|rpmlib\\(*|/bin/sh|/bin/bash|/usr/bin/*|/usr/sbin/*)", script);
        Assert.Contains("带版本比较的 capability 交给 yum 事务预检判断", script);
        Assert.Contains("*\" = \"*|*\" >= \"*|*\" <= \"*|*\" > \"*|*\" < \"*)", script);
        Assert.Contains("validate_redhat_offline_transaction \"$package_dir\" \"${rpm_files[@]}\"", script);
        Assert.True(script.IndexOf("case \"$normalized_requirement\"", StringComparison.Ordinal) <
                    script.IndexOf("if local_rpm_capabilities_satisfy_requirement", StringComparison.Ordinal));
        Assert.True(script.IndexOf("validate_redhat_offline_transaction \"$package_dir\" \"${rpm_files[@]}\"", StringComparison.Ordinal) <
                    script.IndexOf("yum --disablerepo='*' remove -y mariadb-libs", StringComparison.Ordinal));
    }

    [Fact]
    public void MariaDbInstallScript_PreflightsDebianOfflineDependenciesBeforeAptInstall()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MariaDB", "install_common.sh");

        Assert.Contains("validate_debian_offline_dependencies()", script);
        Assert.Contains("debian_package_installed_or_available()", script);
        Assert.Contains("package_files=(\"$package_dir\"/\"${package_name}\"_*.deb)", script);
        Assert.Contains("[ ${#package_files[@]} -gt 0 ]", script);
        Assert.Contains("server_deb_files=(\"$package_dir\"/mariadb-server_*.deb \"$package_dir\"/mariadb-server-core_*.deb)", script);
        Assert.Contains("[ ${#server_deb_files[@]} -eq 0 ]", script);
        Assert.Contains("server_rpm_files=(\"$package_dir\"/MariaDB-server-*.rpm \"$package_dir\"/mariadb-server-*.rpm)", script);
        Assert.Contains("[ ${#server_rpm_files[@]} -eq 0 ]", script);
        Assert.DoesNotContain("ls \"$package_dir\"/\"${package_name}\"_*.deb", script);
        Assert.DoesNotContain("ls \"$package_dir\"/mariadb-server_*.deb", script);
        Assert.DoesNotContain("ls \"$package_dir\"/mariadb-server-core_*.deb", script);
        Assert.DoesNotContain("ls \"$package_dir\"/MariaDB-server-*.rpm", script);
        Assert.DoesNotContain("ls \"$package_dir\"/mariadb-server-*.rpm", script);
        Assert.Contains("libconfig-inifiles-perl", script);
        Assert.Contains("libdbi-perl", script);
        Assert.Contains("lsof", script);
        Assert.Contains("rsync", script);
        Assert.Contains("错误：严格离线模式下，MariaDB 离线目录缺少以下 Debian 依赖包，且目标系统未安装", script);
        Assert.Contains("printf 'deb [trusted=yes] file://%s ./\\n'", script);
        Assert.Contains("-o Dir::Etc::sourcelist=\"$repo_list\"", script);
        Assert.Contains("-o Dir::Etc::sourceparts=/dev/null", script);
        Assert.Contains("检测到 Packages 元数据，优先使用本地 trusted file:// APT 仓库安装 MariaDB...", script);
        Assert.Contains("当前 APT 操作仅使用 trusted file:// 本地仓库，不读取其它 sourceparts。", script);
        Assert.DoesNotContain("检测到 Packages 与签名 key", script);
        Assert.Contains("apt-get -y -qq --no-install-recommends", script);
        Assert.True(script.IndexOf("validate_debian_offline_dependencies \"$package_dir\"", StringComparison.Ordinal) <
                    script.IndexOf("install_debian_from_repository \"$package_dir\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MariaDbInstallScript_SkipsOptionalRedhatTestRpm()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MariaDB", "install_common.sh");

        Assert.Contains("is_optional_redhat_mariadb_rpm()", script);
        Assert.Contains("MariaDB-test-*.rpm|mariadb-test-*.rpm)", script);
        Assert.Contains("跳过非运行必需的 MariaDB 测试 RPM", script);
        Assert.Contains("validate_redhat_offline_dependencies \"$package_dir\" \"${rpm_files[@]}\"", script);
        Assert.Contains("validate_redhat_offline_transaction \"$package_dir\" \"${rpm_files[@]}\"", script);
        Assert.Contains("yum --disablerepo='*' localinstall -y \"${rpm_files[@]}\"", script);
        Assert.True(script.IndexOf("if is_optional_redhat_mariadb_rpm \"$rpm_file\";", StringComparison.Ordinal) <
                    script.IndexOf("validate_redhat_offline_dependencies \"$package_dir\" \"${rpm_files[@]}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MySqlUninstallScript_RemovesCustomDataDirectoryAppArmorRules()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "uninstall_linux.sh");

        Assert.Contains("remove_custom_data_directory_apparmor_rules()", script);
        Assert.Contains("awk -v read_rule=\"  $target_dir/ r,\" -v write_rule=\"  $target_dir/** rwk,\"", script);
        Assert.Contains("remove_custom_data_directory_apparmor_rules \"$DATA_DIRECTORY_RESOLVED\"", script);
        Assert.Contains("apparmor_parser -r /etc/apparmor.d/usr.sbin.mysqld", script);
    }

    [Fact]
    public void MySqlUninstallScript_RemovesInstallerOwnedSelinuxPortPolicyResources()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "MySQL", "uninstall_linux.sh");

        Assert.Contains("remove_mysql_port_selinux_resources()", script);
        Assert.Contains("local state_dir=\"/var/lib/remote-installer/mysql\"", script);
        Assert.Contains("grep -Fxq \"owner=remote-installer\"", script);
        Assert.Contains("owner_id=$(stat -c '%u' \"$state_file\"", script);
        Assert.Contains("permissions=$(stat -c '%a' \"$state_file\"", script);
        Assert.Contains("跳过不可信 SELinux 状态文件", script);
        Assert.Contains("[[ ! \"$port\" =~ ^[0-9]+$ ]]", script);
        Assert.Contains("expected_state_file=\"$state_dir/selinux-port-${port}.state\"", script);
        Assert.Contains("remote_installer_mysql_port_${port}", script);
        Assert.Contains("module_created=true", script);
        Assert.Contains("semodule -r \"$module_name\"", script);
        Assert.Contains("semanage port -d -p tcp \"$port\"", script);
        Assert.True(script.IndexOf("remove_mysql_port_selinux_resources", StringComparison.Ordinal) <
                    script.IndexOf("systemctl daemon-reload || true", StringComparison.Ordinal));
    }

    private static string NormalizeUnixPath(string path)
    {
        var parts = new Stack<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.Pop();
                }
                continue;
            }

            parts.Push(part);
        }

        return "/" + string.Join('/', parts.Reverse());
    }

    private static bool IsExpectedSafeMySqlDataDirectory(string resolvedPath)
    {
        return ContainsOnlySafeMySqlPathCharacters(resolvedPath) &&
               (resolvedPath.StartsWith("/var/lib/mysql/", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/var/lib/mysql-", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/opt/mysql/", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/opt/mysql-", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/data/mysql/", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/data/mysql-", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/srv/mysql/", StringComparison.Ordinal) ||
                resolvedPath.StartsWith("/srv/mysql-", StringComparison.Ordinal));
    }

    private static bool ContainsOnlySafeMySqlPathCharacters(string path)
    {
        return path.All(character => char.IsAsciiLetterOrDigit(character) || character is '/' or '.' or '_' or '-');
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
