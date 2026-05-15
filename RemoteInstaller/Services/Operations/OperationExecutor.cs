using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public interface IOperationInstaller
{
    Task<InstallTask> InstallAsync(
        RemoteHost host,
        ApplicationInfo app,
        Dictionary<string, string> parameters,
        string? localPackagePath,
        IProgress<InstallTask>? progressReporter,
        CancellationToken cancellationToken,
        IProgress<LogEntry>? logReporter);

    Task<ApplicationStatus> CheckStatusAsync(
        RemoteHost host,
        ApplicationInfo app,
        Dictionary<string, string>? parameters,
        CancellationToken cancellationToken);

    Task<InstallTask> UninstallAsync(
        RemoteHost host,
        ApplicationInfo app,
        Dictionary<string, string>? parameters,
        bool keepData,
        IProgress<InstallTask>? progressReporter,
        CancellationToken cancellationToken,
        IProgress<LogEntry>? logReporter);
}

public sealed class OperationExecutor : IBatchOperationExecutor, IDisposable
{
    private readonly IOperationInstaller _installer;
    private readonly bool _ownsInstaller;

    public OperationExecutor(IOperationInstaller installer, bool ownsInstaller = false)
    {
        _installer = installer;
        _ownsInstaller = ownsInstaller;
    }

    public async Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        return request.Type switch
        {
            OperationType.Install => await ExecuteInstallAsync(request, onEvent, cancellationToken),
            OperationType.CheckStatus => await ExecuteCheckStatusAsync(request, onEvent, cancellationToken),
            OperationType.Uninstall => await ExecuteUninstallAsync(request, onEvent, cancellationToken),
            _ => throw new InvalidOperationException($"不支持的操作类型：{request.Type}")
        };
    }

    private async Task<OperationResult> ExecuteInstallAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var currentTaskId = string.Empty;
        var task = await _installer.InstallAsync(
            request.Host,
            request.Application,
            request.Parameters,
            request.LocalPackagePath,
            CreateProgressReporter(onEvent, taskId => currentTaskId = taskId),
            cancellationToken,
            CreateLogReporter(onEvent, () => currentTaskId));
        var result = OperationResult.FromTask(OperationType.Install, request.Host, request.Application, task, null, hasWarning: false);
        PublishTerminalEvent(result, onEvent);
        return result;
    }

    private async Task<OperationResult> ExecuteCheckStatusAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var status = await _installer.CheckStatusAsync(request.Host, request.Application, request.Parameters, cancellationToken);
        var task = new InstallTask
        {
            HostId = request.Host.Id,
            HostName = request.Host.Name,
            AppId = request.Application.Id,
            AppName = request.Application.Name,
            AppVersion = request.Application.Version
        };
        task.Start();
        task.Complete();
        onEvent?.Invoke(OperationEvent.StatusChanged(task.Id, status));
        var result = OperationResult.FromTask(OperationType.CheckStatus, request.Host, request.Application, task, status, hasWarning: false);
        onEvent?.Invoke(OperationEvent.Completed(result));
        return result;
    }

    private async Task<OperationResult> ExecuteUninstallAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var currentTaskId = string.Empty;
        var task = await _installer.UninstallAsync(
            request.Host,
            request.Application,
            request.Parameters,
            request.KeepData,
            CreateProgressReporter(onEvent, taskId => currentTaskId = taskId),
            cancellationToken,
            CreateLogReporter(onEvent, () => currentTaskId));
        var result = OperationResult.FromTask(OperationType.Uninstall, request.Host, request.Application, task, null, hasWarning: false);
        PublishTerminalEvent(result, onEvent);
        return result;
    }

    private static IProgress<InstallTask> CreateProgressReporter(Action<OperationEvent>? onEvent, Action<string>? onTaskId = null)
    {
        return new CallbackProgress<InstallTask>(task =>
        {
            onTaskId?.Invoke(task.Id);
            onEvent?.Invoke(OperationEvent.Progress(task.Id, task.StageDisplayText, task.Progress));
        });
    }

    private static IProgress<LogEntry> CreateLogReporter(Action<OperationEvent>? onEvent, Func<string>? fallbackTaskId = null)
    {
        return new CallbackProgress<LogEntry>(entry =>
        {
            var taskId = string.IsNullOrWhiteSpace(entry.TaskId) ? fallbackTaskId?.Invoke() ?? string.Empty : entry.TaskId;
            onEvent?.Invoke(OperationEvent.Log(taskId, entry));
        });
    }

    private static void PublishTerminalEvent(OperationResult result, Action<OperationEvent>? onEvent)
    {
        if (result.Canceled)
        {
            onEvent?.Invoke(OperationEvent.Canceled(result));
            return;
        }

        if (result.Succeeded)
        {
            onEvent?.Invoke(OperationEvent.Completed(result));
            return;
        }

        onEvent?.Invoke(OperationEvent.Failed(result));
    }

    public void Dispose()
    {
        if (_ownsInstaller && _installer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value)
        {
            _callback(value);
        }
    }
}
