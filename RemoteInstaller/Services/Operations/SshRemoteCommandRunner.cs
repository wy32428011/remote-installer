namespace RemoteInstaller.Services.Operations;

public sealed class SshRemoteCommandRunner : IRemoteCommandRunner
{
    private readonly SshService _sshService;

    public SshRemoteCommandRunner(SshService sshService)
    {
        _sshService = sshService;
    }

    public Task<RemoteCommandResult> ExecuteAsync(
        string command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        return _sshService.ExecuteCommandResultAsync(command, onOutput, cancellationToken);
    }
}
