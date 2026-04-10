using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 安装历史记录模型
/// P1 功能：任务历史记录
/// </summary>
public partial class InstallHistory : ObservableObject
{
    [ObservableProperty]
    private int _id;

    /// <summary>
    /// 主机 ID
    /// </summary>
    [ObservableProperty]
    private string _hostId = string.Empty;

    /// <summary>
    /// 主机名称
    /// </summary>
    [ObservableProperty]
    private string _hostName = string.Empty;

    /// <summary>
    /// 应用 ID
    /// </summary>
    [ObservableProperty]
    private string _applicationId = string.Empty;

    /// <summary>
    /// 应用名称
    /// </summary>
    [ObservableProperty]
    private string _applicationName = string.Empty;

    /// <summary>
    /// 应用版本
    /// </summary>
    [ObservableProperty]
    private string _applicationVersion = string.Empty;

    /// <summary>
    /// 操作类型（安装/卸载）
    /// </summary>
    [ObservableProperty]
    private HistoryOperationType _operationType = HistoryOperationType.Install;

    /// <summary>
    /// 任务状态
    /// </summary>
    [ObservableProperty]
    private TaskStatus _status = TaskStatus.Pending;

    /// <summary>
    /// 开始时间
    /// </summary>
    [ObservableProperty]
    private DateTime _startTime;

    /// <summary>
    /// 结束时间
    /// </summary>
    [ObservableProperty]
    private DateTime? _endTime;

    /// <summary>
    /// 错误信息
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 日志内容（JSON 格式存储）
    /// </summary>
    [ObservableProperty]
    private string? _logContent;

    /// <summary>
    /// 操作类型显示文本
    /// </summary>
    public string OperationTypeDisplayText => OperationType switch
    {
        HistoryOperationType.Install => "📥 安装",
        HistoryOperationType.Uninstall => "🗑️ 卸载",
        _ => "未知"
    };

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusDisplayText => Status switch
    {
        TaskStatus.Pending => "⏳ 等待中",
        TaskStatus.Running => "🔄 进行中",
        TaskStatus.Paused => "⏸️ 已暂停",
        TaskStatus.Completed => "✅ 成功",
        TaskStatus.Failed => "❌ 失败",
        TaskStatus.Cancelled => "⏹️ 已取消",
        _ => "未知"
    };

    /// <summary>
    /// 状态颜色
    /// </summary>
    public string StatusColor => Status switch
    {
        TaskStatus.Pending => "#ffab40",
        TaskStatus.Running => "#4fc1ff",
        TaskStatus.Paused => "#dcdcaa",
        TaskStatus.Completed => "#4ec9b0",
        TaskStatus.Failed => "#f14c4c",
        TaskStatus.Cancelled => "#6b6b6b",
        _ => "#d4d4d4"
    };

    /// <summary>
    /// 耗时（秒）
    /// </summary>
    public double? DurationSeconds => EndTime.HasValue 
        ? (EndTime.Value - StartTime).TotalSeconds 
        : null;

    /// <summary>
    /// 耗时显示文本
    /// </summary>
    public string DurationDisplayText
    {
        get
        {
            if (!DurationSeconds.HasValue)
            {
                return "进行中...";
            }

            var seconds = DurationSeconds.Value;
            if (seconds < 60)
            {
                return $"{seconds:F1}秒";
            }
            else if (seconds < 3600)
            {
                return $"{seconds / 60:F1}分钟";
            }
            else
            {
                var hours = seconds / 3600;
                var mins = (seconds % 3600) / 60;
                return $"{hours:F1}小时{mins:F0}分";
            }
        }
    }

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted => Status == TaskStatus.Completed 
        || Status == TaskStatus.Failed 
        || Status == TaskStatus.Cancelled;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess => Status == TaskStatus.Completed;

    /// <summary>
    /// 是否失败
    /// </summary>
    public bool IsFailed => Status == TaskStatus.Failed;
}

/// <summary>
/// 历史记录操作类型
/// </summary>
public enum HistoryOperationType
{
    /// <summary>
    /// 安装
    /// </summary>
    Install = 0,
    
    /// <summary>
    /// 卸载
    /// </summary>
    Uninstall = 1
}
