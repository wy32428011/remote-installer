using System;

namespace RemoteInstaller.Models;

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; set; }
    
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string? TaskId { get; set; }
    
    /// <summary>
    /// 格式化时间
    /// </summary>
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    /// <summary>
    /// 级别图标
    /// </summary>
    public string LevelIcon => Level switch
    {
        LogLevel.Info => "ℹ️",
        LogLevel.Success => "✅",
        LogLevel.Warning => "⚠️",
        LogLevel.Error => "❌",
        _ => "📝"
    };

    /// <summary>
    /// 格式化输出
    /// </summary>
    public override string ToString()
    {
        return $"[{FormattedTime}] {LevelIcon} {Message}";
    }

    /// <summary>
    /// 显示文本（用于 XAML 绑定）
    /// </summary>
    public string DisplayText => ToString();
}
