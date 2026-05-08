using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ScriptResolverTests
{
    [Fact]
    public void BuildLinuxShellScriptCommand_WrapsScriptInQuotedHereDoc()
    {
        var command = ScriptResolver.BuildLinuxShellScriptCommand("#!/bin/bash\r\necho ok\r\n");

        Assert.StartsWith("bash -s <<'REMOTE_INSTALLER_CHECK_STATUS_SCRIPT'", command);
        Assert.Contains("echo ok", command);
        Assert.EndsWith("REMOTE_INSTALLER_CHECK_STATUS_SCRIPT", command);
        Assert.DoesNotContain("\r", command);
    }

    [Fact]
    public void ExtractScriptReferenceToken_ExtractsBashScriptPath()
    {
        var token = ScriptResolver.ExtractScriptReferenceToken("export PORT=6379 && bash Scripts/Redis/check_status_linux.sh", ".sh");

        Assert.Equal("Scripts/Redis/check_status_linux.sh", token);
    }

    [Fact]
    public void ResolveConfiguredScriptFilePath_FindsProjectScript()
    {
        var resolver = new ScriptResolver();

        var path = resolver.TryResolveConfiguredScriptFilePath(
            "Scripts/Redis/check_status_linux.sh",
            OperatingSystemType.Ubuntu);

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith(Path.Combine("Scripts", "Redis", "check_status_linux.sh"), path);
    }
}
