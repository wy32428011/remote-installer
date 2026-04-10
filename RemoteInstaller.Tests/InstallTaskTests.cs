using RemoteInstaller.Models;
using Xunit;
using TaskStatus = RemoteInstaller.Models.TaskStatus;

namespace RemoteInstaller.Tests;

/// <summary>
/// 安装任务模型测试
/// </summary>
public class InstallTaskTests
{
    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        // Act
        var task = new InstallTask();

        // Assert
        Assert.NotEqual(string.Empty, task.Id);
        Assert.Equal(Models.TaskStatus.Pending, task.Status);
        Assert.Equal(InstallStage.Preparing, task.Stage);
        Assert.Equal(0, task.Progress);
        Assert.False(task.IsPaused);
        Assert.True(task.CanCancel);
    }

    [Fact]
    public void Start_SetsRunningStatus()
    {
        // Arrange
        var task = new InstallTask();

        // Act
        task.Start();

        // Assert
        Assert.Equal(Models.TaskStatus.Running, task.Status);
        Assert.False(task.IsPaused);
        Assert.True(task.StartTime != default);
    }

    [Fact]
    public void Pause_SetsPausedStatus()
    {
        // Arrange
        var task = new InstallTask();
        task.Start();

        // Act
        task.Pause();

        // Assert
        Assert.Equal(TaskStatus.Paused, task.Status);
        Assert.True(task.IsPaused);
    }

    [Fact]
    public void Resume_SetsRunningStatus()
    {
        // Arrange
        var task = new InstallTask();
        task.Pause();

        // Act
        task.Resume();

        // Assert
        Assert.Equal(TaskStatus.Running, task.Status);
        Assert.False(task.IsPaused);
    }

    [Fact]
    public void Complete_SetsCompletedStatus()
    {
        // Arrange
        var task = new InstallTask();
        task.Start();

        // Act
        task.Complete();

        // Assert
        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal(InstallStage.Completed, task.Stage);
        Assert.Equal(100, task.Progress);
        Assert.False(task.CanCancel);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void Fail_SetsFailedStatus()
    {
        // Arrange
        var task = new InstallTask();
        task.Start();
        var errorMessage = "Test error message";

        // Act
        task.Fail(errorMessage);

        // Assert
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(InstallStage.Failed, task.Stage);
        Assert.Equal(errorMessage, task.ErrorMessage);
        Assert.False(task.CanCancel);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void Cancel_SetsCancelledStatus()
    {
        // Arrange
        var task = new InstallTask();
        task.Start();

        // Act
        task.Cancel();

        // Assert
        Assert.Equal(TaskStatus.Cancelled, task.Status);
        Assert.Equal(InstallStage.Cancelled, task.Stage);
        Assert.False(task.CanCancel);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void UpdateProgress_UpdatesStageAndProgress()
    {
        // Arrange
        var task = new InstallTask();

        // Act
        task.UpdateProgress(InstallStage.Installing, 75.5);

        // Assert
        Assert.Equal(InstallStage.Installing, task.Stage);
        Assert.Equal(75.5, task.Progress);
    }

    [Fact]
    public void UpdateProgress_ClampsProgressToValidRange()
    {
        // Arrange
        var task = new InstallTask();

        // Act
        task.UpdateProgress(InstallStage.Installing, -10);
        Assert.Equal(0, task.Progress);

        task.UpdateProgress(InstallStage.Installing, 150);
        Assert.Equal(100, task.Progress);
    }

    [Theory]
    [InlineData(InstallStage.Preparing, "准备中...")]
    [InlineData(InstallStage.Connecting, "连接服务器...")]
    [InlineData(InstallStage.Uploading, "上传文件...")]
    [InlineData(InstallStage.Installing, "执行安装...")]
    [InlineData(InstallStage.Completed, "✅ 完成")]
    [InlineData(InstallStage.Failed, "❌ 失败")]
    public void StageDisplayText_ReturnsCorrectText(InstallStage stage, string expectedText)
    {
        // Arrange
        var task = new InstallTask { Stage = stage };

        // Act
        var result = task.StageDisplayText;

        // Assert
        Assert.Equal(expectedText, result);
    }
}
