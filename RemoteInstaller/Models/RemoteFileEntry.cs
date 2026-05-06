namespace RemoteInstaller.Models;

/// <summary>
/// 远程文件条目。
/// </summary>
public class RemoteFileEntry
{
    /// <summary>
    /// 文件或目录名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 远程完整路径。
    /// </summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否为目录。
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// 文件大小（字节）。
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 最后修改时间。
    /// </summary>
    public DateTimeOffset? ModifiedTime { get; set; }

    /// <summary>
    /// 权限字符串。
    /// </summary>
    public string Permissions { get; set; } = string.Empty;
}
