using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 远程目录项
/// </summary>
public partial class DirectoryItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _remotePath = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _children = new();
}
