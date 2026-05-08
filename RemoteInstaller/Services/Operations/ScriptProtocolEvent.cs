namespace RemoteInstaller.Services.Operations;

public enum ScriptProtocolEventKind
{
    Log,
    Progress,
    Status,
    Result
}

public sealed class ScriptProtocolEvent
{
    public ScriptProtocolEventKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
