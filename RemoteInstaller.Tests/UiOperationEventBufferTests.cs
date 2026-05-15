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
