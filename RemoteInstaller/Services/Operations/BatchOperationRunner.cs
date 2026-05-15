using System.Collections.Concurrent;

namespace RemoteInstaller.Services.Operations;

public interface IBatchOperationExecutor
{
    Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken);
}

public sealed class BatchOperationRunner
{
    private readonly IBatchOperationExecutor _executor;

    public BatchOperationRunner(IBatchOperationExecutor executor)
    {
        _executor = executor;
    }

    public async Task<BatchOperationSummary> RunInstallQueueAsync(
        IReadOnlyList<OperationRequest> requests,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationResult>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExecuteRequestAsync(request, onEvent, cancellationToken));
        }

        return BuildSummary(requests.Count, results);
    }

    public async Task<BatchOperationSummary> RunStatusQueueAsync(
        IReadOnlyList<OperationRequest> requests,
        int maxConcurrency,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        return await RunConcurrentQueueAsync(requests, Math.Max(1, maxConcurrency), onEvent, cancellationToken);
    }

    public async Task<BatchOperationSummary> RunUninstallQueueAsync(
        IReadOnlyList<OperationRequest> requests,
        int maxConcurrentHosts,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<OperationResult>();
        using var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentHosts));
        var hostGroups = requests.GroupBy(request => request.Host.Id).ToList();
        var tasks = hostGroups.Select(async group =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                foreach (var request in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(await ExecuteRequestAsync(request, onEvent, cancellationToken));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return BuildSummary(requests.Count, results);
    }

    private async Task<BatchOperationSummary> RunConcurrentQueueAsync(
        IReadOnlyList<OperationRequest> requests,
        int maxConcurrency,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<OperationResult>();
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = requests.Select(async request =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                results.Add(await ExecuteRequestAsync(request, onEvent, cancellationToken));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return BuildSummary(requests.Count, results);
    }

    private async Task<OperationResult> ExecuteRequestAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _executor.ExecuteAsync(request, onEvent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new OperationResult
            {
                Type = request.Type,
                Host = request.Host,
                Application = request.Application,
                TaskStatus = Models.TaskStatus.Failed,
                ErrorMessage = ex.Message
            };
            onEvent?.Invoke(OperationEvent.Failed(result));
            return result;
        }
    }

    private static BatchOperationSummary BuildSummary(int totalCount, IEnumerable<OperationResult> results)
    {
        var resultList = results.ToList();
        return new BatchOperationSummary
        {
            TotalCount = totalCount,
            SuccessCount = resultList.Count(item => item.Succeeded),
            FailedCount = resultList.Count(item => item.TaskStatus == Models.TaskStatus.Failed),
            CanceledCount = resultList.Count(item => item.Canceled)
        };
    }
}
