using System;
using System.Collections.Generic;
using System.Linq;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 日志查询服务
/// 提供日志的查询、过滤和统计功能
/// </summary>
public class LogQueryService
{
    private readonly DatabaseService _databaseService;

    public LogQueryService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// 获取任务的所有日志
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="limit">返回数量限制（null 表示无限制）</param>
    public List<LogEntry> GetTaskLogs(string taskId, int? limit = null)
    {
        return _databaseService.GetTaskLogs(taskId, limit);
    }

    /// <summary>
    /// 获取任务的错误日志
    /// </summary>
    public List<LogEntry> GetTaskErrorLogs(string taskId, int? limit = null)
    {
        return _databaseService.GetTaskLogsByLevel(taskId, LogLevel.Error, limit);
    }

    /// <summary>
    /// 获取任务的警告日志
    /// </summary>
    public List<LogEntry> GetTaskWarningLogs(string taskId, int? limit = null)
    {
        return _databaseService.GetTaskLogsByLevel(taskId, LogLevel.Warning, limit);
    }

    /// <summary>
    /// 获取任务的成功日志
    /// </summary>
    public List<LogEntry> GetTaskSuccessLogs(string taskId, int? limit = null)
    {
        return _databaseService.GetTaskLogsByLevel(taskId, LogLevel.Success, limit);
    }

    /// <summary>
    /// 获取任务的 Info 日志
    /// </summary>
    public List<LogEntry> GetTaskInfoLogs(string taskId, int? limit = null)
    {
        return _databaseService.GetTaskLogsByLevel(taskId, LogLevel.Info, limit);
    }

    /// <summary>
    /// 获取任务日志（按级别过滤）
    /// </summary>
    public List<LogEntry> GetTaskLogsByLevel(string taskId, LogLevel level, int? limit = null)
    {
        return _databaseService.GetTaskLogsByLevel(taskId, level, limit);
    }

    /// <summary>
    /// 获取任务日志（按时间范围过滤）
    /// </summary>
    public List<LogEntry> GetTaskLogsByTimeRange(
        string taskId, 
        DateTime startTime, 
        DateTime endTime, 
        int? limit = null)
    {
        var allLogs = GetTaskLogs(taskId, limit);
        
        return allLogs.Where(l => l.Timestamp >= startTime && l.Timestamp <= endTime)
            .ToList();
    }

    /// <summary>
    /// 搜索日志（按关键词）
    /// </summary>
    public List<LogEntry> SearchLogs(string taskId, string keyword, int? limit = null)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            return GetTaskLogs(taskId, limit);
        }

        var allLogs = GetTaskLogs(taskId, limit);
        
        return allLogs.Where(l => l.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 获取日志摘要
    /// </summary>
    public List<LogSummary> GetLogSummaries(int limit = 100)
    {
        return _databaseService.GetLogSummaries(limit);
    }

    /// <summary>
    /// 获取任务日志统计
    /// </summary>
    public LogStatistics GetTaskLogStatistics(string taskId)
    {
        var logs = GetTaskLogs(taskId);
        
        return new LogStatistics
        {
            TotalCount = logs.Count,
            InfoCount = logs.Count(l => l.Level == RemoteInstaller.Models.LogLevel.Info),
            SuccessCount = logs.Count(l => l.Level == RemoteInstaller.Models.LogLevel.Success),
            WarningCount = logs.Count(l => l.Level == RemoteInstaller.Models.LogLevel.Warning),
            ErrorCount = logs.Count(l => l.Level == RemoteInstaller.Models.LogLevel.Error),
            FirstLogTime = logs.Any() ? logs.Min(l => l.Timestamp) : DateTime.MinValue,
            LastLogTime = logs.Any() ? logs.Max(l => l.Timestamp) : DateTime.MinValue
        };
    }

    /// <summary>
    /// 导出任务日志为文本
    /// </summary>
    public string ExportTaskLogsToText(string taskId, bool includeTimestamp = true, RemoteInstaller.Models.LogLevel? filterLevel = null)
    {
        var logs = GetTaskLogs(taskId);
        
        if (filterLevel.HasValue)
        {
            logs = logs.Where(l => l.Level == filterLevel.Value).ToList();
        }

        var sb = new System.Text.StringBuilder();
        
        foreach (var log in logs)
        {
            if (includeTimestamp)
            {
                sb.AppendLine($"[{log.FormattedTime}] {log.LevelIcon} [{log.Level}] {log.Message}");
            }
            else
            {
                sb.AppendLine($"{log.LevelIcon} [{log.Level}] {log.Message}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 删除任务日志
    /// </summary>
    public void DeleteTaskLogs(string taskId)
    {
        _databaseService.DeleteTaskLogs(taskId);
    }

    /// <summary>
    /// 清理旧日志
    /// </summary>
    public void CleanupOldLogs(int daysToKeep = 30)
    {
        _databaseService.CleanupOldLogs(daysToKeep);
    }

    /// <summary>
    /// 获取最近 N 条日志
    /// </summary>
    public List<LogEntry> GetRecentLogs(string taskId, int count = 50)
    {
        return GetTaskLogs(taskId, count);
    }
}

/// <summary>
/// 日志统计信息
/// </summary>
public class LogStatistics
{
    public int TotalCount { get; set; }
    public int InfoCount { get; set; }
    public int SuccessCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime FirstLogTime { get; set; }
    public DateTime LastLogTime { get; set; }

    /// <summary>
    /// 持续时间
    /// </summary>
    public TimeSpan Duration => LastLogTime - FirstLogTime;

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate => TotalCount > 0 ? (double)ErrorCount / TotalCount * 100 : 0;
}
