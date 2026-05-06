using System;
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class MosquittoScriptTests
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

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void InstallLinuxScript_RequiresExplicitOfflinePackagePath()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.Contains("PACKAGE_PATH=\"${PACKAGE_PATH:-}\"", script);
        Assert.Contains("Mosquitto 仅支持离线安装，必须显式提供 PACKAGE_PATH", script);
        Assert.DoesNotContain("自动发现安装包", script);
    }

    [Fact]
    public void InstallLinuxScript_DoesNotFallbackToOnlineRepositories()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.DoesNotContain("apt-get update", script);
        Assert.DoesNotContain("apt-get install -f", script);
        Assert.DoesNotContain("apt-get install -y mosquitto", script);
        Assert.DoesNotContain("yum install -y mosquitto", script);
        Assert.Contains("dpkg -i \"${ROOT_DEBS[@]}\"", script);
        Assert.Contains("yum --disablerepo='*' -y localinstall", script);
        Assert.Contains("package_matches_arch", script);
        Assert.Contains("detect_arch", script);
    }

    [Fact]
    public void InstallLinuxScript_UsesPasswordFileAuthenticationModel()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.Contains("PASSWORD_FILE_INPUT=\"${PASSWORD_FILE:-}\"", script);
        Assert.Contains("load_password()", script);
        Assert.Contains("rm -f \"$PASSWORD_FILE_INPUT\"", script);
        Assert.Contains("if [ \"$has_username\" -ne \"$has_password\" ]; then", script);
        Assert.Contains("command_exists mosquitto_passwd || fail \"启用认证模式需要 mosquitto_passwd\"", script);
        Assert.Contains("mosquitto_passwd -b -c \"$PASSWORD_FILE\" \"$USERNAME\" \"$PASSWORD\"", script);
        Assert.Contains("allow_anonymous false", script);
        Assert.Contains("password_file ${PASSWORD_FILE}", script);
        Assert.Contains("allow_anonymous true", script);
        Assert.DoesNotContain("Dashboard API", script);
        Assert.DoesNotContain("configure_authentication()", script);
    }

    [Fact]
    public void InstallLinuxScript_StartsServiceAndVerifiesSingleMqttPort()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.Contains("systemctl daemon-reload || true", script);
        Assert.Contains("systemctl enable \"$SERVICE_NAME\"", script);
        Assert.Contains("systemctl restart \"$SERVICE_NAME\"", script);
        Assert.Contains("verify_installation() {", script);
        Assert.Contains("if ! systemctl is-active --quiet \"$SERVICE_NAME\"; then", script);
        Assert.Contains("dpkg-query -W -f='${Status} ${Package}\\n' mosquitto", script);
        Assert.Contains("if ! is_port_listening \"$MQTT_PORT\"; then", script);
        Assert.DoesNotContain("WEBSOCKET_PORT", script);
    }

    [Fact]
    public void InstallLinuxScript_DefinesArchitectureFilteringAndOsDetectionHelpers()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.Contains("normalize_arch() {", script);
        Assert.Contains("detect_arch() {", script);
        Assert.Contains("package_matches_arch() {", script);
        Assert.Contains("get_os_info() {", script);
        Assert.Contains("TARGET_ARCH=\"$(detect_arch)\"", script);
    }

    [Fact]
    public void InstallLinuxScript_FiltersPackagesByDetectedArchitectureBeforeInstall()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_linux.sh");

        Assert.Contains("ROOT_DEBS=()", script);
        Assert.Contains("ROOT_RPMS=()", script);
        Assert.Contains("if package_matches_arch \"$package\" \"$TARGET_ARCH\"; then", script);
        Assert.Contains("Ubuntu Mosquitto 离线目录缺少与目标架构匹配的 mosquitto-*.deb / mosquitto_*.deb 主包", script);
        Assert.Contains("CentOS 7 Mosquitto 离线目录缺少与目标架构匹配的 mosquitto-*.rpm 主包", script);
    }

    [Fact]
    public void InstallWindowsScript_RequiresAdminExplicitPackagePathAndSupportsOptionalCredentials()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_windows.ps1");

        Assert.Contains("Test-Admin", script);
        Assert.Contains("请以管理员身份运行此脚本", script);
        Assert.Contains("Mosquitto 仅支持离线安装，必须显式提供有效的 PackagePath", script);
        Assert.Contains("仅支持 zip 或已解压目录", script);
        Assert.Contains("[string]$Username = \"\"", script);
        Assert.Contains("[string]$PasswordFile = \"\"", script);
        Assert.DoesNotContain("[string]$Password = \"\"", script);
        Assert.Contains("Read-PasswordFromFile", script);
        Assert.Contains("$Password = Read-PasswordFromFile -Path $PasswordFile", script);
        Assert.Contains("Get-AuthMode", script);
        Assert.Contains("Mosquitto 用户名和密码必须同时提供，或同时留空以启用匿名访问", script);
    }

    [Fact]
    public void InstallWindowsScript_RegistersServiceConfiguresFirewallAndVerifiesPorts()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Mosquitto", "install_windows.ps1");

        Assert.Contains("New-Service -Name $ServiceName", script);
        Assert.Contains("New-NetFirewallRule -DisplayName \"Mosquitto MQTT\"", script);
        Assert.Contains("Wait-PortListening -Port $MqttPort", script);
        Assert.DoesNotContain("WebSocket", script);
        Assert.Contains("Get-Process -Name mosquitto", script);
        Assert.Contains("C:\\Program Files\\mosquitto", script);
    }

    [Fact]
    public void InstallConfigViewModel_RoutesMosquittoToDedicatedOfflineResolverAndForcesLocalOnly()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("SupportsOnlineInstall => !IsMariaDb && !IsMosquitto", viewModel);
        Assert.Contains("Mosquitto 仅支持本地资源安装，请使用 Scripts/Mosquitto 下的离线目录。", viewModel);
        Assert.Contains("TryResolveMosquittoLocalPackage", viewModel);
        Assert.Contains("TryGetCompatibleMosquittoOfflineFolder", viewModel);
        Assert.Contains("TryValidateMosquittoOfflinePath", viewModel);
        Assert.Contains("NormalizeMosquittoCredentialParameter", viewModel);
        Assert.Contains("TryValidateMosquittoCredentialPair", viewModel);
        Assert.Contains("Mosquitto 用户名和密码必须同时填写，或同时留空以启用匿名访问。", viewModel);
        Assert.Contains("GetDefaultMosquittoVersionForCurrentHost", viewModel);
        Assert.Contains("22 => \"2.0.22\"", viewModel);
        Assert.Contains("24 => \"2.1.2\"", viewModel);
        Assert.Contains("return \"1.6.10\"", viewModel);
    }

    [Fact]
    public void InstallConfigViewModel_AllowsMosquittoScriptRootsToBeOverriddenForIsolatedTests()
    {
        var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "InstallConfigViewModel.cs");

        Assert.Contains("ScriptRootOverridesFactory", viewModel);
        Assert.Contains("AsyncLocal<Func<IEnumerable<string>>?>", viewModel);
        Assert.Contains("Path.Combine(root, \"Mosquitto\", offlineFolder)", viewModel);
    }

    [Fact]
    public void InstallerService_DefinesMosquittoServiceExecutableAndProcessMappings()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("\"mosquitto\" => \"mosquitto\"", installerService);
        Assert.Contains("mosquitto -h 2>&1", installerService);
        Assert.Contains("dpkg-query -W -f='${Status} ${Package}\\n' mosquitto", installerService);
        Assert.Contains("systemctl is-active --quiet mosquitto", installerService);
        Assert.Contains("Get-ChildItem -Path 'C:\\Program Files\\mosquitto'", installerService);
    }

    [Fact]
    public void InstallerService_UsesExplicitEscapedArgumentsAndSecretFilesForMosquitto()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("BuildWindowsPowerShellArguments", installerService);
        Assert.Contains("EscapePowerShellSingleQuotedValue", installerService);
        Assert.Contains("ReplacePowerShellPlaceholders", installerService);
        Assert.Contains("ValidateBashEnvironmentVariableName", installerService);
        Assert.Contains("PrepareMosquittoSecretFilesAsync", installerService);
        Assert.Contains("!string.Equals(param.Key, \"PASSWORD\"", installerService);
        Assert.Contains("parameters[\"PASSWORD_FILE\"]", installerService);
        Assert.DoesNotContain("$env:{k}=\"{parameters[k]}\"", installerService);
    }

    [Fact]
    public void InstallerService_NormalizesLinuxScriptLineEndingsBeforeExecution()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("Replace(\"\\r\\n\", \"\\n\")", installerService);
        Assert.Contains("Replace(\"\\r\", \"\\n\")", installerService);
        Assert.Contains("UploadTextAsync(content, remoteScriptPath, host.OsType, cancellationToken)", installerService);
    }
}
