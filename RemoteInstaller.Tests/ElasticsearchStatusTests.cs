using System;
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class ElasticsearchStatusTests
{
    [Fact]
    public void CheckStatusLinuxScript_NormalizesRunningStateAsInstalledBeforeMachineReadableOutput()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "check_status_linux.sh");

        var normalizeIndex = script.IndexOf("if [ \"$is_running\" = \"true\" ] && [ \"$is_installed\" = \"false\" ]; then", StringComparison.Ordinal);
        var machineReadableIndex = script.IndexOf("echo \"--- MACHINE READABLE ---\"", StringComparison.Ordinal);

        Assert.True(normalizeIndex >= 0, "Elasticsearch 状态脚本应在输出前将 RUNNING=true 归一为 INSTALLED=true。");
        Assert.True(machineReadableIndex >= 0, "未找到 Elasticsearch 状态脚本的机器可读输出段。");
        Assert.True(normalizeIndex < machineReadableIndex, "归一化逻辑必须发生在机器可读状态输出之前。");
        Assert.Contains("is_installed=\"true\"", script);
    }

    [Fact]
    public void InstallLinuxScript_LeavesVersionedRedHatRequirementsToYumTransactionCheck()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "install_linux.sh");

        Assert.Contains("if [[ \"$requirement\" =~ [[:space:]](=|==|>=|<=|>|<)[[:space:]] ]]; then", script);
        Assert.Contains("validate_redhat_offline_dependencies \"$PACKAGE_ROOT\"", script);
        Assert.Contains("validate_redhat_offline_transaction \"${RPM_FILES[@]}\"", script);
        Assert.True(script.IndexOf("validate_redhat_offline_dependencies \"$PACKAGE_ROOT\"", StringComparison.Ordinal) <
                    script.IndexOf("validate_redhat_offline_transaction \"${RPM_FILES[@]}\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("validate_redhat_offline_transaction \"${RPM_FILES[@]}\"", StringComparison.Ordinal) <
                    script.IndexOf("run_redhat_localinstall \"${RPM_FILES[@]}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallLinuxScript_ValidatesExternalParametersBeforeWritingShellSystemdAndYaml()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "install_linux.sh");

        Assert.Contains("validate_elasticsearch_port()", script);
        Assert.Contains("validate_elasticsearch_name()", script);
        Assert.Contains("validate_elasticsearch_memory_limit()", script);
        Assert.Contains("无效的 Elasticsearch HTTP 端口：<不安全端口，已拒绝>", script);
        Assert.Contains("^[A-Za-z0-9._-]{1,64}$", script);
        Assert.Contains("^[1-9][0-9]*[mMgG]$", script);
        Assert.True(script.IndexOf("validate_elasticsearch_port \"$HTTP_PORT\"", StringComparison.Ordinal) <
                    script.IndexOf("update_config \"http.port\" \"$HTTP_PORT\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("validate_elasticsearch_memory_limit \"$MEMORY_LIMIT\"", StringComparison.Ordinal) <
                    script.IndexOf("Environment=\"ES_JAVA_OPTS=-Xms${MEMORY_LIMIT} -Xmx${MEMORY_LIMIT}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallLinuxScript_ValidatesOfflinePackageMetadataBeforePackageManagerExecution()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "install_linux.sh");

        Assert.Contains("validate_elasticsearch_deb_package()", script);
        Assert.Contains("dpkg-deb -f \"$package_file\" Package", script);
        Assert.Contains("dpkg-deb -f \"$package_file\" Version", script);
        Assert.Contains("validate_elasticsearch_rpm_package()", script);
        Assert.Contains("rpm -qp --queryformat '%{NAME}' \"$package_file\"", script);
        Assert.Contains("rpm -qp --queryformat '%{VERSION}' \"$package_file\"", script);
        Assert.True(script.IndexOf("validate_elasticsearch_deb_package \"$DEB_MAIN_PACKAGE\"", StringComparison.Ordinal) <
                    script.IndexOf("DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i \"$DEB_MAIN_PACKAGE\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("validate_elasticsearch_rpm_package \"$RPM_MAIN_PACKAGE\"", StringComparison.Ordinal) <
                    script.IndexOf("run_redhat_localinstall \"${RPM_FILES[@]}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallLinuxScript_UsesUnpredictableTempDirectoryForTarFallback()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "install_linux.sh");

        Assert.Contains("TEMP_DIR=$(mktemp -d /tmp/es_extract.XXXXXX)", script);
        Assert.DoesNotContain("TEMP_DIR=\"/tmp/es_extract_$$\"", script);
    }

    [Fact]
    public void InstallerService_NormalizesParsedRunningStateAsInstalled()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
        var parseCheckOutput = ExtractMethod(installerService, "private void ParseCheckOutput");

        Assert.Contains("ApplicationStatusNormalizer.ApplyStatusEvents(status, events);", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.BuildEvidence(events);", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.Normalize(status, evidence);", parseCheckOutput);
        Assert.DoesNotContain("NormalizeApplicationStatus", installerService);
    }

    [Fact]
    public void CheckStatusLinuxScript_DoesNotUseRawPgrepThatCanMatchItsOwnHereDocCommand()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Elasticsearch", "check_status_linux.sh");

        Assert.DoesNotContain("es_pid=$(pgrep -f \"elasticsearch\"", script);
        Assert.Contains("find_es_pids()", script);
        Assert.Contains("STATUS_SCRIPT_PID=$$", script);
        Assert.Contains("/proc/$pid/cmdline", script);
        Assert.Contains("org\\.elasticsearch\\.bootstrap\\.Elasticsearch", script);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到方法签名：{signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"未找到方法体起始：{signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        throw new InvalidOperationException($"未找到方法体结束：{signature}");
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
