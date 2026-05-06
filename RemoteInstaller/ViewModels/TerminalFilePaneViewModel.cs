using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 终端右侧文件栏 ViewModel。
/// 负责 SFTP 能力探测、目录树加载与常用文件操作。
/// </summary>
public partial class TerminalFilePaneViewModel : ObservableObject
{
    private readonly SshService _sshService;
    private readonly RemoteHost _remoteHost;

    /// <summary>
    /// 根目录显示文案。
    /// </summary>
    private const string RootLabel = "远程文件";

    /// <summary>
    /// 当前是否可用 SFTP。
    /// </summary>
    [ObservableProperty]
    private bool _isSftpAvailable;

    /// <summary>
    /// 当前是否正在执行文件操作。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 当前目录路径。
    /// </summary>
    [ObservableProperty]
    private string _currentRemotePath = "~";

    /// <summary>
    /// 路径栏输入值。
    /// </summary>
    [ObservableProperty]
    private string _pathInput = "~";

    /// <summary>
    /// 当前状态文本。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "文件侧栏未初始化";

    /// <summary>
    /// 顶层树节点集合。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _directoryItems = new();

    /// <summary>
    /// 当前选中的节点。
    /// </summary>
    [ObservableProperty]
    private DirectoryItem? _selectedItem;

    /// <summary>
    /// 当前下载 / 上传等进度文本。
    /// </summary>
    [ObservableProperty]
    private string _operationMessage = string.Empty;

    /// <summary>
    /// 是否已经完成过初始化。
    /// </summary>
    [ObservableProperty]
    private bool _isInitialized;

    /// <summary>
    /// 当前主机展示文本。
    /// </summary>
    public string HostDisplay => $"{_remoteHost.Name} ({_remoteHost.IpAddress})";

    /// <summary>
    /// 是否已选中条目。
    /// </summary>
    public bool HasSelection => SelectedItem != null;

    /// <summary>
    /// 当前选中条目是否可下载。
    /// </summary>
    public bool CanDownloadSelected =>
        !IsBusy &&
        SelectedItem is { IsDirectory: false, IsPlaceholder: false, IsVirtualRoot: false };

    /// <summary>
    /// 当前选中条目是否可重命名。
    /// </summary>
    public bool CanRenameSelected =>
        !IsBusy &&
        SelectedItem is { IsPlaceholder: false, IsVirtualRoot: false };

    /// <summary>
    /// 当前选中条目是否可删除。
    /// </summary>
    public bool CanDeleteSelected =>
        !IsBusy &&
        SelectedItem is { IsPlaceholder: false, IsVirtualRoot: false };

    /// <summary>
    /// 当前目录是否允许上传。
    /// </summary>
    public bool CanUploadToCurrentDirectory => !IsBusy && IsSftpAvailable && !string.IsNullOrWhiteSpace(CurrentRemotePath);

    /// <summary>
    /// 当前目录是否允许新建目录。
    /// </summary>
    public bool CanCreateDirectoryInCurrentPath => !IsBusy && IsSftpAvailable && !string.IsNullOrWhiteSpace(CurrentRemotePath);

    public TerminalFilePaneViewModel(SshService sshService, RemoteHost remoteHost)
    {
        _sshService = sshService;
        _remoteHost = remoteHost;
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        LoadDirectoryCommand.NotifyCanExecuteChanged();
        LoadChildrenCommand.NotifyCanExecuteChanged();
        GoToPathCommand.NotifyCanExecuteChanged();
        OpenSelectedDirectoryCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CreateDirectoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDownloadSelected));
        OnPropertyChanged(nameof(CanRenameSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanUploadToCurrentDirectory));
        OnPropertyChanged(nameof(CanCreateDirectoryInCurrentPath));
    }

    partial void OnIsSftpAvailableChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        LoadDirectoryCommand.NotifyCanExecuteChanged();
        LoadChildrenCommand.NotifyCanExecuteChanged();
        GoToPathCommand.NotifyCanExecuteChanged();
        OpenSelectedDirectoryCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CreateDirectoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUploadToCurrentDirectory));
        OnPropertyChanged(nameof(CanCreateDirectoryInCurrentPath));
    }

    partial void OnSelectedItemChanged(DirectoryItem? value)
    {
        OpenSelectedDirectoryCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanDownloadSelected));
        OnPropertyChanged(nameof(CanRenameSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));

        if (value is { IsDirectory: true, IsPlaceholder: false })
        {
            PathInput = value.RemotePath;
        }
    }

    partial void OnCurrentRemotePathChanged(string value)
    {
        PathInput = value;
        GoToPathCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        CreateDirectoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUploadToCurrentDirectory));
        OnPropertyChanged(nameof(CanCreateDirectoryInCurrentPath));
    }

    partial void OnPathInputChanged(string value)
    {
        GoToPathCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 初始化文件栏。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RunBusyOperationAsync(
            "正在初始化文件侧栏...",
            async token =>
            {
                IsSftpAvailable = await _sshService.IsSftpAvailableAsync(token);
                IsInitialized = true;

                if (!IsSftpAvailable)
                {
                    DirectoryItems.Clear();
                    SelectedItem = null;
                    OperationMessage = string.Empty;
                    StatusMessage = "当前主机未提供可用的 SFTP 文件能力，终端仍可正常使用。";
                    return;
                }

                await LoadDirectoryCoreAsync(CurrentRemotePath, token, preserveSelectionPath: null);
                StatusMessage = $"文件侧栏已连接到 {HostDisplay}";
            },
            "初始化文件侧栏失败",
            showErrorDialog: false,
            cancellationToken);
    }

    /// <summary>
    /// 重置为不可用状态。
    /// </summary>
    public void ResetUnavailable(string? message = null)
    {
        IsSftpAvailable = false;
        IsInitialized = false;
        IsBusy = false;
        CurrentRemotePath = "~";
        PathInput = "~";
        OperationMessage = string.Empty;
        DirectoryItems.Clear();
        SelectedItem = null;
        StatusMessage = string.IsNullOrWhiteSpace(message)
            ? "文件侧栏未连接"
            : message;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await RunBusyOperationAsync(
            "正在刷新文件侧栏...",
            async token =>
            {
                var selectionPath = SelectedItem?.RemotePath;
                await LoadDirectoryCoreAsync(CurrentRemotePath, token, selectionPath);
                StatusMessage = $"已刷新 {CurrentRemotePath}";
            },
            "刷新目录失败");
    }

    [RelayCommand(CanExecute = nameof(CanLoadDirectory))]
    private async Task LoadDirectoryAsync(string? remotePath = null)
    {
        await RunBusyOperationAsync(
            "正在加载目录...",
            async token =>
            {
                await LoadDirectoryCoreAsync(remotePath ?? CurrentRemotePath, token, preserveSelectionPath: null);
                StatusMessage = $"当前目录：{CurrentRemotePath}";
            },
            "加载目录失败");
    }

    [RelayCommand(CanExecute = nameof(CanGoToPath))]
    private async Task GoToPathAsync()
    {
        var targetPath = PathInput;
        await RunBusyOperationAsync(
            "正在打开远程目录...",
            async token =>
            {
                await LoadDirectoryCoreAsync(targetPath, token, preserveSelectionPath: null);
                StatusMessage = $"当前目录：{CurrentRemotePath}";
            },
            "打开远程目录失败");
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedDirectory))]
    private async Task OpenSelectedDirectoryAsync()
    {
        if (SelectedItem is not { IsDirectory: true, IsPlaceholder: false } selectedDirectory)
        {
            return;
        }

        var targetPath = selectedDirectory.RemotePath;
        await RunBusyOperationAsync(
            $"正在进入 {selectedDirectory.Name}...",
            async token =>
            {
                await LoadDirectoryCoreAsync(targetPath, token, preserveSelectionPath: null);
                StatusMessage = $"当前目录：{CurrentRemotePath}";
            },
            "进入目录失败");
    }

    [RelayCommand(CanExecute = nameof(CanLoadChildren))]
    private async Task LoadChildrenAsync(DirectoryItem? parent)
    {
        if (parent == null || !parent.IsDirectory)
        {
            return;
        }

        await RunBusyOperationAsync(
            $"正在展开 {parent.Name}...",
            async token =>
            {
                var entries = await _sshService.ListDirectoryAsync(parent.RemotePath, token);
                ApplyChildren(parent, entries);
                parent.IsLoaded = true;
                parent.IsExpanded = true;
                CurrentRemotePath = parent.RemotePath;
                StatusMessage = $"已加载 {parent.RemotePath}";
            },
            "加载子目录失败",
            showErrorDialog: false);
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        var choice = MessageBox.Show(
            "是否上传整个文件夹？\n选择“是”上传文件夹，选择“否”上传文件。",
            "选择上传类型",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        if (choice == MessageBoxResult.Yes)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "选择要上传的文件夹",
                Multiselect = false
            };

            if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
            {
                return;
            }

            var folderPath = folderDialog.FolderName;
            var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetPath = CombineRemotePath(CurrentRemotePath, folderName);

            await RunBusyOperationAsync(
                "正在上传文件夹...",
                async token =>
                {
                    await _sshService.UploadDirectoryAsync(
                        folderPath,
                        targetPath,
                        progress => OperationMessage = $"正在上传目录 {folderName}... {progress}%",
                        token);

                    OperationMessage = $"目录上传完成：{folderName}";
                    await LoadDirectoryCoreAsync(CurrentRemotePath, token, preserveSelectionPath: targetPath);
                    StatusMessage = $"已上传目录 {folderName}";
                },
                "上传目录失败");

            return;
        }

        var fileDialog = new OpenFileDialog
        {
            Title = "选择要上传的文件",
            Filter = "所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (fileDialog.ShowDialog() != true || fileDialog.FileNames.Length == 0)
        {
            return;
        }

        await RunBusyOperationAsync(
            "正在上传文件...",
            async token =>
            {
                string? lastUploadedPath = null;
                OperationMessage = string.Empty;

                foreach (var localPath in fileDialog.FileNames)
                {
                    token.ThrowIfCancellationRequested();

                    var localName = Path.GetFileName(localPath);
                    var targetPath = CombineRemotePath(CurrentRemotePath, localName);
                    lastUploadedPath = targetPath;
                    OperationMessage = $"正在上传 {localName}...";

                    await _sshService.UploadFileAsync(
                        localPath,
                        targetPath,
                        progress => OperationMessage = $"正在上传 {localName}... {progress}%",
                        token);
                }

                OperationMessage = $"已上传 {fileDialog.FileNames.Length} 个文件";
                await LoadDirectoryCoreAsync(CurrentRemotePath, token, preserveSelectionPath: lastUploadedPath);
                StatusMessage = $"上传完成：{fileDialog.FileNames.Length} 个文件";
            },
            "上传文件失败");
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (SelectedItem == null || SelectedItem.IsDirectory)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存远程文件",
            FileName = SelectedItem.Name,
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var selectionPath = SelectedItem.RemotePath;
        await RunBusyOperationAsync(
            "正在下载文件...",
            async token =>
            {
                OperationMessage = $"正在下载 {SelectedItem.Name}...";
                await _sshService.DownloadFileAsync(
                    SelectedItem.RemotePath,
                    dialog.FileName,
                    progress => OperationMessage = $"正在下载 {SelectedItem!.Name}... {progress}%",
                    token);

                OperationMessage = $"下载完成：{dialog.FileName}";
                await RestoreSelectionAsync(selectionPath, token);
                StatusMessage = $"已下载到 {dialog.FileName}";
            },
            "下载文件失败");
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        if (SelectedItem == null)
        {
            return;
        }

        if (SelectedItem.IsVirtualRoot || SelectedItem.IsPlaceholder)
        {
            return;
        }

        var oldName = SelectedItem.Name;
        var newName = PromptForText($"请输入 {oldName} 的新名称：", "重命名远程条目", oldName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        newName = newName.Trim();
        if (string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            return;
        }

        if (newName.Contains('/') || newName.Contains('\\'))
        {
            MessageBox.Show("名称中不能包含路径分隔符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sourcePath = SelectedItem.RemotePath;
        var targetPath = BuildSiblingPath(sourcePath, newName);
        await RunBusyOperationAsync(
            "正在重命名条目...",
            async token =>
            {
                await _sshService.MoveAsync(sourcePath, targetPath, token);
                await LoadDirectoryCoreAsync(CurrentRemotePath, token, targetPath);
                StatusMessage = $"已重命名为 {newName}";
                OperationMessage = string.Empty;
            },
            "重命名失败");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedItem == null)
        {
            return;
        }

        if (SelectedItem.IsVirtualRoot || SelectedItem.IsPlaceholder)
        {
            return;
        }

        var target = SelectedItem;
        var isDirectory = target.IsDirectory;
        var prompt = isDirectory
            ? $"确定要递归删除目录“{target.Name}”吗？\n此操作会删除目录下的所有文件，无法撤销。"
            : $"确定要删除文件“{target.Name}”吗？";

        var result = MessageBox.Show(
            prompt,
            isDirectory ? "确认删除目录" : "确认删除文件",
            MessageBoxButton.YesNo,
            isDirectory ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var parentPath = GetParentPath(target.RemotePath) ?? CurrentRemotePath;
        await RunBusyOperationAsync(
            "正在删除条目...",
            async token =>
            {
                if (isDirectory)
                {
                    await _sshService.DeleteDirectoryAsync(target.RemotePath, recursive: true, token);
                }
                else
                {
                    await _sshService.DeleteFileAsync(target.RemotePath, token);
                }

                await LoadDirectoryCoreAsync(parentPath, token, preserveSelectionPath: null);
                StatusMessage = isDirectory
                    ? $"已删除目录 {target.Name}"
                    : $"已删除文件 {target.Name}";
                OperationMessage = string.Empty;
            },
            "删除失败");
    }

    [RelayCommand(CanExecute = nameof(CanCreateDirectory))]
    private async Task CreateDirectoryAsync()
    {
        var directoryName = PromptForText("请输入新目录名称：", "新建远程目录", string.Empty);
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return;
        }

        directoryName = directoryName.Trim();
        if (directoryName.Contains('/') || directoryName.Contains('\\'))
        {
            MessageBox.Show("目录名称中不能包含路径分隔符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetPath = CombineRemotePath(CurrentRemotePath, directoryName);
        await RunBusyOperationAsync(
            "正在新建目录...",
            async token =>
            {
                await _sshService.MakeDirectoryAsync(targetPath, token);
                await LoadDirectoryCoreAsync(CurrentRemotePath, token, targetPath);
                StatusMessage = $"已创建目录 {directoryName}";
                OperationMessage = string.Empty;
            },
            "创建目录失败");
    }

    private bool CanRefresh() => !IsBusy && IsSftpAvailable;

    private bool CanLoadDirectory() => !IsBusy && IsSftpAvailable;

    private bool CanGoToPath() => !IsBusy && IsSftpAvailable && !string.IsNullOrWhiteSpace(PathInput);

    private bool CanOpenSelectedDirectory() =>
        !IsBusy &&
        IsSftpAvailable &&
        SelectedItem is { IsDirectory: true, IsPlaceholder: false };

    private bool CanLoadChildren(DirectoryItem? parent) => !IsBusy && IsSftpAvailable && parent != null && parent.IsDirectory && !parent.IsPlaceholder;

    private bool CanUpload() => CanUploadToCurrentDirectory;

    private bool CanDownload() => CanDownloadSelected;

    private bool CanRename() => CanRenameSelected;

    private bool CanDelete() => CanDeleteSelected;

    private bool CanCreateDirectory() => CanCreateDirectoryInCurrentPath;

    private async Task LoadDirectoryCoreAsync(string remotePath, CancellationToken cancellationToken, string? preserveSelectionPath)
    {
        var rootPath = await ResolveDirectoryPathAsync(remotePath, cancellationToken);
        var entries = await _sshService.ListDirectoryAsync(rootPath, cancellationToken);

        CurrentRemotePath = rootPath;
        DirectoryItems.Clear();

        var root = BuildDirectoryNode(RootLabel, rootPath, secondaryText: rootPath);
        root.IsVirtualRoot = true;
        root.IsLoaded = true;
        root.IsExpanded = true;
        ApplyChildren(root, entries);
        DirectoryItems.Add(root);

        if (!string.IsNullOrWhiteSpace(preserveSelectionPath))
        {
            await RestoreSelectionAsync(preserveSelectionPath, cancellationToken);
            return;
        }

        SelectedItem = root;
    }

    private async Task<string> ResolveDirectoryPathAsync(string remotePath, CancellationToken cancellationToken)
    {
        var normalizedInput = NormalizePath(remotePath);
        var entry = await _sshService.GetEntryAsync(normalizedInput, cancellationToken);
        if (entry == null)
        {
            throw new DirectoryNotFoundException($"远程目录不存在：{remotePath}");
        }

        if (!entry.IsDirectory)
        {
            throw new InvalidOperationException($"远程路径不是目录：{remotePath}");
        }

        return NormalizePath(entry.RemotePath);
    }

    private async Task RestoreSelectionAsync(string? targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        var normalizedTargetPath = NormalizePath(targetPath);
        foreach (var root in DirectoryItems)
        {
            var found = await FindAndExpandAsync(root, normalizedTargetPath, cancellationToken);
            if (found != null)
            {
                SelectedItem = found;
                found.IsSelected = true;
                return;
            }
        }
    }

    private async Task<DirectoryItem?> FindAndExpandAsync(DirectoryItem node, string targetPath, CancellationToken cancellationToken)
    {
        if (string.Equals(NormalizePath(node.RemotePath), targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        if (!node.IsDirectory)
        {
            return null;
        }

        if (!node.IsLoaded)
        {
            var children = await _sshService.ListDirectoryAsync(node.RemotePath, cancellationToken);
            ApplyChildren(node, children);
            node.IsLoaded = true;
        }

        foreach (var child in node.Children.Where(item => !item.IsPlaceholder))
        {
            var found = await FindAndExpandAsync(child, targetPath, cancellationToken);
            if (found != null)
            {
                node.IsExpanded = true;
                return found;
            }
        }

        return null;
    }

    private void ApplyChildren(DirectoryItem parent, IReadOnlyList<RemoteFileEntry> entries)
    {
        parent.Children.Clear();

        foreach (var entry in entries)
        {
            var item = CreateDirectoryItem(entry);
            if (item.IsDirectory)
            {
                item.Children.Add(CreatePlaceholderNode());
            }

            parent.Children.Add(item);
        }
    }

    private DirectoryItem CreateDirectoryItem(RemoteFileEntry entry)
    {
        var secondaryText = entry.IsDirectory
            ? FormatDirectorySecondaryText(entry)
            : FormatFileSecondaryText(entry);

        return new DirectoryItem
        {
            Name = entry.Name,
            RemotePath = entry.RemotePath,
            IsDirectory = entry.IsDirectory,
            IsLoaded = false,
            IsExpanded = false,
            IsSelected = false,
            IsPlaceholder = false,
            SecondaryText = secondaryText,
            TooltipText = BuildTooltip(entry)
        };
    }

    private static DirectoryItem BuildDirectoryNode(string name, string remotePath, string secondaryText)
    {
        return new DirectoryItem
        {
            Name = name,
            RemotePath = remotePath,
            IsDirectory = true,
            IsLoaded = false,
            IsExpanded = false,
            IsSelected = false,
            IsPlaceholder = false,
            SecondaryText = secondaryText,
            TooltipText = remotePath
        };
    }

    private static DirectoryItem CreatePlaceholderNode()
    {
        return new DirectoryItem
        {
            Name = "加载中...",
            IsPlaceholder = true,
            IsDirectory = false,
            RemotePath = string.Empty,
            SecondaryText = string.Empty,
            TooltipText = string.Empty
        };
    }

    private async Task RunBusyOperationAsync(
        string busyMessage,
        Func<CancellationToken, Task> action,
        string failureMessage,
        bool showErrorDialog = true,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        OperationMessage = string.Empty;
        StatusMessage = busyMessage;

        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "文件操作已取消";
            OperationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{failureMessage}: {ex.Message}";
            OperationMessage = string.Empty;

            if (showErrorDialog)
            {
                MessageBox.Show($"{failureMessage}: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildTooltip(RemoteFileEntry entry)
    {
        var lines = new List<string>
        {
            $"名称：{entry.Name}",
            $"路径：{entry.RemotePath}",
            $"类型：{(entry.IsDirectory ? "目录" : "文件")}",
            $"权限：{(string.IsNullOrWhiteSpace(entry.Permissions) ? "未知" : entry.Permissions)}"
        };

        if (!entry.IsDirectory)
        {
            lines.Add($"大小：{FormatFileSize(entry.Size)}");
        }

        if (entry.ModifiedTime.HasValue)
        {
            lines.Add($"修改时间：{entry.ModifiedTime.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDirectorySecondaryText(RemoteFileEntry entry)
    {
        return entry.ModifiedTime.HasValue
            ? $"目录 · {entry.ModifiedTime.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
            : "目录";
    }

    private static string FormatFileSecondaryText(RemoteFileEntry entry)
    {
        var size = FormatFileSize(entry.Size);
        return entry.ModifiedTime.HasValue
            ? $"{size} · {entry.ModifiedTime.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
            : size;
    }

    private static string FormatFileSize(long size)
    {
        var value = Math.Max(0, size);
        var units = new[] { "B", "KB", "MB", "GB" };
        var unitIndex = 0;
        var displayValue = (double)value;

        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return $"{displayValue:0.#} {units[unitIndex]}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
        }

        return string.IsNullOrWhiteSpace(normalized) ? "/" : normalized;
    }

    private static string CombineRemotePath(string basePath, string name)
    {
        var normalizedBasePath = NormalizePath(basePath);
        var normalizedName = NormalizePath(name).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return normalizedBasePath;
        }

        if (normalizedBasePath == "/")
        {
            return "/" + normalizedName;
        }

        return normalizedBasePath + "/" + normalizedName;
    }

    private static string BuildSiblingPath(string sourcePath, string name)
    {
        var parent = GetParentPath(sourcePath);
        return string.IsNullOrWhiteSpace(parent)
            ? NormalizePath(name)
            : CombineRemotePath(parent, name);
    }

    private static string? GetParentPath(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == "/")
        {
            return null;
        }

        var lastSlashIndex = normalized.LastIndexOf('/');
        if (lastSlashIndex <= 0)
        {
            return "/";
        }

        return normalized[..lastSlashIndex];
    }

    private static string? PromptForText(string prompt, string title, string defaultValue)
    {
        return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, defaultValue);
    }
}
