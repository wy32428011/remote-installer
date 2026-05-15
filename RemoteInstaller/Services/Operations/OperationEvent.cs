using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public enum OperationEventKind
{
    Progress,
    Log,
    StatusChanged,
    Completed,
    Failed,
    Canceled
}

public sealed class OperationEvent
{
    public OperationEventKind Kind { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public double Percent { get; init; }
    public LogEntry? LogEntry { get; init; }
    public ApplicationStatus? Status { get; init; }
    public OperationResult? Result { get; init; }

    public static OperationEvent Progress(string taskId, string stage, double percent)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.Progress,
            TaskId = taskId,
            Stage = stage,
            Percent = Math.Clamp(percent, 0, 100)
        };
    }

    public static OperationEvent Log(string taskId, LogEntry entry)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.Log,
            TaskId = taskId,
            LogEntry = entry
        };
    }

    public static OperationEvent StatusChanged(string taskId, ApplicationStatus status)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.StatusChanged,
            TaskId = taskId,
            Status = status
        };
    }

    public static OperationEvent Completed(OperationResult result)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.Completed,
            TaskId = result.TaskId,
            Result = result,
            Status = result.Status
        };
    }

    public static OperationEvent Failed(OperationResult result)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.Failed,
            TaskId = result.TaskId,
            Result = result,
            Status = result.Status
        };
    }

    public static OperationEvent Canceled(OperationResult result)
    {
        return new OperationEvent
        {
            Kind = OperationEventKind.Canceled,
            TaskId = result.TaskId,
            Result = result,
            Status = result.Status
        };
    }
}
