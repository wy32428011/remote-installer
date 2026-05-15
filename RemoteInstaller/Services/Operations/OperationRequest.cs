using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class OperationRequest
{
    public OperationRequest(
        OperationType type,
        RemoteHost host,
        ApplicationInfo application,
        IDictionary<string, string>? parameters = null,
        string? localPackagePath = null,
        bool keepData = false,
        bool isBatch = false)
    {
        Type = type;
        Host = host;
        Application = application;
        Parameters = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        LocalPackagePath = localPackagePath ?? string.Empty;
        KeepData = keepData;
        IsBatch = isBatch;
    }

    public OperationType Type { get; }
    public RemoteHost Host { get; }
    public ApplicationInfo Application { get; }
    public Dictionary<string, string> Parameters { get; }
    public string LocalPackagePath { get; }
    public bool KeepData { get; }
    public bool IsBatch { get; }
}
