using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 文件日志服务 - 将日志写入文件，支持滚动
/// </summary>
public class FileLoggerService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFileName;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly Timer _flushTimer;
    private readonly int _maxFileSizeMB;
    private readonly int _maxFiles;
    private bool _disposed;
    private readonly object _fileLock = new();

    // StreamWriter 持有文件句柄，避免频繁打开关闭文件
    private StreamWriter? _logWriter;

    public FileLoggerService(string? logDirectory = null, string? logFileName = null, int maxFileSizeMB = 10, int maxFiles = 7)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteInstaller",
            "Logs");

        _logFileName = logFileName ?? $"RemoteInstaller_{DateTime.Now:yyyyMMdd}.log";
        _maxFileSizeMB = maxFileSizeMB;
        _maxFiles = maxFiles;

        // 确保目录存在
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // 每秒刷新一次日志到文件
        _flushTimer = new Timer(_ => FlushToFile(), null, 1000, 1000);
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    public void Log(LogLevel level, string source, string message, Exception? exception = null)
    {
        var timestamp = DateTime.Now;
        var levelStr = level.ToString().ToUpper().PadRight(7);
        var sb = new StringBuilder();
        sb.Append($"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{source}] {message}");

        if (exception != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {exception.GetType().Name}: {exception.Message}");
            if (exception.StackTrace != null)
            {
                sb.AppendLine();
                sb.Append("  StackTrace:");
                foreach (var line in exception.StackTrace.Split('\n'))
                {
                    var trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        sb.AppendLine();
                        sb.Append($"    {trimmedLine}");
                    }
                }
            }

            // 包含内部异常
            if (exception.InnerException != null)
            {
                var inner = exception.InnerException;
                sb.AppendLine();
                sb.Append($"  InnerException: {inner.GetType().Name}: {inner.Message}");
                if (inner.StackTrace != null)
                {
                    sb.AppendLine();
                    sb.Append("  InnerStackTrace:");
                    foreach (var line in inner.StackTrace.Split('\n'))
                    {
                        var trimmedLine = line.Trim();
                        if (!string.IsNullOrEmpty(trimmedLine))
                        {
                            sb.AppendLine();
                            sb.Append($"    {trimmedLine}");
                        }
                    }
                }
            }
        }

        _logQueue.Enqueue(sb.ToString());
    }

    /// <summary>
    /// 记录 Info 级别日志
    /// </summary>
    public void Info(string source, string message) => Log(LogLevel.Info, source, message);

    /// <summary>
    /// 记录 Success 级别日志
    /// </summary>
    public void Success(string source, string message) => Log(LogLevel.Success, source, message);

    /// <summary>
    /// 记录 Warning 级别日志
    /// </summary>
    public void Warning(string source, string message) => Log(LogLevel.Warning, source, message);

    /// <summary>
    /// 记录 Error 级别日志
    /// </summary>
    public void Error(string source, string message, Exception? exception = null) => Log(LogLevel.Error, source, message, exception);

    /// <summary>
    /// 刷新日志到文件
    /// </summary>
    private void FlushToFile()
    {
        if (_disposed) return;

        var lines = new List<string>();
        while (_logQueue.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        if (lines.Count == 0) return;

        lock (_fileLock)
        {
            try
            {
                var logFilePath = Path.Combine(_logDirectory, _logFileName);

                // 使用 StreamWriter 持有文件句柄，避免频繁打开关闭
                if (_logWriter == null)
                {
                    _logWriter = new StreamWriter(logFilePath, append: true, encoding: Encoding.UTF8, bufferSize: 4096);
                }

                foreach (var line in lines)
                {
                    _logWriter.WriteLine(line);
                }
                _logWriter.Flush();

                // 检查文件大小
                var fileInfo = new FileInfo(logFilePath);
                if (fileInfo.Length > _maxFileSizeMB * 1024 * 1024)
                {
                    // 滚动日志
                    RollOverLogFile();
                }
            }
            catch
            {
                // 忽略写入错误，避免日志写入失败导致程序崩溃
            }
        }
    }

    /// <summary>
    /// 滚动日志文件
    /// </summary>
    private void RollOverLogFile()
    {
        // 关闭当前的 StreamWriter
        _logWriter?.Dispose();
        _logWriter = null;

        var logFilePath = Path.Combine(_logDirectory, _logFileName);

        // 删除最旧的日志文件
        for (int i = _maxFiles - 1; i >= 0; i--)
        {
            var ext = i == 0 ? ".log" : $".{i}.log";
            var oldFile = Path.Combine(_logDirectory, _logFileName.Replace(".log", ext));
            if (File.Exists(oldFile))
            {
                if (i >= _maxFiles - 1)
                {
                    File.Delete(oldFile);
                }
                else
                {
                    var nextExt = $".{i + 1}.log";
                    var nextFile = Path.Combine(_logDirectory, _logFileName.Replace(".log", nextExt));
                    try { File.Move(oldFile, nextFile); } catch { }
                }
            }
        }

        // 重命名当前文件为 .1.log
        var backupFile = Path.Combine(_logDirectory, _logFileName.Replace(".log", ".1.log"));
        try { File.Move(logFilePath, backupFile); } catch { }
    }

    /// <summary>
    /// 获取日志目录
    /// </summary>
    public string GetLogDirectory() => _logDirectory;

    /// <summary>
    /// 读取最近的日志
    /// </summary>
    public List<string> ReadRecentLogs(int lines = 500)
    {
        var logFilePath = Path.Combine(_logDirectory, _logFileName);
        if (!File.Exists(logFilePath)) return new List<string>();

        var allLines = File.ReadAllLines(logFilePath, Encoding.UTF8);
        return allLines.Length <= lines
            ? new List<string>(allLines)
            : new List<string>(allLines.Skip(allLines.Length - lines));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _flushTimer?.Dispose();
                // 最后一次刷新
                FlushToFile();
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
