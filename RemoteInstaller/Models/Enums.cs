namespace RemoteInstaller.Models;

/// <summary>
/// 操作系统类型
/// </summary>
public enum OperatingSystemType
{
    /// <summary>
    /// Windows Server
    /// </summary>
    Windows,

    /// <summary>
    /// Linux (通用)
    /// </summary>
    Linux,

    /// <summary>
    /// CentOS
    /// </summary>
    CentOS,

    /// <summary>
    /// Ubuntu
    /// </summary>
    Ubuntu
}

/// <summary>
/// 主机连接状态
/// </summary>
public enum HostStatus
{
    /// <summary>
    /// 未知
    /// </summary>
    Unknown,
    
    /// <summary>
    /// 在线
    /// </summary>
    Online,
    
    /// <summary>
    /// 离线
    /// </summary>
    Offline,
    
    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,
    
    /// <summary>
    /// 错误
    /// </summary>
    Error
}

/// <summary>
/// 认证方式
/// </summary>
public enum AuthType
{
    /// <summary>
    /// 密码认证
    /// </summary>
    Password,
    
    /// <summary>
    /// 私钥认证
    /// </summary>
    PrivateKey
}

/// <summary>
/// 应用安装状态
/// </summary>
public enum InstallStatus
{
    /// <summary>
    /// 未安装
    /// </summary>
    NotInstalled,
    
    /// <summary>
    /// 已安装
    /// </summary>
    Installed,
    
    /// <summary>
    /// 运行中
    /// </summary>
    Running,
    
    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,
    
    /// <summary>
    /// 安装中
    /// </summary>
    Installing,
    
    /// <summary>
    /// 卸载中
    /// </summary>
    Uninstalling
}

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,
    
    /// <summary>
    /// 成功
    /// </summary>
    Success,
    
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    
    /// <summary>
    /// 错误
    /// </summary>
    Error
}

/// <summary>
/// 安装阶段
/// </summary>
public enum InstallStage
{
    /// <summary>
    /// 准备中
    /// </summary>
    Preparing,
    
    /// <summary>
    /// 连接服务器
    /// </summary>
    Connecting,
    
    /// <summary>
    /// 上传文件
    /// </summary>
    Uploading,
    
    /// <summary>
    /// 解压文件
    /// </summary>
    Extracting,
    
    /// <summary>
    /// 配置参数
    /// </summary>
    Configuring,
    
    /// <summary>
    /// 执行安装
    /// </summary>
    Installing,
    
    /// <summary>
    /// 启动服务
    /// </summary>
    Starting,
    
    /// <summary>
    /// 验证安装
    /// </summary>
    Verifying,
    
    /// <summary>
    /// 完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled
}

/// <summary>
/// 任务状态
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// 等待中
    /// </summary>
    Pending,
    
    /// <summary>
    /// 运行中
    /// </summary>
    Running,
    
    /// <summary>
    /// 暂停中
    /// </summary>
    Paused,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 已失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled
}
