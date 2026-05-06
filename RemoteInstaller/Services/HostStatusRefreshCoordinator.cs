using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

public sealed class HostStatusRefreshCoordinator
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(350);

    private readonly ConcurrentDictionary<string, HostStatusSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<HostStatusSnapshot>> _inflightRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _selectionDebounceTokens = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetFreshSnapshot(string hostId, out HostStatusSnapshot? snapshot)
    {
        snapshot = null;
        if (!_snapshots.TryGetValue(hostId, out var cached))
        {
            return false;
        }

        if (DateTime.UtcNow - cached.CapturedAt > CacheLifetime)
        {
            return false;
        }

        snapshot = cached;
        return true;
    }

    public void StoreSnapshot(HostStatusSnapshot snapshot)
    {
        _snapshots[snapshot.HostId] = snapshot;
    }

    public void Invalidate(string hostId)
    {
        _snapshots.TryRemove(hostId, out _);
    }

    public void CancelPendingSelection(string hostId)
    {
        if (_selectionDebounceTokens.TryRemove(hostId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public async Task DelayForSelectionAsync(string hostId, CancellationToken cancellationToken)
    {
        CancelPendingSelection(hostId);
        var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _selectionDebounceTokens[hostId] = debounceCts;

        try
        {
            await Task.Delay(SelectionDebounce, debounceCts.Token);
        }
        finally
        {
            if (_selectionDebounceTokens.TryGetValue(hostId, out var current) && ReferenceEquals(current, debounceCts))
            {
                _selectionDebounceTokens.TryRemove(hostId, out _);
            }

            debounceCts.Dispose();
        }
    }

    public Task<HostStatusSnapshot> GetOrCreateInflightAsync(string hostId, Func<Task<HostStatusSnapshot>> factory)
    {
        return _inflightRefreshes.GetOrAdd(hostId, _ => RunAndReleaseAsync(hostId, factory));
    }

    private async Task<HostStatusSnapshot> RunAndReleaseAsync(string hostId, Func<Task<HostStatusSnapshot>> factory)
    {
        try
        {
            var snapshot = await factory();
            StoreSnapshot(snapshot);
            return snapshot;
        }
        finally
        {
            _inflightRefreshes.TryRemove(hostId, out _);
        }
    }
}
