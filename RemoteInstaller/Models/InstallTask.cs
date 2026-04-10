using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 安装任务模型
/// </summary>
public partial class InstallTask : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");
    
    [ObservableProperty]
    private string _hostId;
    
    [ObservableProperty]
    private string _hostName;
    
    [ObservableProperty]
    private string _appId;
    
    [ObservableProperty]
    private string _appName;
    
    [ObservableProperty]
    private string _appVersion;
    
    [ObservableProperty]
    private TaskStatus _status = TaskStatus.Pending;
    
    [ObservableProperty]
    private InstallStage _stage = InstallStage.Preparing;
    
    [ObservableProperty]
    private double _progress = 0;
    
    [ObservableProperty]
    private string _errorMessage;
    
    [ObservableProperty]
    private DateTime _startTime;
    
    [ObservableProperty]
    private DateTime _endTime;
    
    [ObservableProperty]
    private bool _isPaused;
    
    [ObservableProperty]
    private bool _canCancel = true;

    /// <summary>
    /// 阶段显示文本
    /// </summary>
    public string StageDisplayText => Stage switch
    {
        InstallStage.Preparing => "准备中...",
        InstallStage.Connecting => "连接服务器...",
        InstallStage.Uploading => "上传文件...",
        InstallStage.Extracting => "解压文件...",
        InstallStage.Configuring => "配置参数...",
        InstallStage.Installing => "执行安装...",
        InstallStage.Starting => "启动服务...",
        InstallStage.Verifying => "验证安装...",
        InstallStage.Completed => "✅ 完成",
        InstallStage.Failed => "❌ 失败",
        InstallStage.Cancelled => "⏹️ 已取消",
        _ => "未知"
    };

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusDisplayText => Status switch
    {
        TaskStatus.Pending => "等待中",
        TaskStatus.Running => "运行中",
        TaskStatus.Paused => "已暂停",
        TaskStatus.Completed => "已完成",
        TaskStatus.Failed => "已失败",
        TaskStatus.Cancelled => "已取消",
        _ => "未知"
    };

    /// <summary>
    /// 是否完成
    /// </summary>
    public bool IsCompleted => Status == TaskStatus.Completed 
        || Status == TaskStatus.Failed 
        || Status == TaskStatus.Cancelled;

    /// <summary>
    /// 开始任务
    /// </summary>
    public void Start()
    {
        Status = TaskStatus.Running;
        StartTime = DateTime.Now;
        IsPaused = false;
    }

    /// <summary>
    /// 暂停任务
    /// </summary>
    public void Pause()
    {
        Status = TaskStatus.Paused;
        IsPaused = true;
    }

    /// <summary>
    /// 恢复任务
    /// </summary>
    public void Resume()
    {
        Status = TaskStatus.Running;
        IsPaused = false;
    }

    /// <summary>
    /// 完成任务
    /// </summary>
    public void Complete()
    {
        Status = TaskStatus.Completed;
        Stage = InstallStage.Completed;
        Progress = 100;
        EndTime = DateTime.Now;
        CanCancel = false;
    }

    /// <summary>
    /// 标记失败
    /// </summary>
    public void Fail(string errorMessage)
    {
        Status = TaskStatus.Failed;
        Stage = InstallStage.Failed;
        ErrorMessage = errorMessage;
        EndTime = DateTime.Now;
        CanCancel = false;
    }

    /// <summary>
    /// 取消任务
    /// </summary>
    public void Cancel()
    {
        Status = TaskStatus.Cancelled;
        Stage = InstallStage.Cancelled;
        EndTime = DateTime.Now;
        CanCancel = false;
    }

    /// <summary>
    /// 更新进度
    /// </summary>
    public void UpdateProgress(InstallStage stage, double progress, string? errorMessage = null)
    {
        Stage = stage;
        Progress = Math.Max(0, Math.Min(100, progress));
        if (errorMessage != null)
        {
            ErrorMessage = errorMessage;
        }
    }
}
