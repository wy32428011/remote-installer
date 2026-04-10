using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 连接监控器 - 负责监控 SSH 连接状态并实现自动重连
/// P1 功能：断线重连机制
/// </summary>
public class ConnectionMonitor : IDisposable
{
    private readonly SshService _sshService;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ConnectionState> _connectionStates = new();
    private readonly object _stateLock = new();
    private Timer? _monitorTimer;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// 重连配置
    /// </summary>
    public ReconnectConfig Config { get; set; } = new ReconnectConfig();

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// 重连日志事件
    /// </summary>
    public event EventHandler<ReconnectLogEventArgs>? ReconnectLog;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionMonitor(SshService sshService, ILogger logger)
    {
        _sshService = sshService;
        _logger = logger;
    }

    /// <summary>
    /// 启动连接监控
    /// </summary>
    /// <param name="intervalSeconds">监控间隔（秒）</param>
    public void Start(int intervalSeconds = 30)
    {
        if (_monitorTimer != null)
        {
            _logger.Log(LogLevel.Info, "连接监控已启动");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _monitorTimer = new Timer(async _ => await MonitorConnectionsAsync(), null, 
            TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));

        _logger.Log(LogLevel.Info, $"连接监控启动，间隔：{intervalSeconds}秒");
        LogReconnect($"连接监控启动，监控间隔：{intervalSeconds}秒");
    }

    /// <summary>
    /// 停止连接监控
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        _cancellationTokenSource = null;

        _logger.Log(LogLevel.Info, "连接监控已停止");
        LogReconnect("连接监控已停止");
    }

    /// <summary>
    /// 注册主机连接
    /// </summary>
    public void RegisterHost(RemoteHost host)
    {
        lock (_stateLock)
        {
            _connectionStates[host.Id] = new ConnectionState
            {
                Host = host,
                IsConnected = false,
                LastHeartbeat = DateTime.MinValue,
                ReconnectAttempts = 0,
                LastError = null
            };
            _logger.Log(LogLevel.Info, $"注册主机：{host.Name}");
        }
    }

    /// <summary>
    /// 注销主机连接
    /// </summary>
    public void UnregisterHost(string hostId)
    {
        lock (_stateLock)
        {
            if (_connectionStates.Remove(hostId))
            {
                _logger.Log(LogLevel.Info, $"注销主机：{hostId}");
            }
        }
    }

    /// <summary>
    /// 更新连接状态
    /// </summary>
    public void UpdateConnectionState(string hostId, bool isConnected, string? errorMessage = null)
    {
        lock (_stateLock)
        {
            if (_connectionStates.TryGetValue(hostId, out var state))
            {
                var previousState = state.IsConnected;
                state.IsConnected = isConnected;
                state.LastHeartbeat = DateTime.Now;
                state.LastError = errorMessage;

                if (isConnected)
                {
                    state.ReconnectAttempts = 0; // 重置重连计数
                }

                // 触发状态变更事件
                if (previousState != isConnected)
                {
                    ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
                    {
                        HostId = hostId,
                        IsConnected = isConnected,
                        ErrorMessage = errorMessage
                    });
                }
            }
        }
    }

    /// <summary>
    /// 获取连接状态
    /// </summary>
    public bool IsHostConnected(string hostId)
    {
        lock (_stateLock)
        {
            return _connectionStates.TryGetValue(hostId, out var state) && state.IsConnected;
        }
    }

    /// <summary>
    /// 监控所有连接（定时任务）
    /// </summary>
    private async Task MonitorConnectionsAsync()
    {
        if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }

        // 先复制状态列表，避免在 lock 中 await
        var statesToMonitor = new List<KeyValuePair<string, ConnectionState>>();
        lock (_stateLock)
        {
            statesToMonitor.AddRange(_connectionStates);
        }

        foreach (var kvp in statesToMonitor)
        {
            try
            {
                await MonitorSingleConnectionAsync(kvp.Key, kvp.Value);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"监控主机 {kvp.Key} 异常：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 监控单个连接
    /// </summary>
    private async Task MonitorSingleConnectionAsync(string hostId, ConnectionState state)
    {
        var host = state.Host;
        
        // 检查是否需要重连
        if (state.IsConnected && state.LastHeartbeat < DateTime.Now.AddMinutes(-Config.HeartbeatTimeoutMinutes))
        {
            _logger.Log(LogLevel.Warning, $"主机 {host.Name} 心跳超时，尝试重连");
            LogReconnect($"主机 {host.Name} 心跳超时，开始重连流程");
            
            // 标记为断开
            UpdateConnectionState(hostId, false, "心跳超时");
            
            // 执行重连
            await AttemptReconnectAsync(host, hostId);
        }
    }

    /// <summary>
    /// 尝试重连
    /// </summary>
    private async Task AttemptReconnectAsync(RemoteHost host, string hostId)
    {
        var success = false;

        for (int attempt = 1; attempt <= Config.MaxReconnectAttempts; attempt++)
        {
            try
            {
                _logger.Log(LogLevel.Info, $"重连尝试 {attempt}/{Config.MaxReconnectAttempts}: {host.Name}");
                LogReconnect($"重连尝试 {attempt}/{Config.MaxReconnectAttempts}: {host.Name}");

                // 测试连接
                var testResult = await _sshService.TestConnectionAsync(host);

                if (testResult.Success)
                {
                    UpdateConnectionState(hostId, true, null);
                    _logger.Log(LogLevel.Success, $"主机 {host.Name} 重连成功");
                    LogReconnect($"主机 {host.Name} 重连成功");
                    success = true;
                    break;
                }
                else
                {
                    _logger.Log(LogLevel.Warning, $"主机 {host.Name} 重连失败：{testResult.Message}");
                    LogReconnect($"重连失败：{testResult.Message}");
                    UpdateConnectionState(hostId, false, testResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"主机 {host.Name} 重连异常：{ex.Message}");
                LogReconnect($"重连异常：{ex.Message}");
                UpdateConnectionState(hostId, false, ex.Message);
            }

            // 如果不是最后一次尝试，等待后重试
            if (attempt < Config.MaxReconnectAttempts)
            {
                var waitTime = Config.ReconnectIntervalSeconds * attempt; // 指数退避
                _logger.Log(LogLevel.Info, $"等待 {waitTime} 秒后重试...");
                await Task.Delay(waitTime * 1000);
            }
        }

        if (!success)
        {
            // 重连失败，通知用户
            _logger.Log(LogLevel.Error, $"主机 {host.Name} 重连失败，已达最大尝试次数");
            LogReconnect($"重连失败，已达最大尝试次数 ({Config.MaxReconnectAttempts})");
            
            // 触发重连失败事件
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                HostId = hostId,
                IsConnected = false,
                ErrorMessage = $"重连失败，已达最大尝试次数 ({Config.MaxReconnectAttempts})"
            });
        }
    }

    /// <summary>
    /// 手动触发重连
    /// </summary>
    public async Task<bool> ForceReconnectAsync(RemoteHost host, string hostId)
    {
        _logger.Log(LogLevel.Info, $"手动触发重连：{host.Name}");
        LogReconnect($"手动触发重连：{host.Name}");

        try
        {
            var testResult = await _sshService.TestConnectionAsync(host);

            if (testResult.Success)
            {
                UpdateConnectionState(hostId, true, null);
                _logger.Log(LogLevel.Success, $"主机 {host.Name} 重连成功");
                LogReconnect($"重连成功");
                return true;
            }
            else
            {
                _logger.Log(LogLevel.Error, $"主机 {host.Name} 重连失败：{testResult.Message}");
                LogReconnect($"重连失败：{testResult.Message}");
                UpdateConnectionState(hostId, false, testResult.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, $"主机 {host.Name} 重连异常：{ex.Message}");
            LogReconnect($"重连异常：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 记录重连日志
    /// </summary>
    private void LogReconnect(string message)
    {
        ReconnectLog?.Invoke(this, new ReconnectLogEventArgs
        {
            Timestamp = DateTime.Now,
            Message = message
        });
    }

    /// <summary>
    /// 获取所有连接状态
    /// </summary>
    public List<ConnectionState> GetAllConnectionStates()
    {
        lock (_stateLock)
        {
            return new List<ConnectionState>(_connectionStates.Values);
        }
    }

    #region IDisposable 实现

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// 重连配置
/// </summary>
public class ReconnectConfig
{
    /// <summary>
    /// 最大重连次数（默认 3 次）
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// 重连间隔（秒，默认 5 秒）
    /// </summary>
    public int ReconnectIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// 心跳超时时间（分钟，默认 5 分钟）
    /// </summary>
    public int HeartbeatTimeoutMinutes { get; set; } = 5;
}

/// <summary>
/// 连接状态
/// </summary>
public class ConnectionState
{
    /// <summary>
    /// 主机信息
    /// </summary>
    public RemoteHost Host { get; set; } = null!;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// 重连尝试次数
    /// </summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; set; }
}

/// <summary>
/// 连接状态变更事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 主机 ID
    /// </summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 重连日志事件参数
/// </summary>
public class ReconnectLogEventArgs : EventArgs
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 日志消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
