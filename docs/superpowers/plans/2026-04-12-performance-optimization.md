# 远程安装应用性能优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Remote Installer 更快进入可交互状态，并显著减少主机切换、刷新、批量检测时的重复状态检测与等待感。

**Architecture:** 本次实现不重写现有检测逻辑，而是在现有 `MainViewModel + InstallerService + SshService` 结构上新增一个很薄的主机状态刷新协调层，负责主机级去抖、缓存、去重和取消。`MainViewModel` 保留 UI 驱动职责，但不再从多个入口直接散落调用 `CheckAllAppsStatusAsync(...)`；同时把非首屏必需初始化从构造阶段移到窗口显示后异步执行。

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, SSH.NET, MaterialDesignInXaml

---

## File Map

- **Create:** `RemoteInstaller/Models/HostStatusSnapshot.cs`
  - 定义主机级缓存快照、内置应用状态项、自定义应用状态项、缓存元数据
- **Create:** `RemoteInstaller/Services/HostStatusRefreshCoordinator.cs`
  - 管理主机级去抖、缓存、进行中任务复用、强制刷新和缓存失效
- **Modify:** `RemoteInstaller/ViewModels/MainViewModel.cs:273-296, 421-428, 657-665, 804-823, 1108-1116, 1527-1560, 1894-2091, 2235-2644`
  - 收敛所有刷新入口，抽出抓取快照 / 应用快照 / 缓存失效 / 窗口加载后初始化逻辑
- **Modify:** `RemoteInstaller/MainWindow.xaml.cs:12-20`
  - 在窗口加载完成后触发 ViewModel 的异步后置初始化
- **Modify:** `RemoteInstaller/MainWindow.xaml:399-423`
  - 让刷新提示文案支持“直接刷新中”与“显示缓存并后台刷新中”的轻量区分
- **Modify:** `RemoteInstaller/Locator.cs:11-33`
  - 构造新的协调层并注入 `MainViewModel`

---

## Implementation Rules For This Plan

- 不新增自动化测试任务，遵循当前项目偏好：**只做构建 + 定向人工验证**。
- 不提交 git commit，除非用户在执行阶段明确要求。
- 不大改 UI 结构；所有可见变化仅限刷新提示文案与行为。
- 保留 `InstallerService.CheckStatusAsync(...)` 作为单应用检测实现，不在本轮重写为新协议。

---

### Task 1: 建立主机状态快照模型与刷新协调层骨架

**Files:**
- Create: `RemoteInstaller/Models/HostStatusSnapshot.cs`
- Create: `RemoteInstaller/Services/HostStatusRefreshCoordinator.cs`

- [ ] **Step 1: 创建主机状态快照模型文件**

写入 `RemoteInstaller/Models/HostStatusSnapshot.cs`，先把缓存边界固定下来：

```csharp
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
```

- [ ] **Step 2: 创建刷新协调层骨架**

写入 `RemoteInstaller/Services/HostStatusRefreshCoordinator.cs`，先搭好主机级缓存、去抖和进行中任务容器：

```csharp
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
```

- [ ] **Step 3: 构建确认新文件没有命名或 using 问题**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`

Expected: 构建通过；如果失败，应只修新建文件的命名空间、using 或类型引用问题，不扩散修改。

- [ ] **Step 4: 人工核对边界是否合理**

人工检查这两个文件，确认：
- 快照类型不依赖 WPF 或 ViewModel
- 协调层不依赖 `MainViewModel`
- 缓存以主机为粒度，而不是按单个应用散落管理

---

### Task 2: 把非首屏必需初始化移到窗口加载后执行

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs:1527-1560`
- Modify: `RemoteInstaller/MainWindow.xaml.cs:12-20`
- Modify: `RemoteInstaller/Locator.cs:11-33`

- [ ] **Step 1: 给 MainViewModel 增加新依赖和后置初始化入口**

调整 `MainViewModel` 构造函数签名，让协调层通过 `Locator` 注入：

```csharp
private readonly HostStatusRefreshCoordinator _hostStatusRefreshCoordinator;
private int _postLoadInitialized;

public MainViewModel(
    SshService sshService,
    DatabaseService databaseService,
    ILogger logger,
    ConfigurationService configurationService,
    HostStatusRefreshCoordinator hostStatusRefreshCoordinator,
    AppConfigurationService? appConfigurationService = null)
{
    _sshService = sshService;
    _databaseService = databaseService;
    _logger = logger;
    _configurationService = configurationService;
    _hostStatusRefreshCoordinator = hostStatusRefreshCoordinator;
    _appConfigurationService = appConfigurationService;

    // 保留首屏必要初始化
    LoadSettings();
    LoadHosts();
    InitializeSampleData();
    ApplyServerFilter();
    ApplyAppFilter();
    ApplyTaskFilter();
}
```

并新增窗口加载后入口：

```csharp
public async Task InitializeAfterWindowLoadedAsync()
{
    if (Interlocked.Exchange(ref _postLoadInitialized, 1) == 1)
    {
        return;
    }

    await Task.Yield();
    LoadCustomApps();
    AddLog("已完成首屏后的后台初始化", LogLevel.Info);
}
```

- [ ] **Step 2: 让 MainWindow 在 Loaded 后触发异步后置初始化**

修改 `RemoteInstaller/MainWindow.xaml.cs`，在构造函数里增加 `Loaded` 事件：

```csharp
public MainWindow()
{
    InitializeComponent();
    DataContext = Locator.Instance;
    Loaded += MainWindow_Loaded;
}

private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
{
    Loaded -= MainWindow_Loaded;

    if (DataContext is MainViewModel viewModel)
    {
        await viewModel.InitializeAfterWindowLoadedAsync();
    }
}
```

- [ ] **Step 3: 在 Locator 中构造协调层并更新 MainViewModel 创建方式**

修改 `RemoteInstaller/Locator.cs`：

```csharp
private static HostStatusRefreshCoordinator _hostStatusRefreshCoordinator = null!;

if (_instance == null)
{
    _logger = new LoggerService();
    _databaseService = new DatabaseService();
    _sshService = new SshService();
    _configurationService = new ConfigurationService(_sshService, _logger);
    _hostStatusRefreshCoordinator = new HostStatusRefreshCoordinator();
    _appConfigurationService = new AppConfigurationService();
    _instance = new MainViewModel(
        _sshService,
        _databaseService,
        _logger,
        _configurationService,
        _hostStatusRefreshCoordinator,
        _appConfigurationService);
}
```

- [ ] **Step 4: 构建确认启动链可编译**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`

Expected: 构建通过，`Locator -> MainViewModel -> MainWindow` 调用链无签名错误。

- [ ] **Step 5: 人工验证首屏路径**

手动运行应用，检查：
- 主窗口能正常打开
- 主机列表和应用列表能立即显示
- 自定义应用仍会在窗口打开后出现
- 启动过程没有一打开就触发远程 SSH 刷新

---

### Task 3: 抽出“抓取快照”与“应用快照”逻辑，替换散落的直接刷新调用

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs:273-296, 421-428, 657-665, 1108-1116, 1894-2091, 2452-2467, 2630-2634`

- [ ] **Step 1: 在 MainViewModel 中抽出抓取快照方法**

把当前 `CheckAllAppsStatusAsync(...)` 的“远程抓取”主体拆成一个返回快照的方法：

```csharp
private async Task<HostStatusSnapshot> FetchHostStatusSnapshotAsync(
    HostViewModel hostViewModel,
    CancellationToken cancellationToken = default)
{
    var host = GetHostFromViewModel(hostViewModel);
    if (host == null)
    {
        throw new InvalidOperationException("无法获取主机信息");
    }

    var installerService = new InstallerService(_sshService, _logger);
    await _sshService.ConnectAsync(host, cancellationToken);

    var appResults = new Dictionary<string, BuiltInAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
    var customResults = new Dictionary<string, CustomAppStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
    var semaphore = new SemaphoreSlim(3);

    var appTasks = Applications.Select(async appCard =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var appInfo = GetApplicationInfo(appCard.Id);
            if (appInfo == null)
            {
                return;
            }

            var status = await installerService.CheckStatusAsync(host, appInfo, cancellationToken);
            lock (appResults)
            {
                appResults[appCard.Id] = new BuiltInAppStatusSnapshot
                {
                    IsInstalled = status.IsInstalled,
                    IsRunning = status.IsRunning,
                    InstalledVersion = status.InstalledVersion ?? "未知"
                };
            }
        }
        finally
        {
            semaphore.Release();
        }
    });

    var customTasks = CustomApps.Select(async customApp =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var customStatus = await CheckSingleCustomAppStatusAsync(host, customApp, cancellationToken);
            lock (customResults)
            {
                customResults[customApp.Id] = new CustomAppStatusSnapshot
                {
                    IsInstalled = customStatus.IsInstalled,
                    IsRunning = customStatus.IsRunning,
                    StatusText = customStatus.StatusText
                };
            }
        }
        finally
        {
            semaphore.Release();
        }
    });

    await Task.WhenAll(appTasks.Concat(customTasks));

    return new HostStatusSnapshot
    {
        HostId = hostViewModel.Id,
        CapturedAt = DateTime.UtcNow,
        Applications = appResults,
        CustomApplications = customResults
    };
}
```

- [ ] **Step 2: 增加“应用快照到 UI”的方法，并只更新变化项**

在 `MainViewModel` 中新增：

```csharp
private void ApplyHostStatusSnapshot(HostViewModel hostViewModel, HostStatusSnapshot snapshot)
{
    hostViewModel.IsOnline = true;

    foreach (var appCard in Applications)
    {
        if (!snapshot.Applications.TryGetValue(appCard.Id, out var item))
        {
            continue;
        }

        if (appCard.IsInstalled != item.IsInstalled)
        {
            appCard.IsInstalled = item.IsInstalled;
        }

        if (appCard.IsRunning != item.IsRunning)
        {
            appCard.IsRunning = item.IsRunning;
        }

        if (!string.Equals(appCard.InstalledVersion, item.InstalledVersion, StringComparison.Ordinal))
        {
            appCard.InstalledVersion = item.InstalledVersion;
        }
    }

    foreach (var customApp in CustomApps)
    {
        if (!snapshot.CustomApplications.TryGetValue(customApp.Id, out var item))
        {
            continue;
        }

        if (customApp.IsInstalled != item.IsInstalled)
        {
            customApp.IsInstalled = item.IsInstalled;
        }

        if (customApp.IsRunning != item.IsRunning)
        {
            customApp.IsRunning = item.IsRunning;
        }

        if (!string.Equals(customApp.StatusText, item.StatusText, StringComparison.Ordinal))
        {
            customApp.StatusText = item.StatusText;
        }
    }
}
```

- [ ] **Step 3: 增加统一刷新入口，并替换 SelectedHost / Refresh / TestConnection / 部署后刷新等直接调用**

在 `MainViewModel` 中新增统一入口：

```csharp
private async Task RequestHostStatusRefreshAsync(
    HostViewModel hostViewModel,
    HostStatusRefreshReason reason,
    bool forceRefresh = false,
    CancellationToken cancellationToken = default)
{
    var hostId = hostViewModel.Id;

    if (reason == HostStatusRefreshReason.HostSelection)
    {
        await _hostStatusRefreshCoordinator.DelayForSelectionAsync(hostId, cancellationToken);
    }

    if (!forceRefresh && _hostStatusRefreshCoordinator.TryGetFreshSnapshot(hostId, out var cached) && cached != null)
    {
        ApplyHostStatusSnapshot(hostViewModel, cached);
        _refreshStatusText = "显示缓存结果，后台刷新中...";
    }
    else
    {
        _refreshStatusText = "正在刷新应用状态...";
    }

    IsRefreshingAppStatus = true;
    OnPropertyChanged(nameof(RefreshStatusText));

    try
    {
        var snapshot = await _hostStatusRefreshCoordinator.GetOrCreateInflightAsync(
            hostId,
            () => FetchHostStatusSnapshotAsync(hostViewModel, cancellationToken));

        ApplyHostStatusSnapshot(hostViewModel, snapshot);
        AddLog($"{hostViewModel.Name} 应用状态检测完成", LogLevel.Success);
    }
    catch (OperationCanceledException)
    {
        AddLog($"{hostViewModel.Name} 的状态刷新已取消", LogLevel.Warning);
    }
    catch (Exception ex)
    {
        hostViewModel.IsOnline = false;
        AddLog($"{hostViewModel.Name} 检测失败：{ex.Message}", LogLevel.Error);
    }
    finally
    {
        IsRefreshingAppStatus = false;
        _refreshStatusText = "正在刷新应用状态...";
        OnPropertyChanged(nameof(RefreshStatusText));
    }
}
```

然后把以下调用替换为统一入口：

```csharp
// OnPropertyChanged -> SelectedHost 变化
_ = RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.HostSelection);

// TestConnection
await RequestHostStatusRefreshAsync(targetHost, HostStatusRefreshReason.ManualRefresh, forceRefresh: true);

// Refresh
await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.ManualRefresh, forceRefresh: true);

// RefreshCustomAppsStatusAfterDialog
_ = RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.CustomAppChanged, forceRefresh: true);

// Uninstall/Install 成功后
await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.PostInstall, forceRefresh: true);
```

- [ ] **Step 4: 用新入口替代旧的 CheckAllAppsStatusAsync 暴露面**

保留旧方法名只作为兼容薄包装，避免一次性改太多调用点：

```csharp
private Task CheckAllAppsStatusAsync(HostViewModel hostViewModel, CancellationToken cancellationToken = default)
{
    return RequestHostStatusRefreshAsync(
        hostViewModel,
        HostStatusRefreshReason.ManualRefresh,
        forceRefresh: true,
        cancellationToken: cancellationToken);
}
```

这样能先收敛入口，再逐步删除旧命名。

- [ ] **Step 5: 构建确认 MainViewModel 主路径完成收敛**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`

Expected: 构建通过，且 `SelectedHost`、手动刷新、测试连接、部署后刷新都已改走统一入口。

- [ ] **Step 6: 人工验证交互行为**

手动验证：
- 快速切换多台主机时，不再立刻对每次切换都打 SSH
- 停留在某台主机后才开始刷新
- 点击“刷新应用”仍能强制刷新
- “测试连接”完成后仍会回填应用状态

---

### Task 4: 增加缓存失效与批量检测协同，避免重复堆积刷新任务

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs:804-823, 933-938, 1054-1056, 2235-2644`

- [ ] **Step 1: 给安装、卸载、自定义应用变更增加缓存失效**

在相关成功路径里加入缓存失效：

```csharp
private void InvalidateHostStatusCache(HostViewModel? hostViewModel)
{
    if (hostViewModel == null || string.IsNullOrWhiteSpace(hostViewModel.Id))
    {
        return;
    }

    _hostStatusRefreshCoordinator.Invalidate(hostViewModel.Id);
}
```

调用点至少覆盖：

```csharp
// InstallApplication 成功后
InvalidateHostStatusCache(SelectedHost);
await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.PostInstall, forceRefresh: true);

// UninstallApplication 成功后
InvalidateHostStatusCache(SelectedHost);
await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.PostUninstall, forceRefresh: true);

// 自定义应用部署对话框关闭后
InvalidateHostStatusCache(SelectedHost);
_ = RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.CustomAppChanged, forceRefresh: true);
```

- [ ] **Step 2: 让批量检测仍保留并发，但统一走协调层**

修改 `BatchCheckStatus()`：

```csharp
var tasks = SelectedHosts.Select(host =>
    RequestHostStatusRefreshAsync(
        host,
        HostStatusRefreshReason.BatchRefresh,
        forceRefresh: true,
        cancellationToken: token)).ToList();

await Task.WhenAll(tasks);
```

这样批量检测仍然并发，但同一台主机不会再因为其它入口叠加重复刷新。

- [ ] **Step 3: 让批量安装/卸载结束后的“当前选中主机刷新”统一走协调层**

替换这些代码：

```csharp
if (SelectedHost != null && SelectedHosts.Contains(SelectedHost))
{
    await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.PostInstall, forceRefresh: true);
}
```

以及：

```csharp
if (SelectedHost != null && SelectedHosts.Contains(SelectedHost))
{
    await RequestHostStatusRefreshAsync(SelectedHost, HostStatusRefreshReason.PostUninstall, forceRefresh: true);
}
```

- [ ] **Step 4: 构建确认批量路径没有遗漏旧入口**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`

Expected: 构建通过，批量检测 / 安装后刷新 / 卸载后刷新都能编译并指向统一入口。

- [ ] **Step 5: 人工验证缓存失效与批量协同**

手动验证：
- 安装成功后，当前主机不会继续显示旧缓存状态
- 卸载成功后，当前主机刷新结果正确
- 批量检测多台主机时，同一主机不会出现重复刷新堆积
- 快速连续点击刷新，不会明显叠加多个同主机检测

---

### Task 5: 为刷新提示增加轻量状态文案，并完成整体验证

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs:137-141, 228-233`
- Modify: `RemoteInstaller/MainWindow.xaml:399-423`

- [ ] **Step 1: 给 MainViewModel 增加刷新提示文案属性**

新增字段和只读属性：

```csharp
private string _refreshStatusText = "正在刷新应用状态...";

public string RefreshStatusText => _refreshStatusText;
```

并在不同场景下更新：

```csharp
_refreshStatusText = usedCache
    ? "显示缓存结果，后台刷新中..."
    : "正在刷新应用状态...";
OnPropertyChanged(nameof(RefreshStatusText));
```

- [ ] **Step 2: 把 MainWindow 里的固定提示文字替换成绑定**

把 `RemoteInstaller/MainWindow.xaml:404-414` 里的固定文案：

```xml
<TextBlock Text="正在刷新应用状态..."
           Foreground="{DynamicResource SecondaryTextBrush}"
           VerticalAlignment="Center"
           FontSize="11"/>
```

替换为：

```xml
<TextBlock Text="{Binding RefreshStatusText}"
           Foreground="{DynamicResource SecondaryTextBrush}"
           VerticalAlignment="Center"
           FontSize="11"/>
```

- [ ] **Step 3: 构建确认 UI 绑定无误**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`

Expected: 构建通过，XAML 绑定不报错。

- [ ] **Step 4: 做完整人工验证清单**

逐项验证：

1. **启动验证**
   - 打开应用后主界面更快进入可交互状态
   - 启动时不立刻进入远程刷新
   - 自定义应用稍后补齐，不阻塞首屏

2. **主机切换验证**
   - 快速切换主机时不会每次立即刷新
   - 稳定停留后才开始刷新
   - 切回刚看过的主机时可立即看到缓存结果

3. **手动刷新验证**
   - 点击“刷新应用”时强制刷新
   - 文案显示“正在刷新应用状态...”

4. **缓存命中验证**
   - 切回短时间内已看过的主机时，先显示缓存
   - 文案显示“显示缓存结果，后台刷新中...”

5. **变更后失效验证**
   - 安装、卸载、自定义应用变更后，旧缓存不会继续展示
   - 变更完成后会得到一轮新的真实状态

---

## Self-Review Checklist

- **Spec coverage:** 已覆盖首屏减负、统一检测入口、主机级去抖、主机级缓存、缓存失效、批量检测协同、轻量刷新提示文案。
- **Placeholder scan:** 无 TBD / TODO / “后续再说” 类占位；每个任务都绑定了明确文件与验证动作。
- **Type consistency:** 全程统一使用 `HostStatusSnapshot`、`BuiltInAppStatusSnapshot`、`CustomAppStatusSnapshot`、`HostStatusRefreshCoordinator`、`RequestHostStatusRefreshAsync`、`InvalidateHostStatusCache`、`RefreshStatusText` 这些名称。
