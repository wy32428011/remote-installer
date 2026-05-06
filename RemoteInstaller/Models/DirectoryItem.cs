using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 远程目录项
/// </summary>
public partial class DirectoryItem : ObservableObject
{
    /// <summary>
    /// 节点名称。
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 节点对应的远程完整路径。
    /// </summary>
    [ObservableProperty]
    private string _remotePath = string.Empty;

    /// <summary>
    /// 是否为目录节点。
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// 是否已在树上展开。
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 是否已经完成一次子节点加载。
    /// </summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>
    /// 是否为当前选中节点。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 是否为仅用于显示展开箭头的占位节点。
    /// </summary>
    [ObservableProperty]
    private bool _isPlaceholder;

    /// <summary>
    /// 是否为 UI 人工构造的虚拟根节点。
    /// </summary>
    [ObservableProperty]
    private bool _isVirtualRoot;

    /// <summary>
    /// 节点下方的摘要文本，例如大小、时间或目录说明。
    /// </summary>
    [ObservableProperty]
    private string _secondaryText = string.Empty;

    /// <summary>
    /// 节点悬浮提示文本，用于压缩展示更多元数据。
    /// </summary>
    [ObservableProperty]
    private string _tooltipText = string.Empty;

    /// <summary>
    /// 子节点集合。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _children = new();
}
