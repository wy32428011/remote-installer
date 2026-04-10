using System.Collections.ObjectModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 应用配置文件根对象
/// </summary>
public class ApplicationConfiguration
{
    public ObservableCollection<ApplicationConfig> Applications { get; set; } = new();
}

/// <summary>
/// 应用配置
/// </summary>
public class ApplicationConfig
{
    /// <summary>
    /// 应用 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 支持的操作系统
    /// </summary>
    public ObservableCollection<string> OsSupport { get; set; } = new();

    /// <summary>
    /// 版本列表
    /// </summary>
    public ObservableCollection<VersionConfig> Versions { get; set; } = new();

    /// <summary>
    /// 脚本配置
    /// </summary>
    public ScriptConfig Scripts { get; set; } = new();
}

/// <summary>
/// 版本配置
/// </summary>
public class VersionConfig
{
    /// <summary>
    /// 版本号
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Windows 安装包 URL
    /// </summary>
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Linux 安装包 URL
    /// </summary>
    public string LinuxPackageUrl { get; set; } = string.Empty;

    /// <summary>
    /// 校验和
    /// </summary>
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// 参数列表
    /// </summary>
    public ObservableCollection<ParameterConfig> Parameters { get; set; } = new();
}

/// <summary>
/// 参数配置
/// </summary>
public class ParameterConfig
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 默认值
    /// </summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 脚本配置
/// </summary>
public class ScriptConfig
{
    /// <summary>
    /// 安装脚本
    /// </summary>
    public ScriptUrls Install { get; set; } = new();

    /// <summary>
    /// 卸载脚本
    /// </summary>
    public ScriptUrls Uninstall { get; set; } = new();

    /// <summary>
    /// 检测脚本
    /// </summary>
    public ScriptUrls Detect { get; set; } = new();
}

/// <summary>
/// 脚本 URL 配置
/// </summary>
public class ScriptUrls
{
    /// <summary>
    /// Linux 脚本
    /// </summary>
    public string Linux { get; set; } = string.Empty;

    /// <summary>
    /// Windows 脚本
    /// </summary>
    public string Windows { get; set; } = string.Empty;
}
