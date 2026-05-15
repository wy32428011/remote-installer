using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class BatchOperationRunnerTests
{
    [Fact]
    public async Task RunInstallQueueAsync_ExecutesRequestsSerially()
    {
        var order = new List<string>();
        var executor = new FakeBatchExecutor(async request =>
        {
            order.Add($"start:{request.Application.Id}:{request.Host.Id}");
            await Task.Delay(5);
            order.Add($"end:{request.Application.Id}:{request.Host.Id}");
            return Completed(request);
        });
        var runner = new BatchOperationRunner(executor);
        var requests = new[]
        {
            Request(OperationType.Install, "host-1", "redis"),
            Request(OperationType.Install, "host-2", "nginx")
        };

        var summary = await runner.RunInstallQueueAsync(requests, _ => { }, CancellationToken.None);

        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(new[]
        {
            "start:redis:host-1",
            "end:redis:host-1",
            "start:nginx:host-2",
            "end:nginx:host-2"
        }, order);
    }

    [Fact]
    public async Task RunUninstallQueueAsync_AllowsHostConcurrencyButSerializesAppsPerHost()
    {
        var syncRoot = new object();
        var runningPerHost = new Dictionary<string, int>();
        var maxPerHost = new Dictionary<string, int>();
        var executor = new FakeBatchExecutor(async request =>
        {
            lock (syncRoot)
            {
                runningPerHost[request.Host.Id] = runningPerHost.GetValueOrDefault(request.Host.Id) + 1;
                maxPerHost[request.Host.Id] = Math.Max(maxPerHost.GetValueOrDefault(request.Host.Id), runningPerHost[request.Host.Id]);
            }

            await Task.Delay(20);

            lock (syncRoot)
            {
                runningPerHost[request.Host.Id]--;
            }

            return Completed(request);
        });
        var runner = new BatchOperationRunner(executor);
        var requests = new[]
        {
            Request(OperationType.Uninstall, "host-1", "redis"),
            Request(OperationType.Uninstall, "host-1", "nginx"),
            Request(OperationType.Uninstall, "host-2", "redis"),
            Request(OperationType.Uninstall, "host-2", "nginx")
        };

        var summary = await runner.RunUninstallQueueAsync(requests, maxConcurrentHosts: 2, _ => { }, CancellationToken.None);

        Assert.Equal(4, summary.SuccessCount);
        Assert.All(maxPerHost.Values, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task RunStatusQueueAsync_ContinuesAfterFailure()
    {
        var executor = new FakeBatchExecutor(request =>
        {
            return Task.FromResult(request.Application.Id == "bad"
                ? Failed(request, "检测失败")
                : Completed(request));
        });
        var runner = new BatchOperationRunner(executor);
        var requests = new[]
        {
            Request(OperationType.CheckStatus, "host-1", "ok-1"),
            Request(OperationType.CheckStatus, "host-1", "bad"),
            Request(OperationType.CheckStatus, "host-1", "ok-2")
        };

        var summary = await runner.RunStatusQueueAsync(requests, maxConcurrency: 2, _ => { }, CancellationToken.None);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(1, summary.FailedCount);
    }

    [Fact]
    public async Task RunStatusQueueAsync_ConvertsUnexpectedItemExceptionToFailure()
    {
        var executor = new FakeBatchExecutor(request =>
        {
            if (request.Application.Id == "bad")
            {
                throw new InvalidOperationException("检测异常");
            }

            return Task.FromResult(Completed(request));
        });
        var runner = new BatchOperationRunner(executor);
        var requests = new[]
        {
            Request(OperationType.CheckStatus, "host-1", "ok-1"),
            Request(OperationType.CheckStatus, "host-1", "bad"),
            Request(OperationType.CheckStatus, "host-1", "ok-2")
        };

        var events = new List<OperationEvent>();
        var summary = await runner.RunStatusQueueAsync(requests, maxConcurrency: 2, events.Add, CancellationToken.None);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Contains(events, item => item.Kind == OperationEventKind.Failed && item.Result?.ErrorMessage == "检测异常");
    }

    private static OperationRequest Request(OperationType type, string hostId, string appId)
    {
        return new OperationRequest(
            type,
            new RemoteHost { Id = hostId, Name = hostId, IpAddress = "127.0.0.1" },
            new ApplicationInfo { Id = appId, Name = appId, Version = "1.0" },
            isBatch: true);
    }

    private static OperationResult Completed(OperationRequest request)
    {
        return new OperationResult
        {
            Type = request.Type,
            Host = request.Host,
            Application = request.Application,
            TaskId = Guid.NewGuid().ToString("N"),
            TaskStatus = Models.TaskStatus.Completed
        };
    }

    private static OperationResult Failed(OperationRequest request, string error)
    {
        return new OperationResult
        {
            Type = request.Type,
            Host = request.Host,
            Application = request.Application,
            TaskId = Guid.NewGuid().ToString("N"),
            TaskStatus = Models.TaskStatus.Failed,
            ErrorMessage = error
        };
    }

    private sealed class FakeBatchExecutor : IBatchOperationExecutor
    {
        private readonly Func<OperationRequest, Task<OperationResult>> _execute;

        public FakeBatchExecutor(Func<OperationRequest, Task<OperationResult>> execute)
        {
            _execute = execute;
        }

        public Task<OperationResult> ExecuteAsync(OperationRequest request, Action<OperationEvent>? onEvent, CancellationToken cancellationToken)
        {
            return _execute(request);
        }
    }
}
