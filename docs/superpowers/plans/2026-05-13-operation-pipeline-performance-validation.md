# Operation Pipeline Performance Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将安装、检测、卸载与批量操作收敛到统一 Operations 管线，并通过自动测试、UI 防卡顿检查和 CentOS 7 / Ubuntu 24 实机验证确认行为可用。

**Architecture:** 在现有 `RemoteInstaller.Services.Operations` 基础上新增操作请求、结果、事件、执行器和批量 runner。第一阶段让 `OperationExecutor` 复用现有 `InstallerService`，先统一 UI 调用和批量队列；后续再逐步迁移任务保存、日志收集、状态验证和远程工作目录管理。

**Tech Stack:** C# / .NET 10 WPF、CommunityToolkit.Mvvm、xUnit、SSH.NET、SQLite、现有 Bash/PowerShell 脚本协议。

---

## File Structure

本计划扩展已有 `RemoteInstaller/Services/Operations/`，不重复创建已经存在的基础类型。

- Create: `RemoteInstaller/Services/Operations/OperationType.cs`
  - 定义统一操作类型：安装、检测、卸载。
- Create: `RemoteInstaller/Services/Operations/OperationRequest.cs`
  - 承载单次操作所需主机、应用、参数、本地包路径、保留数据和批量标记。
- Create: `RemoteInstaller/Services/Operations/OperationResult.cs`
  - 承载操作最终结果、任务 ID、状态快照、错误和 warning 标记。
- Create: `RemoteInstaller/Services/Operations/OperationEvent.cs`
  - 定义后台操作发给 UI 的进度、日志、状态、完成、失败、取消事件。
- Create: `RemoteInstaller/Services/Operations/OperationExecutor.cs`
  - 统一执行安装、检测、卸载，第一阶段适配现有 `InstallerService`。
- Create: `RemoteInstaller/Services/Operations/BatchOperationRunner.cs`
  - 统一批量安装、批量检测、批量卸载队列策略。
- Create: `RemoteInstaller/Services/Operations/BatchOperationSummary.cs`
  - 承载批量总数、成功数、失败数、取消数。
- Create: `RemoteInstaller/Services/Operations/UiOperationEventBuffer.cs`
  - 统一缓冲操作事件，减少 UI 高频刷新。
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs`
  - 将单机安装、检测、卸载与批量操作迁移到统一管线。
  - 移除远程操作回调中的同步 `Dispatcher.Invoke`。
- Modify: `RemoteInstaller/Services/InstallerService.cs`
  - 暴露当前阶段可复用的兼容执行入口，不在本轮大规模搬迁内部脚本逻辑。
  - 降低进度回调中的任务保存频率。
- Modify: `RemoteInstaller/README.md`
  - 记录本次性能优化策略与实机验证结果。
- Test: `RemoteInstaller.Tests/OperationRequestResultTests.cs`
- Test: `RemoteInstaller.Tests/OperationExecutorTests.cs`
- Test: `RemoteInstaller.Tests/BatchOperationRunnerTests.cs`
- Test: `RemoteInstaller.Tests/UiOperationEventBufferTests.cs`
- Test: `RemoteInstaller.Tests/MainViewModelOperationPipelineTests.cs`
- Test: update existing `RemoteInstaller.Tests/MainViewModelTaskUiThrottlingTests.cs`
- Test: update existing `RemoteInstaller.Tests/InstallProgressViewModelLogThrottlingTests.cs`

> Git 提交说明：下面每个任务包含 commit 步骤以满足计划粒度；实际执行时只有在用户明确要求创建提交时才运行 commit 命令，否则只完成代码、测试和文档修改。

## Task 1: Operation Request and Result Models

**Files:**
- Create: `RemoteInstaller/Services/Operations/OperationType.cs`
- Create: `RemoteInstaller/Services/Operations/OperationRequest.cs`
- Create: `RemoteInstaller/Services/Operations/OperationResult.cs`
- Create: `RemoteInstaller/Services/Operations/OperationEvent.cs`
- Test: `RemoteInstaller.Tests/OperationRequestResultTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `RemoteInstaller.Tests/OperationRequestResultTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationRequestResultTests
{
    [Fact]
    public void OperationRequest_CopiesParametersCaseInsensitively()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "redis", Name = "Redis", Version = "7.2.3" };
        var request = new OperationRequest(
            OperationType.Install,
            host,
            app,
            new Dictionary<string, string> { ["Port"] = "6379" },
            localPackagePath: "C:/packages/redis.tar.gz",
            keepData: false,
            isBatch: true);

        Assert.Equal(OperationType.Install, request.Type);
        Assert.Equal(host, request.Host);
        Assert.Equal(app, request.Application);
        Assert.True(request.Parameters.ContainsKey("port"));
        Assert.Equal("6379", request.Parameters["PORT"]);
        Assert.Equal("C:/packages/redis.tar.gz", request.LocalPackagePath);
        Assert.True(request.IsBatch);
    }

    [Fact]
    public void OperationResult_FromTaskPreservesTaskStatusAndError()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "nginx", Name = "Nginx", Version = "1.25.3" };
        var task = new InstallTask
        {
            Id = "task-1",
            HostId = host.Id,
            HostName = host.Name,
            AppId = app.Id,
            AppName = app.Name,
            AppVersion = app.Version
        };
        task.Fail("端口被占用");

        var result = OperationResult.FromTask(OperationType.Install, host, app, task, status: null, hasWarning: false);

        Assert.Equal(OperationType.Install, result.Type);
        Assert.Equal("task-1", result.TaskId);
        Assert.Equal(Models.TaskStatus.Failed, result.TaskStatus);
        Assert.Equal("端口被占用", result.ErrorMessage);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void OperationEvent_CreatesLogAndProgressEvents()
    {
        var progress = OperationEvent.Progress("task-1", "执行安装", 45);
        var log = OperationEvent.Log("task-1", new LogEntry
        {
            Message = "正在执行脚本",
            Level = LogLevel.Info,
            Timestamp = DateTime.Now
        });

        Assert.Equal(OperationEventKind.Progress, progress.Kind);
        Assert.Equal("执行安装", progress.Stage);
        Assert.Equal(45, progress.Percent);
        Assert.Equal(OperationEventKind.Log, log.Kind);
        Assert.Equal("正在执行脚本", log.LogEntry!.Message);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationRequestResultTests --no-restore
```

Expected: compile failure because `OperationRequest`、`OperationResult`、`OperationEvent` do not exist.

- [ ] **Step 3: Add the model implementations**

Create `RemoteInstaller/Services/Operations/OperationType.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public enum OperationType
{
    Install,
    CheckStatus,
    Uninstall
}
```

Create `RemoteInstaller/Services/Operations/OperationRequest.cs`:

```csharp
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class OperationRequest
{
    public OperationRequest(
        OperationType type,
        RemoteHost host,
        ApplicationInfo application,
        IDictionary<string, string>? parameters = null,
        string? localPackagePath = null,
        bool keepData = false,
        bool isBatch = false)
    {
        Type = type;
        Host = host;
        Application = application;
        Parameters = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        LocalPackagePath = localPackagePath ?? string.Empty;
        KeepData = keepData;
        IsBatch = isBatch;
    }

    public OperationType Type { get; }
    public RemoteHost Host { get; }
    public ApplicationInfo Application { get; }
    public Dictionary<string, string> Parameters { get; }
    public string LocalPackagePath { get; }
    public bool KeepData { get; }
    public bool IsBatch { get; }
}
```

Create `RemoteInstaller/Services/Operations/OperationResult.cs`:

```csharp
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
```

Create `RemoteInstaller/Services/Operations/OperationEvent.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationRequestResultTests --no-restore
```

Expected: `Passed!` with all `OperationRequestResultTests` passing.

- [ ] **Step 5: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/Services/Operations/OperationType.cs RemoteInstaller/Services/Operations/OperationRequest.cs RemoteInstaller/Services/Operations/OperationResult.cs RemoteInstaller/Services/Operations/OperationEvent.cs RemoteInstaller.Tests/OperationRequestResultTests.cs
git commit -m @'
feat: add operation request result models
'@
```

## Task 2: Operation Executor Adapter

**Files:**
- Create: `RemoteInstaller/Services/Operations/OperationExecutor.cs`
- Test: `RemoteInstaller.Tests/OperationExecutorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `RemoteInstaller.Tests/OperationExecutorTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_InstallPublishesProgressLogAndCompletedEvents()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "nginx", Name = "Nginx", Version = "1.25.3" };
        var installer = new FakeOperationInstaller
        {
            InstallTaskFactory = () => CompletedTask(host, app, "task-install")
        };
        var events = new List<OperationEvent>();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(OperationType.Install, host, app, new Dictionary<string, string> { ["HTTP_PORT"] = "80" });

        var result = await executor.ExecuteAsync(request, events.Add, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("task-install", result.TaskId);
        Assert.Contains(events, item => item.Kind == OperationEventKind.Progress && item.Stage == "执行安装");
        Assert.Contains(events, item => item.Kind == OperationEventKind.Log && item.LogEntry!.Message == "安装完成");
        Assert.Contains(events, item => item.Kind == OperationEventKind.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_CheckStatusReturnsCompletedResultWithStatusEvent()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "redis", Name = "Redis", Version = "7.2.3" };
        var status = new ApplicationStatus { IsInstalled = true, IsRunning = true, InstalledVersion = "7.2.3" };
        var installer = new FakeOperationInstaller { Status = status };
        var events = new List<OperationEvent>();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(OperationType.CheckStatus, host, app);

        var result = await executor.ExecuteAsync(request, events.Add, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Same(status, result.Status);
        Assert.Contains(events, item => item.Kind == OperationEventKind.StatusChanged && item.Status == status);
        Assert.Contains(events, item => item.Kind == OperationEventKind.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_UninstallPublishesFailedEventWhenTaskFails()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "rabbitmq", Name = "RabbitMQ", Version = "3.12.10" };
        var installer = new FakeOperationInstaller
        {
            UninstallTaskFactory = () => FailedTask(host, app, "task-uninstall", "仍有运行证据")
        };
        var events = new List<OperationEvent>();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(OperationType.Uninstall, host, app, keepData: false);

        var result = await executor.ExecuteAsync(request, events.Add, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Models.TaskStatus.Failed, result.TaskStatus);
        Assert.Equal("仍有运行证据", result.ErrorMessage);
        Assert.Contains(events, item => item.Kind == OperationEventKind.Failed);
    }

    private static InstallTask CompletedTask(RemoteHost host, ApplicationInfo app, string taskId)
    {
        var task = NewTask(host, app, taskId);
        task.Start();
        task.Complete();
        return task;
    }

    private static InstallTask FailedTask(RemoteHost host, ApplicationInfo app, string taskId, string error)
    {
        var task = NewTask(host, app, taskId);
        task.Start();
        task.Fail(error);
        return task;
    }

    private static InstallTask NewTask(RemoteHost host, ApplicationInfo app, string taskId)
    {
        return new InstallTask
        {
            Id = taskId,
            HostId = host.Id,
            HostName = host.Name,
            AppId = app.Id,
            AppName = app.Name,
            AppVersion = app.Version
        };
    }

    private sealed class FakeOperationInstaller : IOperationInstaller
    {
        public Func<InstallTask>? InstallTaskFactory { get; init; }
        public Func<InstallTask>? UninstallTaskFactory { get; init; }
        public ApplicationStatus Status { get; init; } = new();

        public Task<InstallTask> InstallAsync(
            RemoteHost host,
            ApplicationInfo app,
            Dictionary<string, string> parameters,
            string? localPackagePath,
            IProgress<InstallTask>? progressReporter,
            CancellationToken cancellationToken,
            IProgress<LogEntry>? logReporter)
        {
            var task = InstallTaskFactory?.Invoke() ?? CompletedTask(host, app, "task-install");
            task.UpdateProgress(InstallStage.Installing, 60);
            progressReporter?.Report(task);
            logReporter?.Report(new LogEntry { Message = "安装完成", Level = LogLevel.Success, Timestamp = DateTime.Now });
            return Task.FromResult(task);
        }

        public Task<ApplicationStatus> CheckStatusAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken)
        {
            return Task.FromResult(Status);
        }

        public Task<InstallTask> UninstallAsync(
            RemoteHost host,
            ApplicationInfo app,
            bool keepData,
            IProgress<InstallTask>? progressReporter,
            CancellationToken cancellationToken,
            IProgress<LogEntry>? logReporter)
        {
            var task = UninstallTaskFactory?.Invoke() ?? CompletedTask(host, app, "task-uninstall");
            progressReporter?.Report(task);
            return Task.FromResult(task);
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationExecutorTests --no-restore
```

Expected: compile failure because `OperationExecutor` and `IOperationInstaller` do not exist.

- [ ] **Step 3: Add the executor implementation**

Create `RemoteInstaller/Services/Operations/OperationExecutor.cs`:

```csharp
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

    Task<ApplicationStatus> CheckStatusAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken);

    Task<InstallTask> UninstallAsync(
        RemoteHost host,
        ApplicationInfo app,
        bool keepData,
        IProgress<InstallTask>? progressReporter,
        CancellationToken cancellationToken,
        IProgress<LogEntry>? logReporter);
}

public sealed class OperationExecutor : IDisposable
{
    private readonly IOperationInstaller _installer;

    public OperationExecutor(IOperationInstaller installer)
    {
        _installer = installer;
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
        var progress = CreateProgressReporter(onEvent);
        var logs = CreateLogReporter(onEvent);
        var task = await _installer.InstallAsync(
            request.Host,
            request.Application,
            request.Parameters,
            request.LocalPackagePath,
            progress,
            cancellationToken,
            logs);
        var result = OperationResult.FromTask(OperationType.Install, request.Host, request.Application, task, null, hasWarning: false);
        PublishTerminalEvent(result, onEvent);
        return result;
    }

    private async Task<OperationResult> ExecuteCheckStatusAsync(
        OperationRequest request,
        Action<OperationEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var status = await _installer.CheckStatusAsync(request.Host, request.Application, cancellationToken);
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
        var progress = CreateProgressReporter(onEvent);
        var logs = CreateLogReporter(onEvent);
        var task = await _installer.UninstallAsync(
            request.Host,
            request.Application,
            request.KeepData,
            progress,
            cancellationToken,
            logs);
        var result = OperationResult.FromTask(OperationType.Uninstall, request.Host, request.Application, task, null, hasWarning: false);
        PublishTerminalEvent(result, onEvent);
        return result;
    }

    private static IProgress<InstallTask> CreateProgressReporter(Action<OperationEvent>? onEvent)
    {
        return new Progress<InstallTask>(task =>
        {
            onEvent?.Invoke(OperationEvent.Progress(task.Id, task.StageDisplayText, task.Progress));
        });
    }

    private static IProgress<LogEntry> CreateLogReporter(Action<OperationEvent>? onEvent)
    {
        return new Progress<LogEntry>(entry =>
        {
            onEvent?.Invoke(OperationEvent.Log(string.Empty, entry));
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
        if (_installer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
```

Modify `RemoteInstaller/Services/InstallerService.cs` class declaration to implement `IOperationInstaller`:

```csharp
public class InstallerService : IOperationInstaller, IDisposable
```

`InstallerService` already has compatible `InstallAsync`、`CheckStatusAsync`、`UninstallAsync` signatures, so no method body changes are required for this step.

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationExecutorTests --no-restore
```

Expected: `Passed!` with all `OperationExecutorTests` passing.

- [ ] **Step 5: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/Services/Operations/OperationExecutor.cs RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/OperationExecutorTests.cs
git commit -m @'
feat: add operation executor adapter
'@
```

## Task 3: UI Operation Event Buffer

**Files:**
- Create: `RemoteInstaller/Services/Operations/UiOperationEventBuffer.cs`
- Test: `RemoteInstaller.Tests/UiOperationEventBufferTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `RemoteInstaller.Tests/UiOperationEventBufferTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class UiOperationEventBufferTests
{
    [Fact]
    public void Drain_MergesProgressEventsByTaskAndKeepsLatestProgress()
    {
        var buffer = new UiOperationEventBuffer();
        buffer.Enqueue(OperationEvent.Progress("task-1", "上传资源", 20));
        buffer.Enqueue(OperationEvent.Progress("task-1", "执行安装", 70));
        buffer.Enqueue(OperationEvent.Progress("task-2", "连接主机", 10));

        var batch = buffer.Drain();

        Assert.Equal(2, batch.ProgressEvents.Count);
        Assert.Contains(batch.ProgressEvents, item => item.TaskId == "task-1" && item.Stage == "执行安装" && item.Percent == 70);
        Assert.Contains(batch.ProgressEvents, item => item.TaskId == "task-2" && item.Percent == 10);
    }

    [Fact]
    public void Drain_PreservesLogOrder()
    {
        var buffer = new UiOperationEventBuffer();
        buffer.Enqueue(OperationEvent.Log("task-1", new LogEntry { Message = "第一行", Level = LogLevel.Info, Timestamp = DateTime.Now }));
        buffer.Enqueue(OperationEvent.Log("task-1", new LogEntry { Message = "第二行", Level = LogLevel.Info, Timestamp = DateTime.Now }));

        var batch = buffer.Drain();

        Assert.Equal(new[] { "第一行", "第二行" }, batch.LogEvents.Select(item => item.LogEntry!.Message));
    }

    [Fact]
    public void Drain_PrioritizesTerminalEvents()
    {
        var buffer = new UiOperationEventBuffer();
        var host = new RemoteHost { Id = "host-1", Name = "测试主机" };
        var app = new ApplicationInfo { Id = "nginx", Name = "Nginx" };
        var result = new OperationResult
        {
            Type = OperationType.Install,
            Host = host,
            Application = app,
            TaskId = "task-1",
            TaskStatus = Models.TaskStatus.Completed
        };

        buffer.Enqueue(OperationEvent.Completed(result));

        var batch = buffer.Drain();

        Assert.Single(batch.TerminalEvents);
        Assert.Equal(OperationEventKind.Completed, batch.TerminalEvents[0].Kind);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter UiOperationEventBufferTests --no-restore
```

Expected: compile failure because `UiOperationEventBuffer` does not exist.

- [ ] **Step 3: Add the buffer implementation**

Create `RemoteInstaller/Services/Operations/UiOperationEventBuffer.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter UiOperationEventBufferTests --no-restore
```

Expected: `Passed!` with all `UiOperationEventBufferTests` passing.

- [ ] **Step 5: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/Services/Operations/UiOperationEventBuffer.cs RemoteInstaller.Tests/UiOperationEventBufferTests.cs
git commit -m @'
feat: add operation event buffer
'@
```

## Task 4: Batch Operation Runner

**Files:**
- Create: `RemoteInstaller/Services/Operations/BatchOperationSummary.cs`
- Create: `RemoteInstaller/Services/Operations/BatchOperationRunner.cs`
- Test: `RemoteInstaller.Tests/BatchOperationRunnerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `RemoteInstaller.Tests/BatchOperationRunnerTests.cs`:

```csharp
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
        var runningPerHost = new Dictionary<string, int>();
        var maxPerHost = new Dictionary<string, int>();
        var executor = new FakeBatchExecutor(async request =>
        {
            runningPerHost[request.Host.Id] = runningPerHost.GetValueOrDefault(request.Host.Id) + 1;
            maxPerHost[request.Host.Id] = Math.Max(maxPerHost.GetValueOrDefault(request.Host.Id), runningPerHost[request.Host.Id]);
            await Task.Delay(20);
            runningPerHost[request.Host.Id]--;
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
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter BatchOperationRunnerTests --no-restore
```

Expected: compile failure because `BatchOperationRunner`、`BatchOperationSummary`、`IBatchOperationExecutor` do not exist.

- [ ] **Step 3: Add the batch runner implementation**

Create `RemoteInstaller/Services/Operations/BatchOperationSummary.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public sealed class BatchOperationSummary
{
    public int TotalCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int CanceledCount { get; init; }
}
```

Create `RemoteInstaller/Services/Operations/BatchOperationRunner.cs`:

```csharp
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
            results.Add(await _executor.ExecuteAsync(request, onEvent, cancellationToken));
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
                    results.Add(await _executor.ExecuteAsync(request, onEvent, cancellationToken));
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
                results.Add(await _executor.ExecuteAsync(request, onEvent, cancellationToken));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return BuildSummary(requests.Count, results);
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
```

Modify `RemoteInstaller/Services/Operations/OperationExecutor.cs` class declaration:

```csharp
public sealed class OperationExecutor : IBatchOperationExecutor, IDisposable
```

- [ ] **Step 4: Run the tests and verify they pass**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter BatchOperationRunnerTests --no-restore
```

Expected: `Passed!` with all `BatchOperationRunnerTests` passing.

- [ ] **Step 5: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/Services/Operations/BatchOperationSummary.cs RemoteInstaller/Services/Operations/BatchOperationRunner.cs RemoteInstaller/Services/Operations/OperationExecutor.cs RemoteInstaller.Tests/BatchOperationRunnerTests.cs
git commit -m @'
feat: add batch operation runner
'@
```

## Task 5: MainViewModel Operation Pipeline Integration

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs`
- Test: `RemoteInstaller.Tests/MainViewModelOperationPipelineTests.cs`
- Test: update `RemoteInstaller.Tests/MainViewModelTaskUiThrottlingTests.cs`

- [ ] **Step 1: Write structural tests for integration boundaries**

Create `RemoteInstaller.Tests/MainViewModelOperationPipelineTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class MainViewModelOperationPipelineTests
{
    [Fact]
    public void MainViewModel_CreatesOperationExecutorForSingleOperations()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("CreateOperationExecutor", source);
        Assert.Contains("new OperationRequest(OperationType.Install", source);
        Assert.Contains("new OperationRequest(OperationType.CheckStatus", source);
        Assert.Contains("new OperationRequest(OperationType.Uninstall", source);
    }

    [Fact]
    public void MainViewModel_UsesBatchOperationRunnerForBatchOperations()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

        Assert.Contains("new BatchOperationRunner", source);
        Assert.Contains("RunInstallQueueAsync", source);
        Assert.Contains("RunStatusQueueAsync", source);
        Assert.Contains("RunUninstallQueueAsync", source);
    }

    [Fact]
    public void RemoteOperationCallbacks_DoNotUseSynchronousDispatcherInvoke()
    {
        var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");
        var operationRegionStart = source.IndexOf("private async Task ExecuteInstallAsync", StringComparison.Ordinal);
        Assert.True(operationRegionStart >= 0, "Expected ExecuteInstallAsync to exist.");
        var operationRegion = source[operationRegionStart..];

        Assert.DoesNotContain("Dispatcher.Invoke(()", operationRegion);
        Assert.Contains("Dispatcher.InvokeAsync", source);
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    private static string GetProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RemoteInstaller.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 RemoteInstaller.sln，无法定位项目根目录。");
    }
}
```

Update `RemoteInstaller.Tests/MainViewModelTaskUiThrottlingTests.cs` by adding this test:

```csharp
[Fact]
public void OperationEventBuffer_IsUsedBeforeRefreshingTaskCollections()
{
    var source = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

    Assert.Contains("UiOperationEventBuffer", source);
    Assert.Contains("Drain", source);
    Assert.Contains("ApplyOperationEventBatch", source);
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "MainViewModelOperationPipelineTests|MainViewModelTaskUiThrottlingTests" --no-restore
```

Expected: tests fail because `MainViewModel` still calls operations directly and has not added operation buffer integration.

- [ ] **Step 3: Add operation executor factory and event buffer fields**

Modify `RemoteInstaller/ViewModels/MainViewModel.cs` using existing `using RemoteInstaller.Services.Operations;` already present through `InstallerService` dependencies. If missing, add:

```csharp
using RemoteInstaller.Services.Operations;
```

Add fields near existing task UI refresh fields:

```csharp
private readonly UiOperationEventBuffer _operationEventBuffer = new();
private readonly System.Timers.Timer _operationEventFlushTimer;
private const int OperationEventFlushThrottleMs = 120;
```

In the constructor, after `_taskUiRefreshTimer` initialization, add:

```csharp
_operationEventFlushTimer = new System.Timers.Timer(OperationEventFlushThrottleMs);
_operationEventFlushTimer.AutoReset = false;
_operationEventFlushTimer.Elapsed += (_, _) =>
{
    _operationEventFlushTimer.Stop();
    RunOnUiThread(FlushOperationEvents);
};
```

Add helper methods in `MainViewModel` near task refresh helpers:

```csharp
private OperationExecutor CreateOperationExecutor()
{
    return new OperationExecutor(CreateIsolatedInstallerService());
}

private void EnqueueOperationEvent(OperationEvent operationEvent)
{
    _operationEventBuffer.Enqueue(operationEvent);
    lock (_taskUiRefreshLock)
    {
        if (!_operationEventFlushTimer.Enabled)
        {
            _operationEventFlushTimer.Start();
        }
    }
}

private void FlushOperationEvents()
{
    var batch = _operationEventBuffer.Drain();
    if (!batch.HasChanges)
    {
        return;
    }

    ApplyOperationEventBatch(batch);
}

private void ApplyOperationEventBatch(UiOperationEventBatch batch)
{
    foreach (var terminalEvent in batch.TerminalEvents)
    {
        if (terminalEvent.Result?.Status != null)
        {
            var appCard = Applications.FirstOrDefault(app =>
                string.Equals(app.Id, terminalEvent.Result.Application.Id, StringComparison.OrdinalIgnoreCase));
            if (appCard != null)
            {
                ApplyApplicationStatus(appCard, terminalEvent.Result.Status);
            }
        }
    }

    RequestTaskListRefresh(immediate: batch.TerminalEvents.Count > 0);
}
```

- [ ] **Step 4: Migrate single install to OperationExecutor**

In `ExecuteInstallAsync`, replace direct `installerService.InstallAsync(...)` with:

```csharp
using var operationExecutor = CreateOperationExecutor();
var request = new OperationRequest(
    OperationType.Install,
    host,
    appInfo,
    parameters,
    localPackagePath,
    keepData: false,
    isBatch: false);

var operationResult = await operationExecutor.ExecuteAsync(request, EnqueueOperationEvent, cts.Token);

taskViewModel.TaskId = operationResult.TaskId;
taskViewModel.IsCompleted = operationResult.TaskStatus == Models.TaskStatus.Completed;
taskViewModel.IsFailed = operationResult.TaskStatus == Models.TaskStatus.Failed;
taskViewModel.IsCanceled = operationResult.TaskStatus == Models.TaskStatus.Cancelled;
if (!string.IsNullOrWhiteSpace(operationResult.ErrorMessage))
{
    taskViewModel.StatusMessage = operationResult.ErrorMessage;
}
```

Keep the existing success/failure UI logic, but read from `operationResult` instead of `taskResult`:

```csharp
if (operationResult.TaskStatus == Models.TaskStatus.Completed)
{
    UpdateInstalledApplicationState(appInfo.Id, version, appCard);
    AddLog($"✅ 安装成功：{appInfo.Name} v{version}", LogLevel.Success);
    var refreshedAppCard = appCard ?? Applications.FirstOrDefault(card =>
        string.Equals(card.Id, appInfo.Id, StringComparison.OrdinalIgnoreCase) ||
        (IsJdkVersionApplicationId(appInfo.Id) && IsUnifiedJdkApplicationId(card.Id)));
    if (refreshedAppCard != null)
    {
        await RefreshApplicationStatusAfterMutationAsync(selectedHostViewModel, refreshedAppCard, cts.Token);
    }
    MessageBox.Show($"安装成功！{appInfo.Name} 已安装到 {selectedHostViewModel.Name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
}
else if (operationResult.TaskStatus == Models.TaskStatus.Failed)
{
    AddLog($"❌ 安装失败：{operationResult.ErrorMessage}", LogLevel.Error);
    MessageBox.Show($"安装失败：{operationResult.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
}
```

- [ ] **Step 5: Migrate single status check to OperationExecutor**

In `CheckApplicationStatus`, replace direct `GetApplicationStatusAsync(...)` with:

```csharp
using var operationExecutor = CreateOperationExecutor();
var appInfo = GetApplicationInfo(appCard.Id) ?? new ApplicationInfo
{
    Id = appCard.Id,
    Name = appCard.Name,
    Version = appCard.Version
};
var request = new OperationRequest(OperationType.CheckStatus, host, appInfo);
var result = await operationExecutor.ExecuteAsync(request, EnqueueOperationEvent);

if (result.Status != null)
{
    ApplyApplicationStatus(appCard, result.Status);
}
```

- [ ] **Step 6: Migrate single uninstall to OperationExecutor and remove synchronous Dispatcher.Invoke**

In `UninstallApplication`, replace the `Progress<InstallTask>` block that contains `Application.Current.Dispatcher.Invoke` with no direct progress object. Replace direct `installerService.UninstallAsync(...)` with:

```csharp
using var operationExecutor = CreateOperationExecutor();
var request = new OperationRequest(
    OperationType.Uninstall,
    host,
    appInfo,
    keepData: keepData,
    isBatch: false);

var operationResult = await operationExecutor.ExecuteAsync(request, EnqueueOperationEvent);

taskViewModel.TaskId = operationResult.TaskId;
taskViewModel.IsCompleted = operationResult.TaskStatus == Models.TaskStatus.Completed;
taskViewModel.IsFailed = operationResult.TaskStatus == Models.TaskStatus.Failed;
taskViewModel.IsCanceled = operationResult.TaskStatus == Models.TaskStatus.Cancelled;
if (!string.IsNullOrWhiteSpace(operationResult.ErrorMessage))
{
    taskViewModel.StatusMessage = operationResult.ErrorMessage;
}
```

Keep existing post-uninstall success/failure messages, but read from `operationResult`.

- [ ] **Step 7: Migrate batch install/detect/uninstall to BatchOperationRunner**

In `BatchInstall`, build requests before execution:

```csharp
var requests = selectedHosts
    .SelectMany(host => selectedApps.Select(app => BuildBatchInstallOperationRequest(app, host)))
    .Where(request => request != null)
    .Cast<OperationRequest>()
    .ToList();
using var operationExecutor = CreateOperationExecutor();
var runner = new BatchOperationRunner(operationExecutor);
var summary = await runner.RunInstallQueueAsync(requests, EnqueueOperationEvent, token);
successCount = summary.SuccessCount;
failedCount = summary.FailedCount;
canceledCount = summary.CanceledCount;
completedCount = summary.SuccessCount + summary.FailedCount + summary.CanceledCount;
```

Add helper:

```csharp
private OperationRequest? BuildBatchInstallOperationRequest(ApplicationCardViewModel app, HostViewModel hostViewModel)
{
    var host = GetHostFromViewModel(hostViewModel);
    var appInfo = GetApplicationInfo(app.Id);
    if (host == null || appInfo == null || IsUnifiedJdkApplicationId(appInfo.Id))
    {
        return null;
    }

    var parameters = BuildBatchInstallParameters(appInfo);
    var executionAppInfo = ResolveApplicationInfoForExecution(appInfo, appInfo.Version) ?? appInfo;
    return new OperationRequest(OperationType.Install, host, executionAppInfo, parameters, localPackagePath: string.Empty, isBatch: true);
}
```

In `BatchCheckStatus`, keep host status refresh if needed for full application card refresh, but add `BatchOperationRunner.RunStatusQueueAsync` for selected host/application requests with a fixed concurrency derived from setting:

```csharp
var maxConcurrentChecks = int.TryParse(_databaseService.GetSetting("MaxConcurrentTasks", "3"), out var checkConcurrency)
    ? Math.Max(1, checkConcurrency)
    : 3;
using var operationExecutor = CreateOperationExecutor();
var runner = new BatchOperationRunner(operationExecutor);
var requests = SelectedHosts
    .SelectMany(host => Applications.Select(app => BuildStatusOperationRequest(app, host)))
    .Where(request => request != null)
    .Cast<OperationRequest>()
    .ToList();
await runner.RunStatusQueueAsync(requests, maxConcurrentChecks, EnqueueOperationEvent, token);
```

Add helper:

```csharp
private OperationRequest? BuildStatusOperationRequest(ApplicationCardViewModel app, HostViewModel hostViewModel)
{
    var host = GetHostFromViewModel(hostViewModel);
    var appInfo = GetApplicationInfo(app.Id);
    return host == null || appInfo == null
        ? null
        : new OperationRequest(OperationType.CheckStatus, host, appInfo, isBatch: true);
}
```

In `BatchUninstall`, replace the manual host semaphore block with:

```csharp
using var operationExecutor = CreateOperationExecutor();
var runner = new BatchOperationRunner(operationExecutor);
var requests = SelectedHosts
    .SelectMany(host => selectedApps.Select(app => BuildUninstallOperationRequest(app, host)))
    .Where(request => request != null)
    .Cast<OperationRequest>()
    .ToList();
var summary = await runner.RunUninstallQueueAsync(requests, maxConcurrentTasks, EnqueueOperationEvent, token);
completedCount = summary.SuccessCount + summary.FailedCount + summary.CanceledCount;
successCount = summary.SuccessCount;
failedCount = summary.FailedCount;
canceledCount = summary.CanceledCount;
```

Add helper:

```csharp
private OperationRequest? BuildUninstallOperationRequest(ApplicationCardViewModel app, HostViewModel hostViewModel)
{
    var host = GetHostFromViewModel(hostViewModel);
    var appInfo = GetApplicationInfo(app.Id);
    return host == null || appInfo == null
        ? null
        : new OperationRequest(OperationType.Uninstall, host, appInfo, keepData: false, isBatch: true);
}
```

- [ ] **Step 8: Run the integration boundary tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "MainViewModelOperationPipelineTests|MainViewModelTaskUiThrottlingTests" --no-restore
```

Expected: `Passed!` with all operation pipeline boundary tests passing.

- [ ] **Step 9: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/ViewModels/MainViewModel.cs RemoteInstaller.Tests/MainViewModelOperationPipelineTests.cs RemoteInstaller.Tests/MainViewModelTaskUiThrottlingTests.cs
git commit -m @'
refactor: route view model operations through pipeline
'@
```

## Task 6: Throttle InstallerService Task Persistence

**Files:**
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Test: `RemoteInstaller.Tests/InstallerServicePersistenceThrottlingTests.cs`

- [ ] **Step 1: Write the failing structural tests**

Create `RemoteInstaller.Tests/InstallerServicePersistenceThrottlingTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class InstallerServicePersistenceThrottlingTests
{
    [Fact]
    public void InstallerService_UsesThrottledTaskPersistenceInProgressCallbacks()
    {
        var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        Assert.Contains("RequestTaskPersistence", source);
        Assert.Contains("FlushTaskPersistence", source);
        Assert.DoesNotContain("try { _databaseService.SaveTask(task); } catch", ExtractInstallProgressHandler(source));
    }

    private static string ExtractInstallProgressHandler(string source)
    {
        var start = source.IndexOf("logCollector.ProgressUpdated +=", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find install progress handler.");
        var end = source.IndexOf("logCollector.LogReceived", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find end of install progress handler.");
        return source[start..end];
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    private static string GetProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RemoteInstaller.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 RemoteInstaller.sln，无法定位项目根目录。");
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter InstallerServicePersistenceThrottlingTests --no-restore
```

Expected: fail because `InstallerService` still saves task directly in progress callbacks.

- [ ] **Step 3: Add persistence throttling helpers**

In `RemoteInstaller/Services/InstallerService.cs`, add fields near `_disposed`:

```csharp
private readonly object _taskPersistenceLock = new();
private readonly Dictionary<string, InstallTask> _pendingTaskPersistence = new(StringComparer.OrdinalIgnoreCase);
private readonly System.Timers.Timer _taskPersistenceTimer = new(300) { AutoReset = false };
```

In constructor, add:

```csharp
_taskPersistenceTimer.Elapsed += (_, _) => FlushTaskPersistence();
```

Add helper methods before `InstallAsync`:

```csharp
private void RequestTaskPersistence(InstallTask task)
{
    lock (_taskPersistenceLock)
    {
        _pendingTaskPersistence[task.Id] = task;
        if (!_taskPersistenceTimer.Enabled)
        {
            _taskPersistenceTimer.Start();
        }
    }
}

private void FlushTaskPersistence()
{
    List<InstallTask> tasks;
    lock (_taskPersistenceLock)
    {
        tasks = _pendingTaskPersistence.Values.ToList();
        _pendingTaskPersistence.Clear();
        _taskPersistenceTimer.Stop();
    }

    foreach (var task in tasks)
    {
        try
        {
            _databaseService.SaveTask(task);
        }
        catch
        {
        }
    }
}

private void SaveTaskImmediately(InstallTask task)
{
    FlushTaskPersistence();
    _databaseService.SaveTask(task);
}
```

In `Dispose`, before `_fileLogger.Dispose();`, add:

```csharp
_taskPersistenceTimer.Stop();
_taskPersistenceTimer.Dispose();
FlushTaskPersistence();
```

- [ ] **Step 4: Replace high-frequency SaveTask calls**

In install and uninstall `logCollector.ProgressUpdated` handlers, replace:

```csharp
try { _databaseService.SaveTask(task); } catch { /* 忽略更新错误 */ }
```

and:

```csharp
try { _databaseService.SaveTask(task); } catch { }
```

with:

```csharp
RequestTaskPersistence(task);
```

For start/final/cancel/fail saves, replace direct `_databaseService.SaveTask(task);` with:

```csharp
SaveTaskImmediately(task);
```

Only apply this to `InstallAsync` and `UninstallAsync` in this task.

- [ ] **Step 5: Run the throttling test**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter InstallerServicePersistenceThrottlingTests --no-restore
```

Expected: `Passed!`.

- [ ] **Step 6: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/InstallerServicePersistenceThrottlingTests.cs
git commit -m @'
perf: throttle task persistence during operations
'@
```

## Task 7: Build and Automated Regression Suite

**Files:**
- No source file changes expected.

- [ ] **Step 1: Run build**

Run:

```powershell
dotnet build RemoteInstaller.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 2: If build fails, fix compile errors before continuing**

For compile errors in new Operations files, fix exact type names and namespace mismatches. Common expected corrections:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
```

For ambiguous `TaskStatus`, use:

```csharp
Models.TaskStatus.Completed
Models.TaskStatus.Failed
Models.TaskStatus.Cancelled
```

- [ ] **Step 3: Run full automated tests**

Run:

```powershell
dotnet test
```

Expected: all tests pass. If tests fail due to changed string-structure assertions, update tests only when the production behavior is still covered by a stronger operation-pipeline assertion.

- [ ] **Step 4: Commit only if the user has explicitly authorized commits**

```powershell
git add RemoteInstaller RemoteInstaller.Tests
git commit -m @'
test: validate operation pipeline regression suite
'@
```

## Task 8: WPF Smoke Test and Resource Cleanup

**Files:**
- No source file changes expected unless smoke test finds defects.

- [ ] **Step 1: Stop leftover app and browser test processes**

Run:

```powershell
Get-Process node,chrome,msedge,RemoteInstaller -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
```

Expected: command completes. It may produce no output if no matching processes exist.

- [ ] **Step 2: Start the WPF app**

Run:

```powershell
$env:DOTNET_ROOT = "C:/Users/WY/.dotnet-10"; & "C:/Users/WY/.dotnet-10/dotnet.exe" run --project RemoteInstaller/RemoteInstaller.csproj
```

Expected: Remote Installer window opens.

- [ ] **Step 3: Smoke test UI responsiveness manually**

Perform these actions in the app:

```text
1. Select or add CentOS 7 host 192.168.60.152.
2. Select or add Ubuntu 24 host 192.168.60.154.
3. Search applications by name in the application search box.
4. Switch app category filters.
5. Open and close task details for an existing or newly created task.
6. Trigger single status check for Nginx on one host.
```

Expected:

```text
- Search and filter do not freeze the UI.
- Task detail logs scroll normally.
- Single status check completes or reports a clear host/application error.
- No unhandled exception dialog appears.
```

- [ ] **Step 4: Stop the WPF app after smoke testing**

Run:

```powershell
Get-Process RemoteInstaller -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
```

Expected: app process stops.

## Task 9: CentOS 7 and Ubuntu 24 Full Operation Validation

**Files:**
- Create if validation results need durable record: `docs/operation-validation-2026-05-13.md`
- Modify after validation: `README.md`

- [ ] **Step 1: Create validation checklist file**

Create `docs/operation-validation-2026-05-13.md` with this content:

```markdown
# 操作管线实机验证记录

日期：2026-05-13

## 主机

| 系统 | IP | 用户 | 结果 |
| --- | --- | --- | --- |
| CentOS 7 | 192.168.60.152 | root | 待验证 |
| Ubuntu 24 | 192.168.60.154 | root | 待验证 |

## 验证矩阵

| 系统 | 应用 | 前置检测 | 安装 | 安装后检测 | 卸载 | 卸载后检测 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CentOS 7 | MySQL | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | MariaDB | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | Redis | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | Nginx | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | Elasticsearch | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | RabbitMQ | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | Mosquitto | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| CentOS 7 | Consul | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | MySQL | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | MariaDB | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | Redis | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | Nginx | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | Elasticsearch | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | RabbitMQ | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | Mosquitto | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |
| Ubuntu 24 | Consul | 待验证 | 待验证 | 待验证 | 待验证 | 待验证 |  |

## 卡顿观察

- 批量检测：待记录
- 单机安装：待记录
- 单机检测：待记录
- 单机卸载：待记录
- 任务详情日志：待记录

## 失败与残留

暂无记录。
```

- [ ] **Step 2: Run baseline batch detection inside the app**

In WPF app:

```text
1. Select CentOS 7 and Ubuntu 24 hosts.
2. Select all Linux-supported applications in the application market.
3. Click 批量检测.
4. Wait until the task/log area reports completion.
```

Expected: batch detection completes. UI remains responsive enough to move window, switch filters, and scroll task/log list.

- [ ] **Step 3: Validate each application on CentOS 7**

For each app in this order:

```text
MySQL -> MariaDB -> Redis -> Nginx -> Elasticsearch -> RabbitMQ -> Mosquitto -> Consul
```

Run inside the app against `192.168.60.152`:

```text
1. Click 检测 and record result.
2. Click 安装 and use default parameters unless the install dialog requires a value.
3. Click 检测 and record installed/running/version/port result.
4. Click 卸载 and confirm.
5. Click 检测 and record uninstalled/residue result.
```

Expected: each step either succeeds or records a clear resource/environment failure in `docs/operation-validation-2026-05-13.md`.

- [ ] **Step 4: Validate each application on Ubuntu 24**

For each app in this order:

```text
MySQL -> MariaDB -> Redis -> Nginx -> Elasticsearch -> RabbitMQ -> Mosquitto -> Consul
```

Run inside the app against `192.168.60.154`:

```text
1. Click 检测 and record result.
2. Click 安装 and use default parameters unless the install dialog requires a value.
3. Click 检测 and record installed/running/version/port result.
4. Click 卸载 and confirm.
5. Click 检测 and record uninstalled/residue result.
```

Expected: each step either succeeds or records a clear resource/environment failure in `docs/operation-validation-2026-05-13.md`.

- [ ] **Step 5: Run final batch detection inside the app**

In WPF app:

```text
1. Select CentOS 7 and Ubuntu 24 hosts.
2. Click 批量检测.
3. Confirm all apps report uninstalled or residue-only warning after uninstall attempts.
```

Expected: batch detection completes without UI freeze.

- [ ] **Step 6: Update README with validation summary**

In `README.md`, add this section under the core capability overview or supplemental notes:

```markdown
## 2026-05-13 操作管线优化与验证

本轮将安装、检测、卸载和批量操作收敛到统一操作管线，减少远程操作期间的 UI 高频刷新、同步 Dispatcher 调用和任务状态高频落库。

验证覆盖：

- CentOS 7：192.168.60.152
- Ubuntu 24：192.168.60.154
- 操作顺序：前置检测 -> 安装 -> 安装后检测 -> 卸载 -> 卸载后检测
- 批量检测：优化为受控并发，避免一次性刷新造成 SSH 与 UI 峰值压力

详细验证结果见 `docs/operation-validation-2026-05-13.md`。
```

- [ ] **Step 7: Commit only if the user has explicitly authorized commits**

```powershell
git add README.md docs/operation-validation-2026-05-13.md
git commit -m @'
docs: record operation pipeline validation
'@
```

## Task 10: Final Verification

**Files:**
- No planned source changes.

- [ ] **Step 1: Run final build**

Run:

```powershell
dotnet build RemoteInstaller.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 2: Run final tests**

Run:

```powershell
dotnet test
```

Expected: all tests pass.

- [ ] **Step 3: Check git status**

Run:

```powershell
git status --short
```

Expected: only intended source, test, README, validation, spec, and plan files are modified or untracked.

- [ ] **Step 4: Produce completion summary**

Report to user:

```text
已完成统一操作管线重构、UI 防卡顿优化、自动测试和实机验证。
构建结果：通过/失败原因。
测试结果：通过/失败原因。
实机验证：CentOS 7 与 Ubuntu 24 的应用逐项结果见 docs/operation-validation-2026-05-13.md。
```
