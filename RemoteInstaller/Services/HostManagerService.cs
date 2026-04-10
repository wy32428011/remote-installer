using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 主机管理服务
/// 负责主机的添加、编辑、删除、连接测试和心跳检测
/// </summary>
public partial class HostManagerService : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly SshService _sshService;
    private readonly ILogger _logger;
    
    // 心跳检测相关
    private Timer? _heartbeatTimer;
    private readonly object _heartbeatLock = new();
    private const int HeartbeatIntervalMinutes = 5;

    /// <summary>
    /// 主机列表
    /// </summary>
    [ObservableProperty]
    private List<RemoteHost> _hosts = new();

    /// <summary>
    /// 选中的主机
    /// </summary>
    [ObservableProperty]
    private RemoteHost? _selectedHost;

    public HostManagerService(DatabaseService databaseService, SshService sshService, ILogger logger)
    {
        _databaseService = databaseService;
        _sshService = sshService;
        _logger = logger;
        
        LoadHosts();
        StartHeartbeat();
    }

    /// <summary>
    /// 加载所有主机
    /// </summary>
    public void LoadHosts()
    {
        try
        {
            Hosts = _databaseService.GetAllHosts();
        }
        catch (Exception ex)
        {
            _logger.Error($"加载主机列表失败：{ex.Message}");
            Hosts = new List<RemoteHost>();
        }
    }

    /// <summary>
    /// 添加主机
    /// </summary>
    public RemoteHost AddHost(RemoteHost host)
    {
        try
        {
            // 验证主机信息
            if (string.IsNullOrEmpty(host.Name))
            {
                throw new ArgumentException("主机名称不能为空");
            }

            if (string.IsNullOrEmpty(host.IpAddress))
            {
                throw new ArgumentException("IP 地址不能为空");
            }

            if (string.IsNullOrEmpty(host.Username))
            {
                throw new ArgumentException("用户名不能为空");
            }

            if (host.AuthType == AuthType.Password && string.IsNullOrEmpty(host.EncryptedPassword))
            {
                throw new ArgumentException("密码不能为空");
            }

            if (host.AuthType == AuthType.PrivateKey && string.IsNullOrEmpty(host.KeyPath))
            {
                throw new ArgumentException("密钥路径不能为空");
            }

            // 更新修改时间
            host.UpdateModifiedTime();

            // 保存到数据库
            _databaseService.SaveHost(host);

            // 重新加载列表
            LoadHosts();

            _logger.Success($"主机添加成功：{host.Name}");

            return host;
        }
        catch (Exception ex)
        {
            _logger.Error($"添加主机失败：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 编辑主机
    /// </summary>
    public void UpdateHost(RemoteHost host)
    {
        try
        {
            // 验证主机信息
            if (string.IsNullOrEmpty(host.Name))
            {
                throw new ArgumentException("主机名称不能为空");
            }

            if (string.IsNullOrEmpty(host.IpAddress))
            {
                throw new ArgumentException("IP 地址不能为空");
            }

            if (string.IsNullOrEmpty(host.Username))
            {
                throw new ArgumentException("用户名不能为空");
            }

            // 更新修改时间
            host.UpdateModifiedTime();

            // 保存到数据库
            _databaseService.SaveHost(host);

            // 重新加载列表
            LoadHosts();

            _logger.Success($"主机编辑成功：{host.Name}");
        }
        catch (Exception ex)
        {
            _logger.Error($"编辑主机失败：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 删除主机
    /// </summary>
    public void DeleteHost(string hostId)
    {
        try
        {
            // 从数据库删除
            _databaseService.DeleteHost(hostId);

            // 重新加载列表
            LoadHosts();

            _logger.Success($"主机删除成功");
        }
        catch (Exception ex)
        {
            _logger.Error($"删除主机失败：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 测试主机连接
    /// </summary>
    public async Task<TestConnectionResult> TestConnectionAsync(RemoteHost host, CancellationToken cancellationToken = default)
    {
        host.Status = HostStatus.Connecting;
        host.StatusMessage = "连接测试中...";

        try
        {
            var result = await _sshService.TestConnectionAsync(host, cancellationToken);

            if (result.Success)
            {
                host.Status = HostStatus.Online;
                host.StatusMessage = "连接成功";
                host.LastConnected = DateTime.Now;

                // 如果检测到不同的 OS 类型，更新
                if (result.DetectedOsType != host.OsType)
                {
                    host.OsType = result.DetectedOsType;
                    _databaseService.SaveHost(host);
                }

                // 更新最后连接时间
                _databaseService.UpdateHostHeartbeat(host.Id, DateTime.Now);

                _logger.Success($"连接测试成功：{host.Name}");
            }
            else
            {
                host.Status = HostStatus.Error;
                host.StatusMessage = result.Message;

                _logger.Error($"连接测试失败：{host.Name} - {result.Message}");
            }

            return new TestConnectionResult
            {
                Success = result.Success,
                Message = result.Message,
                DetectedOsType = result.DetectedOsType
            };
        }
        catch (OperationCanceledException)
        {
            host.Status = HostStatus.Offline;
            host.StatusMessage = "连接已取消";

            return new TestConnectionResult
            {
                Success = false,
                Message = "连接已取消",
                DetectedOsType = host.OsType
            };
        }
        catch (Exception ex)
        {
            host.Status = HostStatus.Error;
            host.StatusMessage = ex.Message;

            _logger.Error($"连接测试异常：{ex.Message}");

            return new TestConnectionResult
            {
                Success = false,
                Message = ex.Message,
                DetectedOsType = host.OsType
            };
        }
    }

    /// <summary>
    /// 批量测试连接
    /// </summary>
    public async Task<Dictionary<string, TestConnectionResult>> TestConnectionsAsync(
        List<RemoteHost> hosts, 
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TestConnectionResult>();
        
        foreach (var host in hosts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            
            var result = await TestConnectionAsync(host, cancellationToken);
            results[host.Id] = result;
        }
        
        return results;
    }

    /// <summary>
    /// 启动心跳检测
    /// </summary>
    private void StartHeartbeat()
    {
        lock (_heartbeatLock)
        {
            if (_heartbeatTimer != null)
            {
                return;
            }

            _heartbeatTimer = new Timer(async _ => await HeartbeatCallback(), null,
                TimeSpan.FromMinutes(HeartbeatIntervalMinutes), TimeSpan.FromMinutes(HeartbeatIntervalMinutes));
        }
    }

    /// <summary>
    /// 心跳检测回调
    /// </summary>
    private async Task HeartbeatCallback()
    {
        try
        {
            LoadHosts();

            var offlineThresholdMinutes = HeartbeatIntervalMinutes * 2;
            var offlineHostIds = _databaseService.GetOfflineHostIds(offlineThresholdMinutes);

            foreach (var hostId in offlineHostIds)
            {
                var host = Hosts.FirstOrDefault(h => h.Id == hostId);
                if (host != null && host.Status == HostStatus.Online)
                {
                    // 尝试重新连接验证
                    try
                    {
                        var result = await _sshService.TestConnectionAsync(host, CancellationToken.None);
                        if (!result.Success)
                        {
                            host.Status = HostStatus.Offline;
                            host.StatusMessage = "心跳超时";
                        }
                    }
                    catch (Exception ex)
                    {
                        host.Status = HostStatus.Offline;
                        host.StatusMessage = "心跳超时";
                        _logger.Warning($"主机 {host.Name} 心跳检测失败：{ex.GetType().Name} - {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"心跳检测发生异常：{ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// 停止心跳检测
    /// </summary>
    public void StopHeartbeat()
    {
        lock (_heartbeatLock)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }
    }

    /// <summary>
    /// 根据 ID 获取主机
    /// </summary>
    public RemoteHost? GetHostById(string id)
    {
        return Hosts.FirstOrDefault(h => h.Id == id);
    }

    /// <summary>
    /// 获取在线主机列表
    /// </summary>
    public List<RemoteHost> GetOnlineHosts()
    {
        return Hosts.Where(h => h.Status == HostStatus.Online).ToList();
    }

    /// <summary>
    /// 获取离线主机列表
    /// </summary>
    public List<RemoteHost> GetOfflineHosts()
    {
        return Hosts.Where(h => h.Status == HostStatus.Offline || h.Status == HostStatus.Error).ToList();
    }

    /// <summary>
    /// 按分组获取主机
    /// </summary>
    public Dictionary<string, List<RemoteHost>> GetHostsByGroup()
    {
        return Hosts.GroupBy(h => h.GroupName ?? "未分组")
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}

/// <summary>
/// 连接测试结果
/// </summary>
public class TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OperatingSystemType DetectedOsType { get; set; }
}
