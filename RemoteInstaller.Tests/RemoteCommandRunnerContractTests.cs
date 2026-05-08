using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class RemoteCommandRunnerContractTests
{
    [Fact]
    public async Task FakeRunner_ReturnsNonZeroExitCodeWithoutThrowing()
    {
        IRemoteCommandRunner runner = new FakeRemoteCommandRunner(new RemoteCommandResult
        {
            Command = "false",
            ExitCode = 7,
            Stdout = "partial output",
            Stderr = "script failed",
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(5)
        });

        var result = await runner.ExecuteAsync("false", output => { }, CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.True(result.Failed);
        Assert.Equal("script failed", result.Stderr);
    }

    [Fact]
    public void SshService_ExposesStructuredCommandResultMethod()
    {
        var method = typeof(RemoteInstaller.Services.SshService).GetMethod("ExecuteCommandResultAsync");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RemoteCommandResult>), method!.ReturnType);
    }

    private sealed class FakeRemoteCommandRunner : IRemoteCommandRunner
    {
        private readonly RemoteCommandResult _result;

        public FakeRemoteCommandRunner(RemoteCommandResult result)
        {
            _result = result;
        }

        public Task<RemoteCommandResult> ExecuteAsync(
            string command,
            Action<string>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            onOutput?.Invoke(_result.CombinedOutput);
            return Task.FromResult(_result);
        }
    }
}
