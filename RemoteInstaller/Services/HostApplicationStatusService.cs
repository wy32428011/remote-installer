using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

public sealed class BuiltInApplicationStatusRequest
{
    public required string Id { get; init; }
    public required Func<CancellationToken, Task<ApplicationStatus>> CheckStatusAsync { get; init; }
}

public sealed class CustomApplicationStatusRequest
{
    public required string Id { get; init; }
    public required Func<CancellationToken, Task<(bool IsInstalled, bool IsRunning, string StatusText)>> CheckStatusAsync { get; init; }
}

public sealed class HostApplicationStatusService
{
    private const int MaxConcurrentStatusChecks = 3;

    public async Task<HostStatusSnapshot> FetchSnapshotAsync(
        string hostId,
        IEnumerable<BuiltInApplicationStatusRequest> builtInApps,
        IEnumerable<CustomApplicationStatusRequest> customApps,
        CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(MaxConcurrentStatusChecks);
        var appResults = new ConcurrentDictionary<string, BuiltInAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
        var customResults = new ConcurrentDictionary<string, CustomAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);

        var appTasks = builtInApps.Select(request => FetchBuiltInStatusAsync(request, appResults, semaphore, cancellationToken));
        var customTasks = customApps.Select(request => FetchCustomStatusAsync(request, customResults, semaphore, cancellationToken));

        await Task.WhenAll(appTasks.Concat(customTasks));

        return new HostStatusSnapshot
        {
            HostId = hostId,
            CapturedAt = DateTime.UtcNow,
            Applications = appResults.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase),
            CustomApplications = customResults.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static async Task FetchBuiltInStatusAsync(
        BuiltInApplicationStatusRequest request,
        ConcurrentDictionary<string, BuiltInAppStatusSnapshot> results,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var status = await request.CheckStatusAsync(cancellationToken);
            results[request.Id] = new BuiltInAppStatusSnapshot
            {
                IsInstalled = status.IsInstalled,
                IsRunning = status.IsRunning,
                InstalledVersion = status.InstalledVersion ?? "未知"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            results[request.Id] = new BuiltInAppStatusSnapshot
            {
                IsInstalled = false,
                IsRunning = false,
                InstalledVersion = "未知"
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task FetchCustomStatusAsync(
        CustomApplicationStatusRequest request,
        ConcurrentDictionary<string, CustomAppStatusSnapshot> results,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var status = await request.CheckStatusAsync(cancellationToken);
            results[request.Id] = new CustomAppStatusSnapshot
            {
                IsInstalled = status.IsInstalled,
                IsRunning = status.IsRunning,
                StatusText = status.StatusText
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            results[request.Id] = new CustomAppStatusSnapshot
            {
                IsInstalled = false,
                IsRunning = false,
                StatusText = "检测失败"
            };
        }
        finally
        {
            semaphore.Release();
        }
    }
}
