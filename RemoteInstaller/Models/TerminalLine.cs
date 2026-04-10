namespace RemoteInstaller.Models;

public class TerminalLine
{
    public string Prompt { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public TerminalLineType Type { get; set; } = TerminalLineType.Output;
}

public enum TerminalLineType
{
    Command,
    Output,
    Error,
    System
}
