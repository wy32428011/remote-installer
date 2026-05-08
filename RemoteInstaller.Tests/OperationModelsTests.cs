using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationModelsTests
{
    [Fact]
    public void RemoteCommandResult_SuccessReflectsExitCodeZeroWithoutTimeout()
    {
        var result = new RemoteCommandResult
        {
            Command = "echo ok",
            ExitCode = 0,
            Stdout = "ok",
            Stderr = string.Empty,
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(15)
        };

        Assert.True(result.Succeeded);
        Assert.False(result.Failed);
    }

    [Fact]
    public void ApplicationStatusEvidence_TreatsRuntimeEvidenceAsInstalledEvidence()
    {
        var evidence = new ApplicationStatusEvidence
        {
            ProcessFound = true
        };

        Assert.True(evidence.HasRuntimeEvidence);
        Assert.True(evidence.HasInstalledEvidence);
        Assert.False(evidence.HasOnlyResidue);
    }

    [Fact]
    public void PackageResolution_NotFoundCarriesHintAndMissingDependencies()
    {
        var resolution = PackageResolution.NotFound(
            "缺少 RabbitMQ 离线依赖",
            new[] { "erlang-base", "logrotate" });

        Assert.False(resolution.Found);
        Assert.Equal("缺少 RabbitMQ 离线依赖", resolution.Hint);
        Assert.Equal(new[] { "erlang-base", "logrotate" }, resolution.MissingDependencies);
    }
}
