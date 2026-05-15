using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class NginxOfflineInstallScriptTests
{
    [Fact]
    public void InstallLinuxScript_DoesNotRunGlobalDpkgConfigureAllDuringOfflineDebInstall()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh");

        Assert.DoesNotContain("--configure -a", script);
        Assert.Contains("dpkg \"${dpkg_opts[@]}\" -i \"${common_debs[@]}\"", script);
        Assert.Contains("dpkg \"${dpkg_opts[@]}\" -i \"${main_debs[@]}\"", script);
    }

    [Fact]
    public void InstallLinuxScript_SkipsAlreadyInstalledRedHatDependencyRpms()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh");

        Assert.Contains("select_redhat_offline_rpms()", script);
        Assert.Contains("validate_nginx_main_rpm_package()", script);
        Assert.Contains("validate_nginx_main_rpm_package \"$file_path\"", script);
        Assert.Contains("rpm_name=$(get_rpm_package_name \"$file_path\")", script);
        Assert.Contains("if [ \"$rpm_name\" != \"nginx\" ]; then", script);
        Assert.Contains("if rpm -q \"$rpm_name\" >/dev/null 2>&1; then", script);
        Assert.Contains("跳过已安装的 Nginx 离线依赖 RPM，避免触发系统包升级", script);
        Assert.Contains("run_redhat_localinstall \"${SELECTED_REDHAT_RPM_FILES[@]}\"", script);
        Assert.DoesNotContain("run_redhat_localinstall \"${rpm_files[@]}\"", script);
    }

    [Fact]
    public void InstallLinuxScript_AuthorizesValidatedSelinuxHttpPortBeforeStartingService()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh");

        Assert.Contains("validate_nginx_port()", script);
        Assert.Contains("错误：无效的 Nginx 端口：<不安全端口，已拒绝>", script);
        Assert.Contains("apply_nginx_port_security_context()", script);
        Assert.Contains("semanage port -a -t http_port_t -p tcp \"$port\"", script);
        Assert.Contains("record_nginx_semanage_port_mapping \"$port\"", script);
        Assert.Contains("install_nginx_port_selinux_policy_module()", script);
        Assert.Contains("remote_installer_nginx_port_${port}", script);
        Assert.Contains("portcon tcp $port system_u:object_r:http_port_t:s0;", script);
        Assert.Contains("(portcon tcp $port (system_u object_r http_port_t ((s0)(s0))))", script);
        Assert.Contains("write_nginx_selinux_state_file \"$state_file\" \"$module_name\" \"$port\" true", script);
        Assert.Contains("semodule -r \"$module_name\"", script);
        Assert.Contains("记录 Nginx SELinux 端口策略状态失败，已回滚策略模块", script);
        Assert.Contains("记录 Nginx SELinux 端口状态失败，已回滚端口授权", script);
        Assert.Contains("install -d -o root -g root -m 700 \"$state_dir\"", script);
        Assert.Contains("错误：SELinux 状态目录不可信：$state_dir", script);
        Assert.Contains("错误：SELinux 状态目录权限不可信：$state_dir", script);
        Assert.Contains("tmp_file=$(mktemp \"$state_dir/.selinux-port-${port}.XXXXXX\")", script);
        Assert.Contains("mv -f \"$tmp_file\" \"$state_file\"", script);
        Assert.Contains("nginx_selinux_state_file_matches_module \"$state_file\" \"$module_name\" \"$port\"", script);
        Assert.Contains("错误：SELinux 策略模块 $module_name 已存在但缺少可信状态文件，拒绝接管", script);
        Assert.Contains("chmod 600 \"$tmp_file\"", script);
        Assert.Contains("错误：SELinux 端口 $port 已被其他类型占用，拒绝改写现有策略", script);
        Assert.Contains("错误：SELinux Enforcing 下无法授权 Nginx 端口 $port", script);
        Assert.DoesNotContain("semanage port -m -t http_port_t -p tcp \"$port\"", script);
        Assert.Contains("configure_nginx_runtime_directory()", script);
        Assert.Contains("local runtime_dir=\"/run/nginx\"", script);
        Assert.Contains("local pid_file=\"$runtime_dir/nginx.pid\"", script);
        Assert.Contains("错误：Nginx 运行目录不可信：$runtime_dir", script);
        Assert.Contains("runtime_owner=\"www-data\"", script);
        Assert.Contains("install -d -o \"$runtime_owner\" -g \"$runtime_group\" -m 755 \"$runtime_dir\"", script);
        Assert.Contains("expected_owner_id=$(id -u \"$runtime_owner\"", script);
        Assert.Contains("错误：Nginx 运行目录权限不可信：$runtime_dir", script);
        Assert.Contains("remote-installer-pid.conf", script);
        Assert.Contains("PIDFile=$pid_file", script);
        Assert.Contains("systemctl daemon-reload || true", script);
        Assert.Contains("pid $pid_file;", script);
        Assert.Contains("chcon -t httpd_var_run_t \"$runtime_dir\"", script);
        Assert.True(script.IndexOf("validate_nginx_port \"$PORT\"", StringComparison.Ordinal) <
                    script.IndexOf("apply_nginx_port_security_context \"$PORT\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("configure_nginx_runtime_directory", StringComparison.Ordinal) <
                    script.IndexOf("apply_nginx_port_security_context \"$PORT\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("apply_nginx_port_security_context \"$PORT\"", StringComparison.Ordinal) <
                    script.LastIndexOf("start_service", StringComparison.Ordinal));
    }

    [Fact]
    public void UninstallLinuxScript_RemovesInstallerOwnedSelinuxPortMappings()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "uninstall_linux.sh");

        Assert.Contains("remove_nginx_port_selinux_resources()", script);
        Assert.Contains("local state_dir=\"/var/lib/remote-installer/nginx\"", script);
        Assert.Contains("is_selinux_state_dir_trusted \"$state_dir\" || return 0", script);
        Assert.Contains("跳过不可信 SELinux 状态目录", script);
        Assert.Contains("跳过权限不可信 SELinux 状态目录", script);
        Assert.Contains("grep -Fxq \"owner=remote-installer\"", script);
        Assert.Contains("owner_id=$(stat -c '%u' \"$state_file\"", script);
        Assert.Contains("permissions=$(stat -c '%a' \"$state_file\"", script);
        Assert.Contains("跳过不可信 SELinux 状态文件", script);
        Assert.Contains("[[ ! \"$port\" =~ ^[0-9]+$ ]]", script);
        Assert.Contains("expected_state_file=\"$state_dir/selinux-port-${port}.state\"", script);
        Assert.Contains("remote_installer_nginx_port_${port}", script);
        Assert.Contains("module_created=true", script);
        Assert.Contains("为避免误删管理员接管的端口，卸载脚本不会自动删除", script);
        Assert.DoesNotContain("semanage port -d -p tcp \"$port\"", script);
        Assert.Contains("cleanup_succeeded=false", script);
        Assert.Contains("删除 SELinux 策略模块 $module_name 失败，保留状态文件", script);
        Assert.Contains("保留 SELinux 状态文件以便后续人工确认", script);
        Assert.Contains("semodule -r \"$module_name\"", script);
        Assert.Contains("write_nginx_firewall_state_file()", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("firewalld_port_exists()", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("ufw_port_exists()", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("firewalld 已存在 $PORT/tcp 规则，视为管理员规则，不记录安装器状态", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("ufw 已存在 $PORT/tcp 规则，视为管理员规则，不记录安装器状态", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("tmp_file=$(mktemp \"$state_dir/.firewall-port-${port}.XXXXXX\")", ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh"));
        Assert.Contains("remove_nginx_firewall_resources()", script);
        Assert.Contains("firewall-port-*.state", script);
        Assert.Contains("Nginx 防火墙规则只会按 /var/lib/remote-installer/nginx/firewall-port-*.state 中的安装器状态文件清理", script);
        Assert.DoesNotContain("firewall-cmd --permanent --remove-service=http", script);
        Assert.DoesNotContain("ufw delete allow 'Nginx Full'", script);
        Assert.DoesNotContain("iptables -D INPUT -p tcp --dport ${port} -j ACCEPT", script);
        Assert.Contains("默认保留可能包含业务配置或证书的 Nginx 目录", script);
        Assert.DoesNotContain("\"/etc/nginx\"\n    \"/etc/nginx.conf\"", script);
        Assert.DoesNotContain("\"/usr/share/nginx\"", script);
        Assert.DoesNotContain("\"/opt/nginx\"", script);
        Assert.True(script.IndexOf("remove_nginx_firewall_resources", StringComparison.Ordinal) <
                    script.IndexOf("Nginx 防火墙规则只会按", StringComparison.Ordinal));
        Assert.True(script.IndexOf("remove_nginx_port_selinux_resources", StringComparison.Ordinal) <
                    script.IndexOf("systemctl daemon-reload", StringComparison.Ordinal));
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
