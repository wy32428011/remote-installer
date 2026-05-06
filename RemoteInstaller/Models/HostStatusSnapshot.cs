using System;
using System.Collections.Generic;

namespace RemoteInstaller.Models;

public sealed class HostStatusSnapshot
{
    public string HostId { get; init; } = string.Empty;
    public DateTime CapturedAt { get; init; }
    public IReadOnlyDictionary<string, BuiltInAppStatusSnapshot> Applications { get; init; } =
        new Dictionary<string, BuiltInAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, CustomAppStatusSnapshot> CustomApplications { get; init; } =
        new Dictionary<string, CustomAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
}

public sealed class BuiltInAppStatusSnapshot
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string InstalledVersion { get; init; } = "未知";
}

public sealed class CustomAppStatusSnapshot
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string StatusText { get; init; } = "未部署";
}

public enum HostStatusRefreshReason
{
    HostSelection,
    ManualRefresh,
    BatchRefresh,
    PostInstall,
    PostUninstall,
    CustomAppChanged
}

public sealed class HostStatusRefreshResult
{
    public required HostStatusSnapshot Snapshot { get; init; }
    public bool UsedCache { get; init; }
    public bool IsBackgroundRefresh { get; init; }
}
