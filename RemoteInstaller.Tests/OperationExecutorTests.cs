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
        Assert.Contains(events, item => item.Kind == OperationEventKind.Progress && item.Stage == "执行安装...");
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

    [Fact]
    public async Task ExecuteAsync_InstallPublishesTerminalEventAfterProgressAndLogEvents()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "nginx", Name = "Nginx", Version = "1.25.3" };
        var installer = new FakeOperationInstaller
        {
            InstallTaskFactory = () => CompletedTask(host, app, "task-install")
        };
        var events = new List<OperationEvent>();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(OperationType.Install, host, app);

        await executor.ExecuteAsync(request, events.Add, CancellationToken.None);

        var progressIndex = events.FindIndex(item => item.Kind == OperationEventKind.Progress);
        var logIndex = events.FindIndex(item => item.Kind == OperationEventKind.Log);
        var terminalIndex = events.FindIndex(item => item.Kind == OperationEventKind.Completed);
        Assert.True(progressIndex >= 0);
        Assert.True(logIndex >= 0);
        Assert.True(terminalIndex > progressIndex);
        Assert.True(terminalIndex > logIndex);
    }

    [Fact]
    public async Task ExecuteAsync_InstallUsesProgressTaskIdForLogEventsWhenLogEntryTaskIdIsMissing()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "nginx", Name = "Nginx", Version = "1.25.3" };
        var installer = new FakeOperationInstaller
        {
            InstallTaskFactory = () => CompletedTask(host, app, "task-install"),
            OmitLogTaskId = true
        };
        var events = new List<OperationEvent>();
        var executor = new OperationExecutor(installer);

        await executor.ExecuteAsync(new OperationRequest(OperationType.Install, host, app), events.Add, CancellationToken.None);

        Assert.Contains(events, item => item.Kind == OperationEventKind.Log && item.TaskId == "task-install");
    }

    [Fact]
    public async Task ExecuteAsync_CheckStatusPassesRequestParametersToInstaller()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "traefik", Name = "Traefik", Version = "3.6.13" };
        var installer = new FakeOperationInstaller();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(OperationType.CheckStatus, host, app, new Dictionary<string, string> { ["CONFIG_DIR"] = "/etc/traefik-validation" });

        await executor.ExecuteAsync(request, events => { }, CancellationToken.None);

        Assert.NotNull(installer.LastStatusParameters);
        Assert.Equal("/etc/traefik-validation", installer.LastStatusParameters!["CONFIG_DIR"]);
    }

    [Fact]
    public async Task ExecuteAsync_UninstallPassesRequestParametersToInstaller()
    {
        var host = new RemoteHost { Id = "host-1", Name = "测试主机", IpAddress = "127.0.0.1" };
        var app = new ApplicationInfo { Id = "traefik", Name = "Traefik", Version = "3.6.13" };
        var installer = new FakeOperationInstaller();
        var executor = new OperationExecutor(installer);
        var request = new OperationRequest(
            OperationType.Uninstall,
            host,
            app,
            new Dictionary<string, string> { ["CONFIG_DIR"] = "/etc/traefik-validation" },
            keepData: false);

        await executor.ExecuteAsync(request, events => { }, CancellationToken.None);

        Assert.NotNull(installer.LastUninstallParameters);
        Assert.Equal("/etc/traefik-validation", installer.LastUninstallParameters!["CONFIG_DIR"]);
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
        public bool OmitLogTaskId { get; init; }
        public ApplicationStatus Status { get; init; } = new();
        public Dictionary<string, string>? LastStatusParameters { get; private set; }
        public Dictionary<string, string>? LastUninstallParameters { get; private set; }

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
            logReporter?.Report(new LogEntry { Message = "安装完成", Level = LogLevel.Success, Timestamp = DateTime.Now, TaskId = OmitLogTaskId ? null : task.Id });
            return Task.FromResult(task);
        }

        public Task<ApplicationStatus> CheckStatusAsync(
            RemoteHost host,
            ApplicationInfo app,
            Dictionary<string, string>? parameters,
            CancellationToken cancellationToken)
        {
            LastStatusParameters = parameters;
            return Task.FromResult(Status);
        }

        public Task<InstallTask> UninstallAsync(
            RemoteHost host,
            ApplicationInfo app,
            Dictionary<string, string>? parameters,
            bool keepData,
            IProgress<InstallTask>? progressReporter,
            CancellationToken cancellationToken,
            IProgress<LogEntry>? logReporter)
        {
            LastUninstallParameters = parameters;
            var task = UninstallTaskFactory?.Invoke() ?? CompletedTask(host, app, "task-uninstall");
            progressReporter?.Report(task);
            return Task.FromResult(task);
        }
    }
}
