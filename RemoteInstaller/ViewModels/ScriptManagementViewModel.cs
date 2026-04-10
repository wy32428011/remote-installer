using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 脚本文件项
/// </summary>
public partial class ScriptFileItem : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _remotePath = string.Empty;
}

/// <summary>
/// 通用脚本管理 ViewModel
/// 支持上传脚本文件夹、编辑脚本、保存修改
/// </summary>
public partial class ScriptManagementViewModel : ObservableObject
{
    private readonly CustomApplicationService _customApplicationService;
    private readonly RemoteHost _host;
    private CancellationTokenSource? _operationCts;

    [ObservableProperty]
    private string _windowTitle;

    [ObservableProperty]
    private string _hostDisplay;

    [ObservableProperty]
    private string _localScriptFolder = string.Empty;

    [ObservableProperty]
    private string _remoteScriptDirectory = "/opt/scripts";

    [ObservableProperty]
    private string _localScriptFile = string.Empty;

    [ObservableProperty]
    private string _remoteScriptFileName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ScriptFileItem> _scriptFiles = new();

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _directoryItems = new();

    [ObservableProperty]
    private ScriptFileItem? _selectedScriptFile;

    [ObservableProperty]
    private string _scriptContent = string.Empty;

    [ObservableProperty]
    private string _originalScriptContent = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private string _statusMessage = "请选择脚本文件夹并上传";

    [ObservableProperty]
    private string _activityLog = string.Empty;

    public bool HasSelectedServer { get; }

    public ScriptManagementViewModel(
        CustomApplicationService customApplicationService,
        RemoteHost host)
    {
        _customApplicationService = customApplicationService;
        _host = host;

        WindowTitle = $"脚本管理 - {host.Name}";
        HostDisplay = $"{host.Name} ({host.IpAddress}:{host.Port})";
        HasSelectedServer = true;
    }

    partial void OnRemoteScriptDirectoryChanged(string value)
    {
        UploadCommand.NotifyCanExecuteChanged();
        RefreshListCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedScriptFileChanged(ScriptFileItem? value)
    {
        if (value != null)
        {
            _ = LoadScriptContentAsync(value.RemotePath);
        }
        else
        {
            ScriptContent = string.Empty;
            OriginalScriptContent = string.Empty;
            HasChanges = false;
        }
    }

    partial void OnScriptContentChanged(string value)
    {
        HasChanges = value != OriginalScriptContent;
        SaveScriptCommand.NotifyCanExecuteChanged();
    }

    partial void OnLocalScriptFileChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
        {
            RemoteScriptFileName = Path.GetFileName(value);
        }
        UploadSingleCommand.NotifyCanExecuteChanged();
    }

    partial void OnRemoteScriptFileNameChanged(string value)
    {
        UploadSingleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择脚本文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            LocalScriptFolder = dialog.FolderName;
            StatusMessage = $"已选择: {dialog.FolderName}";
            UploadCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        await RunBusyOperationAsync(
            "正在上传脚本文件夹...",
            async token =>
            {
                UploadProgress = 0;
                AppendActivity($"上传来源: {LocalScriptFolder}");
                AppendActivity($"远程目录: {RemoteScriptDirectory}");

                // 先创建远程目录
                var mkdirCmd = $"mkdir -p {RemoteScriptDirectory}";
                await _customApplicationService.StartApplicationAsync(_host, mkdirCmd, token);

                var remoteTarget = await _customApplicationService.UploadApplicationAsync(
                    _host,
                    LocalScriptFolder,
                    RemoteScriptDirectory,
                    preserveTopLevelDirectory: false,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"上传完成: {remoteTarget}", LogLevel.Success);
                StatusMessage = "上传完成，正在加载脚本列表...";

                // 加载脚本列表
                await LoadScriptListAsync();
            },
            "上传失败");
    }

    private bool CanUpload() => !IsBusy && !string.IsNullOrWhiteSpace(LocalScriptFolder) && !string.IsNullOrWhiteSpace(RemoteScriptDirectory);

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择脚本文件",
            Filter = "脚本文件 (*.sh;*.py;*.ps1;*.bat;*.cmd)|*.sh;*.py;*.ps1;*.bat;*.cmd|所有文件 (*.*)|*.*",
            FilterIndex = 1,
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            LocalScriptFile = dialog.FileName;
            StatusMessage = $"已选择: {dialog.FileName}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUploadSingle))]
    private async Task UploadSingleAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteScriptFileName))
        {
            MessageBox.Show("请输入远程文件名", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunBusyOperationAsync(
            "正在上传脚本...",
            async token =>
            {
                UploadProgress = 0;
                AppendActivity($"上传单个脚本: {LocalScriptFile}");
                AppendActivity($"远程文件名: {RemoteScriptFileName}");

                // 先创建远程目录
                var mkdirCmd = $"mkdir -p {RemoteScriptDirectory}";
                await _customApplicationService.StartApplicationAsync(_host, mkdirCmd, token);

                // 上传到远程目录
                var remoteTarget = Path.Combine(RemoteScriptDirectory, RemoteScriptFileName).Replace('\\', '/');
                await _customApplicationService.UploadFileAsync(
                    _host,
                    LocalScriptFile,
                    remoteTarget,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"上传完成: {remoteTarget}", LogLevel.Success);
                StatusMessage = "上传完成";

                // 刷新列表
                await LoadScriptListAsync();
            },
            "上传失败");
    }

    private bool CanUploadSingle() => !IsBusy && !string.IsNullOrWhiteSpace(LocalScriptFile) && !string.IsNullOrWhiteSpace(RemoteScriptDirectory);

    [RelayCommand]
    private async Task RefreshListAsync()
    {
        if (IsBusy) return;
        await LoadScriptListAsync();
    }

    [RelayCommand]
    private async Task LoadDirectoryAsync()
    {
        if (IsBusy) return;

        await RunBusyOperationAsync(
            "正在加载目录结构...",
            async token =>
            {
                DirectoryItems.Clear();

                // 获取目录下的所有文件和文件夹
                var listCmd = $"ls -laP {RemoteScriptDirectory} 2>/dev/null";
                var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, token);

                if (string.IsNullOrWhiteSpace(result))
                {
                    StatusMessage = "目录为空或不存在";
                    return;
                }

                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("total") || trimmed.StartsWith("d"))
                    {
                        // 跳过 total 行和目录本身的条目
                        continue;
                    }

                    // 解析 ls -la 输出
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        var name = string.Join(" ", parts.Skip(8));
                        if (name == "." || name == "..") continue;

                        var isDir = parts[0].StartsWith("d");
                        var remotePath = RemoteScriptDirectory.TrimEnd('/') + "/" + name;
                        DirectoryItems.Add(new DirectoryItem
                        {
                            Name = name,
                            RemotePath = remotePath,
                            IsDirectory = isDir
                        });
                    }
                }

                AppendActivity($"目录加载完成，共 {DirectoryItems.Count} 项", LogLevel.Success);
                StatusMessage = $"目录包含 {DirectoryItems.Count} 项";
            },
            "加载目录失败");
    }

    private async Task LoadScriptListAsync()
    {
        try
        {
            AppendActivity($"加载脚本列表: {RemoteScriptDirectory}");

            // 使用 find 命令获取目录下的所有文件
            var listCmd = $"find {RemoteScriptDirectory} -type f 2>/dev/null | sort";
            var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, CancellationToken.None);

            ScriptFiles.Clear();

            if (!string.IsNullOrWhiteSpace(result))
            {
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        var fileName = Path.GetFileName(trimmed);
                        ScriptFiles.Add(new ScriptFileItem
                        {
                            FileName = fileName,
                            RemotePath = trimmed
                        });
                    }
                }
            }

            AppendActivity($"共 {ScriptFiles.Count} 个文件", LogLevel.Success);
            StatusMessage = $"共 {ScriptFiles.Count} 个脚本";
        }
        catch (Exception ex)
        {
            AppendActivity($"加载脚本列表失败: {ex.Message}", LogLevel.Error);
            StatusMessage = "加载脚本列表失败";
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadScript))]
    private async Task LoadScriptContentAsync(string? remotePath = null)
    {
        var path = remotePath ?? SelectedScriptFile?.RemotePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        await RunBusyOperationAsync(
            "正在加载脚本...",
            async token =>
            {
                AppendActivity($"加载脚本: {path}");
                ScriptContent = await _customApplicationService.LoadConfigContentAsync(_host, path, token);
                OriginalScriptContent = ScriptContent;
                HasChanges = false;
                AppendActivity("脚本加载完成", LogLevel.Success);
                StatusMessage = $"已加载: {Path.GetFileName(path)}";
            },
            "加载失败");
    }

    private bool CanLoadScript() => !IsBusy && SelectedScriptFile != null;

    [RelayCommand(CanExecute = nameof(CanSaveScript))]
    private async Task SaveScriptAsync()
    {
        if (SelectedScriptFile == null) return;

        await RunBusyOperationAsync(
            "正在保存脚本...",
            async token =>
            {
                AppendActivity($"保存脚本: {SelectedScriptFile.RemotePath}");
                await _customApplicationService.SaveConfigContentAsync(_host, SelectedScriptFile.RemotePath, ScriptContent, token);
                OriginalScriptContent = ScriptContent;
                HasChanges = false;
                AppendActivity("脚本保存完成", LogLevel.Success);
                StatusMessage = "脚本已保存";
            },
            "保存失败");
    }

    private bool CanSaveScript() => !IsBusy && HasChanges && SelectedScriptFile != null;

    private async Task RunBusyOperationAsync(string busyMessage, Func<CancellationToken, Task> action, string failureMessage)
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = busyMessage;
        _operationCts = new CancellationTokenSource();

        try
        {
            await action(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "任务已取消";
            AppendActivity("任务已取消。", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{failureMessage}: {ex.Message}";
            AppendActivity($"{failureMessage}: {ex.Message}", LogLevel.Error);
            MessageBox.Show($"{failureMessage}: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private readonly StringBuilder _activityBuilder = new();

    private void AppendActivity(string message, LogLevel level = LogLevel.Info)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_activityBuilder.Length > 0)
        {
            _activityBuilder.AppendLine();
        }
        _activityBuilder.Append(line);
        ActivityLog = _activityBuilder.ToString();
    }
}
