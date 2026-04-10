using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 日志收集器
/// 用于实时捕获 SSH 命令输出并解析进度信息
/// </summary>
public class LogCollector : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<LogEntry> _collectedLogs = new();
    private const int MaxLogCount = 5000;
    private bool _disposed;

    /// <summary>
    /// 日志条目添加事件
    /// </summary>
    public event Action<LogEntry>? LogReceived;

    /// <summary>
    /// 进度更新事件
    /// 格式：PROGRESS:阶段名称:百分比
    /// </summary>
    public event Action<string, double>? ProgressUpdated;

    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志服务</param>
    /// <param name="taskId">任务 ID</param>
    public LogCollector(ILogger logger, string taskId)
    {
        _logger = logger;
        TaskId = taskId;
    }

    /// <summary>
    /// 处理 SSH 输出
    /// 解析进度上报和日志信息
    /// </summary>
    /// <param name="output">SSH 命令输出</param>
    public void ProcessOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return;

        // 按行处理输出
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            ProcessLine(line.Trim());
        }
    }

    /// <summary>
    /// 处理单行输出
    /// </summary>
    private void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        // 检查是否是进度上报
        if (line.StartsWith("PROGRESS:", StringComparison.OrdinalIgnoreCase))
        {
            ParseProgress(line);
        }
        else
        {
            // 普通日志
            AddLog(RemoteInstaller.Models.LogLevel.Info, line);
        }
    }

    /// <summary>
    /// 解析进度上报
    /// 格式：PROGRESS:阶段名称:百分比
    /// </summary>
    private void ParseProgress(string line)
    {
        try
        {
            var parts = line.Substring("PROGRESS:".Length).Split(':');
            if (parts.Length >= 2)
            {
                var stage = parts[0].Trim();
                if (double.TryParse(parts[1].Trim(), out var percent))
                {
                    percent = Math.Max(0, Math.Min(100, percent));
                    ProgressUpdated?.Invoke(stage, percent);

                    // 同时记录到日志
                    AddLog(RemoteInstaller.Models.LogLevel.Info, $"[{stage}] 进度：{percent:F0}%");
                }
            }
        }
        catch (Exception ex)
        {
            AddLog(RemoteInstaller.Models.LogLevel.Warning, $"进度解析失败：{line} - {ex.Message}");
        }
    }

    /// <summary>
    /// 添加日志条目
    /// </summary>
    public void AddLog(RemoteInstaller.Models.LogLevel level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            TaskId = TaskId
        };

        _collectedLogs.Enqueue(entry);

        // 限制日志数量，防止内存溢出
        // 当超过限制时，移除最旧的条目 (O(1) 操作)
        while (_collectedLogs.Count > MaxLogCount && _collectedLogs.TryDequeue(out _))
        {
            // 持续移除直到数量合适
        }

        // 触发事件
        LogReceived?.Invoke(entry);

        // 同时写入到全局日志服务
        _logger.Log(level, message, TaskId);
    }

    /// <summary>
    /// 获取所有收集的日志
    /// </summary>
    public List<LogEntry> GetLogs()
    {
        return new List<LogEntry>(_collectedLogs);
    }

    /// <summary>
    /// 根据级别过滤日志
    /// </summary>
    public List<LogEntry> GetLogsByLevel(RemoteInstaller.Models.LogLevel level)
    {
        return _collectedLogs.Where(e => e.Level == level).ToList();
    }

    /// <summary>
    /// 清除所有日志
    /// </summary>
    public void Clear()
    {
        while (_collectedLogs.TryDequeue(out _))
        {
            // 持续移除直到队列为空
        }
    }

    /// <summary>
    /// 获取日志数量
    /// </summary>
    public int Count => _collectedLogs.Count;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源 - ConcurrentQueue 没有 Clear 方法，需要逐个移除
                while (_collectedLogs.TryDequeue(out _))
                {
                    // 持续移除直到队列为空
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
