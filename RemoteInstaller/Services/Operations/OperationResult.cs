using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class OperationResult
{
    public OperationType Type { get; init; }
    public required RemoteHost Host { get; init; }
    public required ApplicationInfo Application { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public ApplicationStatus? Status { get; init; }
    public Models.TaskStatus TaskStatus { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool HasWarning { get; init; }
    public bool Succeeded => TaskStatus == Models.TaskStatus.Completed;
    public bool Canceled => TaskStatus == Models.TaskStatus.Cancelled;

    public static OperationResult FromTask(
        OperationType type,
        RemoteHost host,
        ApplicationInfo application,
        InstallTask task,
        ApplicationStatus? status,
        bool hasWarning)
    {
        return new OperationResult
        {
            Type = type,
            Host = host,
            Application = application,
            TaskId = task.Id,
            Status = status,
            TaskStatus = task.Status,
            ErrorMessage = task.ErrorMessage ?? string.Empty,
            HasWarning = hasWarning
        };
    }
}
