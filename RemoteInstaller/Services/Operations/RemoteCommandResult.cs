namespace RemoteInstaller.Services.Operations;

public sealed class RemoteCommandResult
{
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public TimeSpan Duration { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
    public bool Failed => !Succeeded;

    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Stderr))
            {
                return Stdout;
            }

            if (string.IsNullOrWhiteSpace(Stdout))
            {
                return Stderr;
            }

            return Stdout + Environment.NewLine + Stderr;
        }
    }
}
