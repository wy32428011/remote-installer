using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 日志服务接口
/// </summary>
public interface ILogger
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
    
    void Log(RemoteInstaller.Models.LogLevel level, string message, string? taskId = null);
}

/// <summary>
/// 日志服务实现
/// </summary>
public class LoggerService : ILogger
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action<LogEntry>? EntryAdded;

    public List<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return new List<LogEntry>(_entries);
        }
    }

    public List<LogEntry> GetEntriesForTask(string taskId)
    {
        lock (_lock)
        {
            return _entries.Where(e => e.TaskId == taskId).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public void Info(string message) => Log(LogLevel.Info, message);
    public void Success(string message) => Log(LogLevel.Success, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);

    public void Log(RemoteInstaller.Models.LogLevel level, string message, string? taskId = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            TaskId = taskId
        };

        lock (_lock)
        {
            _entries.Add(entry);
            // 限制日志数量
            if (_entries.Count > 1000)
            {
                _entries.RemoveRange(0, _entries.Count - 1000);
            }
        }

        EntryAdded?.Invoke(entry);
    }
}
