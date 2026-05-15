namespace RemoteInstaller.Services.Operations;

public sealed class UiOperationEventBuffer
{
    private readonly object _syncRoot = new();
    private readonly List<OperationEvent> _events = new();

    public void Enqueue(OperationEvent operationEvent)
    {
        lock (_syncRoot)
        {
            _events.Add(operationEvent);
        }
    }

    public UiOperationEventBatch Drain()
    {
        List<OperationEvent> snapshot;
        lock (_syncRoot)
        {
            snapshot = _events.ToList();
            _events.Clear();
        }

        var progressEvents = snapshot
            .Where(item => item.Kind == OperationEventKind.Progress)
            .GroupBy(item => item.TaskId)
            .Select(group => group.Last())
            .ToList();

        var logEvents = snapshot
            .Where(item => item.Kind == OperationEventKind.Log)
            .ToList();

        var statusEvents = snapshot
            .Where(item => item.Kind == OperationEventKind.StatusChanged)
            .GroupBy(item => item.TaskId)
            .Select(group => group.Last())
            .ToList();

        var terminalEvents = snapshot
            .Where(item => item.Kind is OperationEventKind.Completed or OperationEventKind.Failed or OperationEventKind.Canceled)
            .ToList();

        return new UiOperationEventBatch(progressEvents, logEvents, statusEvents, terminalEvents);
    }
}

public sealed class UiOperationEventBatch
{
    public UiOperationEventBatch(
        IReadOnlyList<OperationEvent> progressEvents,
        IReadOnlyList<OperationEvent> logEvents,
        IReadOnlyList<OperationEvent> statusEvents,
        IReadOnlyList<OperationEvent> terminalEvents)
    {
        ProgressEvents = progressEvents;
        LogEvents = logEvents;
        StatusEvents = statusEvents;
        TerminalEvents = terminalEvents;
    }

    public IReadOnlyList<OperationEvent> ProgressEvents { get; }
    public IReadOnlyList<OperationEvent> LogEvents { get; }
    public IReadOnlyList<OperationEvent> StatusEvents { get; }
    public IReadOnlyList<OperationEvent> TerminalEvents { get; }
    public bool HasChanges => ProgressEvents.Count > 0 || LogEvents.Count > 0 || StatusEvents.Count > 0 || TerminalEvents.Count > 0;
}
