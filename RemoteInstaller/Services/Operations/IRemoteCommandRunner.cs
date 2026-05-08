namespace RemoteInstaller.Services.Operations;

public interface IRemoteCommandRunner
{
    Task<RemoteCommandResult> ExecuteAsync(
        string command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}
