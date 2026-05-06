using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 远程主机模型
/// </summary>
public partial class RemoteHost : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 构造函数
    /// </summary>
    public RemoteHost()
    {
        // 确保 Id 总是有值
        if (string.IsNullOrEmpty(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }
    }
    
    [ObservableProperty]
    private string _name;
    
    [ObservableProperty]
    private string _ipAddress;
    
    [ObservableProperty]
    private int _port = 22;
    
    [ObservableProperty]
    private string _username;
    
    [ObservableProperty]
    private string _encryptedPassword;
    
    [ObservableProperty]
    private AuthType _authType = AuthType.Password;
    
    [ObservableProperty]
    private string _keyPath;
    
    [ObservableProperty]
    private string _encryptedKeyPassphrase;
    
    [ObservableProperty]
    private OperatingSystemType _osType = OperatingSystemType.CentOS;

    [ObservableProperty]
    private string _osVersion = string.Empty;

    [ObservableProperty]
    private string _cpuArchitecture = string.Empty;

    [ObservableProperty]
    private string _groupName;
    
    [ObservableProperty]
    private HostStatus _status = HostStatus.Unknown;
    
    [ObservableProperty]
    private string _statusMessage;
    
    [ObservableProperty]
    private DateTime _lastConnected;
    
    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;
    
    [ObservableProperty]
    private DateTime _updatedAt = DateTime.Now;

    /// <summary>
    /// 显示文本
    /// </summary>
    public string DisplayText => $"{Name} ({IpAddress})";

    /// <summary>
    /// OS 显示名称
    /// </summary>
    public string OsDisplayName => OsType switch
    {
        OperatingSystemType.Windows => "Windows Server",
        OperatingSystemType.CentOS => "CentOS",
        OperatingSystemType.Ubuntu => "Ubuntu",
        _ => "Unknown"
    };

    /// <summary>
    /// OS 图标（用于 UI 绑定）
    /// </summary>
    public string OsIcon => OsType switch
    {
        OperatingSystemType.Windows => "🪟",
        OperatingSystemType.CentOS => "🐧",
        OperatingSystemType.Ubuntu => "🐧",
        _ => "❓"
    };

    /// <summary>
    /// 状态颜色（用于 UI 绑定）
    /// </summary>
    public Brush StatusColor => Status switch
    {
        HostStatus.Online => Brushes.Green,
        HostStatus.Offline => Brushes.Red,
        HostStatus.Connecting => Brushes.Orange,
        HostStatus.Error => Brushes.Red,
        _ => Brushes.Gray
    };

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusDisplayText => Status switch
    {
        HostStatus.Online => "🟢 在线",
        HostStatus.Offline => "🔴 离线",
        HostStatus.Connecting => "🟡 连接中",
        HostStatus.Error => "⚠️ 错误",
        _ => "⚪ 未知"
    };

    /// <summary>
    /// 是否为 Linux 系统
    /// </summary>
    public bool IsLinux => OsType != OperatingSystemType.Windows;

    /// <summary>
    /// 更新修改时间
    /// </summary>
    public void UpdateModifiedTime()
    {
        UpdatedAt = DateTime.Now;
    }
}
