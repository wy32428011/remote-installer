using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationDecisionPolicyTests
{
    [Fact]
    public void DecideInstall_CompletesWhenStatusIsInstalledEvenIfScriptFailed()
    {
        var command = new RemoteCommandResult { ExitCode = 1, Stderr = "service warmup failed" };
        var status = new ApplicationStatus { IsInstalled = true, IsRunning = true, InstalledVersion = "7.2.3" };

        var decision = OperationDecisionPolicy.DecideInstall(command, status);

        Assert.Equal(OperationOutcome.Completed, decision.Outcome);
        Assert.True(decision.HasWarning);
        Assert.Contains("状态检测已确认安装", decision.Message);
    }

    [Fact]
    public void DecideInstall_CompletesWhenScriptOutputReportsSuccessfulInstallEvenIfValidationMissesIt()
    {
        var command = new RemoteCommandResult
        {
            ExitCode = -1,
            Stdout = string.Join('\n', new[]
            {
                "INSTALLED: true",
                "VERSION: 3.12.0",
                "RUNNING: true",
                "MANAGEMENT_HTTP_READY: true",
                "PORT: 5672,15672",
                "STAGE:SUCCESS"
            })
        };
        var status = new ApplicationStatus { IsInstalled = false, IsRunning = false };

        var decision = OperationDecisionPolicy.DecideInstall(command, status);

        Assert.Equal(OperationOutcome.Completed, decision.Outcome);
        Assert.True(decision.HasWarning);
        Assert.Contains("脚本输出已确认安装", decision.Message);
    }

    [Fact]
    public void DecideInstall_FailsWhenScriptSucceededButStatusIsNotInstalled()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = false, IsRunning = false };

        var decision = OperationDecisionPolicy.DecideInstall(command, status);

        Assert.Equal(OperationOutcome.Failed, decision.Outcome);
        Assert.Contains("安装验证失败", decision.Message);
    }

    [Fact]
    public void DecideUninstall_FailsWhenRuntimeEvidenceStillExists()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = true, IsRunning = true };
        var evidence = new ApplicationStatusEvidence { ProcessFound = true };

        var decision = OperationDecisionPolicy.DecideUninstall(command, status, evidence);

        Assert.Equal(OperationOutcome.Failed, decision.Outcome);
        Assert.Contains("仍有运行证据", decision.Message);
    }

    [Fact]
    public void DecideUninstall_CompletesWithWarningForResidueOnly()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = false, IsRunning = false };
        var evidence = new ApplicationStatusEvidence { ServiceOnlyResidue = true };

        var decision = OperationDecisionPolicy.DecideUninstall(command, status, evidence);

        Assert.Equal(OperationOutcome.Completed, decision.Outcome);
        Assert.True(decision.HasWarning);
        Assert.Contains("残留", decision.Message);
    }
}
