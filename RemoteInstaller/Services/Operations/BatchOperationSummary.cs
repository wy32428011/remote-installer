namespace RemoteInstaller.Services.Operations;

public sealed class BatchOperationSummary
{
    public int TotalCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int CanceledCount { get; init; }
}
