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
