using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels.Shared.ConfigEditing;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 自定义应用部署 ViewModel（用于 WMS/WCS/FMS）
/// </summary>
public partial class GenericAppDeployViewModel : ObservableObject
{
    private readonly CustomApplicationService _customApplicationService;
    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly Action<string, LogLevel>? _logSink;
    private CancellationTokenSource? _operationCts;
    private bool _hasLoadedConfig;

    [ObservableProperty]
    private string _windowTitle;

    [ObservableProperty]
    private string _hostDisplay;

    [ObservableProperty]
    private string _applicationName = string.Empty;

    [ObservableProperty]
    private string _localSourcePath = string.Empty;

    [ObservableProperty]
    private string _remoteDirectory;

    [ObservableProperty]
    private string _startCommand = string.Empty;

    [ObservableProperty]
    private string _stopCommand = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConfigItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditConfigCommand))]
    private string _configFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    private string _configFileContent = string.Empty;

    public ConfigEditingSession EditingSession { get; } = new();

    public ObservableCollection<ConfigKeyValueItem> ConfigItems => EditingSession.ConfigItems;

    public ObservableCollection<YamlTreeNode> YamlTreeNodes => EditingSession.YamlTreeNodes;

    public bool IsYamlMode => EditingSession.IsYamlMode;

    public bool IsXmlMode => EditingSession.IsXmlMode;

    public bool IsRawTextMode => EditingSession.IsRawTextMode;

    public bool CanSwitchToStructuredMode => EditingSession.CanEnterStructuredMode;

    public bool CanSwitchToTextMode => EditingSession.CanEnterTextMode;

    public bool ShowKeyValueEditor => EditingSession.ShowKeyValueEditor;

    public bool ShowYamlEditor => EditingSession.ShowYamlEditor;

    public bool ShowTextEditor => EditingSession.ShowTextEditor;

    public bool ShowStructuredActions => EditingSession.ShowStructuredActions;

    public bool SupportsStructuredEditing => EditingSession.SupportsStructuredEditing;

    public ConfigKeyValueItem? SelectedConfigItem
    {
        get => EditingSession.SelectedItem;
        set => EditingSession.SelectedItem = value;
    }

    public YamlTreeNode? SelectedYamlNode
    {
        get => EditingSession.SelectedYamlNode;
        set => EditingSession.SelectedYamlNode = value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveStartScriptCommand))]
    private string _startScriptContent = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveStopScriptCommand))]
    private string _stopScriptContent = string.Empty;

    [ObservableProperty]
    private bool _isEditingStartScript;

    [ObservableProperty]
    private bool _isEditingStopScript;

    [ObservableProperty]
    private bool _preserveTopLevelDirectory;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _activityLog = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _appStatus = "未知";

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _directoryItems = new();

    public Action? CloseAction { get; set; }

    public string SourceSummary => GetSourceSummary();

    public string RemoteTargetPreview => GetRemoteTargetPreview();

    public bool HasActivity => !string.IsNullOrWhiteSpace(ActivityLog);

    public GenericAppDeployViewModel(
        CustomApplicationService customApplicationService,
        ConfigurationService configurationService,
        RemoteHost host,
        string appName,
        string remoteDir,
        string startCmd,
        string stopCmd,
        Action<string, LogLevel>? logSink = null)
    {
        _customApplicationService = customApplicationService;
        _configurationService = configurationService;
        _host = host;
        _logSink = logSink;

        _applicationName = appName;
        _remoteDirectory = remoteDir;
        _startCommand = startCmd;
        _stopCommand = stopCmd;

        EditingSession.PropertyChanged += OnEditingSessionPropertyChanged;

        WindowTitle = $"{appName} 部署 - {host.Name}";
        HostDisplay = $"{host.Name} ({host.IpAddress}:{host.Port})";
    }

    partial void OnLocalSourcePathChanged(string value)
    {
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(RemoteTargetPreview));

        if (string.IsNullOrWhiteSpace(ApplicationName) && !string.IsNullOrWhiteSpace(value))
        {
            ApplicationName = InferApplicationName(value);
        }

        // 手动通知命令重新评估 CanExecute
        UploadOnlyCommand.NotifyCanExecuteChanged();
        UploadAndStartCommand.NotifyCanExecuteChanged();
    }

    partial void OnRemoteDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteTargetPreview));
        UploadOnlyCommand.NotifyCanExecuteChanged();
        UploadAndStartCommand.NotifyCanExecuteChanged();
        LoadDirectoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnStartCommandChanged(string value)
    {
        UploadOnlyCommand.NotifyCanExecuteChanged();
        UploadAndStartCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreserveTopLevelDirectoryChanged(bool value)
    {
        OnPropertyChanged(nameof(RemoteTargetPreview));
    }

    partial void OnIsBusyChanged(bool value)
    {
        UploadOnlyCommand.NotifyCanExecuteChanged();
        UploadAndStartCommand.NotifyCanExecuteChanged();
        StartApplicationCommand.NotifyCanExecuteChanged();
        EditConfigCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
        LoadStartScriptCommand.NotifyCanExecuteChanged();
        LoadStopScriptCommand.NotifyCanExecuteChanged();
        SaveStartScriptCommand.NotifyCanExecuteChanged();
        SaveStopScriptCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged();
        SaveConfigCommand.NotifyCanExecuteChanged();
        AddConfigItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnStartScriptContentChanged(string value)
    {
        SaveStartScriptCommand.NotifyCanExecuteChanged();
    }

    partial void OnStopScriptContentChanged(string value)
    {
        SaveStopScriptCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用文件",
            Filter = "应用文件 (*.jar;*.zip;*.tar;*.gz;*.war;*.exe)|*.jar;*.zip;*.tar;*.gz;*.war;*.exe|所有文件 (*.*)|*.*",
            FilterIndex = 1,
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            LocalSourcePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择应用目录",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            LocalSourcePath = dialog.FolderName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadOnlyAsync()
    {
        await RunBusyOperationAsync(
            "正在上传应用...",
            async token =>
            {
                UploadProgress = 0;
                AppendActivity($"上传来源: {LocalSourcePath}");
                AppendActivity($"远程目录: {RemoteDirectory}");

                var remoteTarget = await _customApplicationService.UploadApplicationAsync(
                    _host,
                    LocalSourcePath,
                    RemoteDirectory,
                    PreserveTopLevelDirectory,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"上传完成: {remoteTarget}", LogLevel.Success);
                StatusMessage = "上传完成";
            },
            "上传失败");
    }

    [RelayCommand(CanExecute = nameof(CanUploadAndStart))]
    private async Task UploadAndStartAsync()
    {
        await RunBusyOperationAsync(
            "正在上传并启动...",
            async token =>
            {
                UploadProgress = 0;
                AppendActivity($"上传来源: {LocalSourcePath}");
                AppendActivity($"远程目录: {RemoteDirectory}");

                var remoteTarget = await _customApplicationService.UploadApplicationAsync(
                    _host,
                    LocalSourcePath,
                    RemoteDirectory,
                    PreserveTopLevelDirectory,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"上传完成: {remoteTarget}", LogLevel.Success);

                if (!string.IsNullOrWhiteSpace(StartCommand))
                {
                    AppendActivity("正在执行启动命令...");
                    var output = await _customApplicationService.StartApplicationAsync(_host, StartCommand, token);
                    AppendActivity("启动命令已执行", LogLevel.Success);
                    AppendCommandOutput(output);
                }

                StatusMessage = "上传并启动完成";
            },
            "上传并启动失败");
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartApplicationAsync()
    {
        await RunBusyOperationAsync(
            "正在执行启动命令...",
            async token =>
            {
                AppendActivity("正在执行启动命令...");
                var output = await _customApplicationService.StartApplicationAsync(_host, StartCommand, token);
                AppendActivity("启动命令已执行", LogLevel.Success);
                AppendCommandOutput(output);
                StatusMessage = "启动命令执行完成";
            },
            "启动命令执行失败");
    }

    [RelayCommand(CanExecute = nameof(CanEditConfig))]
    private async Task EditConfigAsync()
    {
        await LoadConfigAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLoadConfig))]
    private async Task LoadConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在加载配置文件...",
            async token =>
            {
                AppendActivity($"正在加载配置: {ConfigFilePath}");
                ConfigFileContent = await _customApplicationService.LoadConfigContentAsync(_host, ConfigFilePath, token);
                EditingSession.Load(ConfigFilePath, ConfigFileContent, EditingSession.CurrentEditMode);
                ConfigFileContent = EditingSession.Content;
                _hasLoadedConfig = true;
                AppendActivity("配置加载完成。", LogLevel.Success);
                StatusMessage = "配置文件已加载，可按结构化或文本模式编辑";
            },
            "加载配置失败");
    }

    [RelayCommand(CanExecute = nameof(CanSaveConfig))]
    private async Task SaveConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在保存配置文件...",
            async token =>
            {
                ConfigFileContent = EditingSession.GetContentForSave();
                AppendActivity($"保存配置文件: {ConfigFilePath}");
                await _customApplicationService.SaveConfigContentAsync(_host, ConfigFilePath, ConfigFileContent, token);
                EditingSession.ApplyExternalContent(ConfigFileContent, markAsSaved: true);
                AppendActivity("配置文件保存完成", LogLevel.Success);
                StatusMessage = "配置文件已保存";
            },
            "保存配置文件失败");
    }

    [RelayCommand(CanExecute = nameof(CanAddConfigItem))]
    private void AddConfigItem()
    {
        EditingSession.AddItem();
        ConfigFileContent = EditingSession.Content;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedConfigItem))]
    private void RemoveSelectedConfigItem()
    {
        EditingSession.RemoveSelectedItem();
        ConfigFileContent = EditingSession.Content;
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        AppendActivity("正在取消当前任务...", LogLevel.Warning);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (IsBusy)
        {
            var result = MessageBox.Show(
                "当前任务仍在运行，仍要关闭窗口吗？",
                "确认关闭",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _operationCts?.Cancel();
        }

        if (EditingSession.IsModified && !string.IsNullOrWhiteSpace(ConfigFilePath))
        {
            var result = MessageBox.Show(
                "配置已修改，关闭前是否保存？",
                "确认关闭",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                await SaveConfigAsync();
                if (EditingSession.IsModified)
                {
                    return;
                }
            }
        }

        CloseAction?.Invoke();
    }

    private void OnEditingSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigEditingSession.Content) && ConfigFileContent != EditingSession.Content)
        {
            ConfigFileContent = EditingSession.Content;
        }

        RaiseEditingStateChanged();
    }

    private void RaiseEditingStateChanged()
    {
        OnPropertyChanged(nameof(ConfigItems));
        OnPropertyChanged(nameof(YamlTreeNodes));
        OnPropertyChanged(nameof(IsYamlMode));
        OnPropertyChanged(nameof(IsXmlMode));
        OnPropertyChanged(nameof(IsRawTextMode));
        OnPropertyChanged(nameof(CanSwitchToStructuredMode));
        OnPropertyChanged(nameof(CanSwitchToTextMode));
        OnPropertyChanged(nameof(ShowKeyValueEditor));
        OnPropertyChanged(nameof(ShowYamlEditor));
        OnPropertyChanged(nameof(ShowTextEditor));
        OnPropertyChanged(nameof(ShowStructuredActions));
        OnPropertyChanged(nameof(SupportsStructuredEditing));
        OnPropertyChanged(nameof(SelectedConfigItem));
        OnPropertyChanged(nameof(SelectedYamlNode));
        AddConfigItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
        SaveConfigCommand.NotifyCanExecuteChanged();
        SwitchToStructuredModeCommand.NotifyCanExecuteChanged();
        SwitchToTextModeCommand.NotifyCanExecuteChanged();
    }

    private bool CanUpload() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(LocalSourcePath) &&
        !string.IsNullOrWhiteSpace(RemoteDirectory);

    private bool CanUploadAndStart() =>
        CanUpload() && !string.IsNullOrWhiteSpace(StartCommand);

    private bool CanStart() =>
        !IsBusy && !string.IsNullOrWhiteSpace(StartCommand);

    private bool CanEditConfig() =>
        !IsBusy && !string.IsNullOrWhiteSpace(ConfigFilePath);

    private bool CanLoadConfig() =>
        !IsBusy && !string.IsNullOrWhiteSpace(ConfigFilePath);

    private bool CanSaveConfig() =>
        !IsBusy && !string.IsNullOrWhiteSpace(ConfigFilePath) && (_hasLoadedConfig || EditingSession.IsModified);

    partial void OnConfigFileContentChanged(string value)
    {
        if (value == EditingSession.Content)
        {
            return;
        }

        EditingSession.ApplyExternalContent(value, markAsSaved: false);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSwitchToStructuredMode))]
    private void SwitchToStructuredMode()
    {
        if (EditingSession.TrySwitchToStructuredMode(out var errorMessage))
        {
            ConfigFileContent = EditingSession.Content;
            return;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            StatusMessage = errorMessage;
            MessageBox.Show(errorMessage, "无法切换模式", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool CanExecuteSwitchToStructuredMode() => EditingSession.CanEnterStructuredMode;

    [RelayCommand(CanExecute = nameof(CanExecuteSwitchToTextMode))]
    private void SwitchToTextMode()
    {
        EditingSession.TrySwitchToTextMode();
        ConfigFileContent = EditingSession.Content;
    }

    private bool CanExecuteSwitchToTextMode() => EditingSession.CanEnterTextMode;

    private bool CanAddConfigItem() =>
        !IsBusy && EditingSession.ShowStructuredActions && !string.IsNullOrWhiteSpace(ConfigFilePath);

    private bool CanRemoveSelectedConfigItem()
    {
        if (IsBusy || !EditingSession.ShowStructuredActions)
        {
            return false;
        }

        return IsYamlMode ? SelectedYamlNode != null : SelectedConfigItem != null;
    }

    private bool CanLoadStartScript() =>
        !IsBusy && !string.IsNullOrWhiteSpace(RemoteDirectory);

    private bool CanLoadStopScript() =>
        !IsBusy && !string.IsNullOrWhiteSpace(RemoteDirectory);

    private bool CanSaveStartScript() =>
        !IsBusy && !string.IsNullOrWhiteSpace(StartScriptContent);

    private bool CanSaveStopScript() =>
        !IsBusy && !string.IsNullOrWhiteSpace(StopScriptContent);

    private bool CanCancelOperation() =>
        IsBusy && _operationCts != null && !_operationCts.IsCancellationRequested;

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
                var listCmd = $"ls -la {RemoteDirectory} 2>/dev/null";
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
                    if (string.IsNullOrEmpty(trimmed) || trimmed == "total")
                    {
                        continue;
                    }

                    // 解析 ls -la 输出
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 8)
                    {
                        var firstCol = parts[0];
                        var isDir = firstCol.StartsWith("d");
                        var name = parts[parts.Length - 1];
                        if (name == "." || name == "..") continue;

                        var remotePath = RemoteDirectory.TrimEnd('/') + "/" + name;
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

    [RelayCommand]
    private async Task LoadChildDirectoriesAsync(DirectoryItem? parent)
    {
        if (parent == null || !parent.IsDirectory || parent.IsLoaded) return;
        if (IsBusy) return;

        await RunBusyOperationAsync(
            $"正在加载 {parent.Name}...",
            async token =>
            {
                parent.Children.Clear();

                var listCmd = $"ls -la {parent.RemotePath} 2>/dev/null";
                var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, token);

                if (string.IsNullOrWhiteSpace(result))
                {
                    parent.IsLoaded = true;
                    return;
                }

                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed == "total")
                    {
                        continue;
                    }

                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 8)
                    {
                        var firstCol = parts[0];
                        var isDir = firstCol.StartsWith("d");
                        var name = parts[parts.Length - 1];
                        if (name == "." || name == "..") continue;

                        var remotePath = parent.RemotePath.TrimEnd('/') + "/" + name;
                        parent.Children.Add(new DirectoryItem
                        {
                            Name = name,
                            RemotePath = remotePath,
                            IsDirectory = isDir
                        });
                    }
                }

                parent.IsLoaded = true;
            },
            "加载子目录失败");
    }

    [ObservableProperty]
    private string _replaceFilePath = string.Empty;

    partial void OnSelectedDirectoryItemChanged(DirectoryItem? value)
    {
        ReplaceFileCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private DirectoryItem? _selectedDirectoryItem;

    [RelayCommand(CanExecute = nameof(CanReplaceFile))]
    private async Task ReplaceFileAsync()
    {
        if (SelectedDirectoryItem == null || SelectedDirectoryItem.IsDirectory) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择替换文件",
            Filter = "所有文件 (*.*)|*.*",
            FilterIndex = 1,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            ReplaceFilePath = dialog.FileName;

            await RunBusyOperationAsync(
                "正在替换文件...",
                async token =>
                {
                    UploadProgress = 0;
                    AppendActivity($"替换文件: {SelectedDirectoryItem.RemotePath}");
                    AppendActivity($"使用本地文件: {ReplaceFilePath}");

                    await _customApplicationService.UploadFileAsync(
                        _host,
                        ReplaceFilePath,
                        SelectedDirectoryItem.RemotePath,
                        progress => UploadProgress = progress,
                        token);

                    UploadProgress = 100;
                    AppendActivity("文件替换完成", LogLevel.Success);
                    StatusMessage = "文件替换完成";
                },
                "替换文件失败");
        }
    }

    private bool CanReplaceFile() => !IsBusy && SelectedDirectoryItem != null && !SelectedDirectoryItem.IsDirectory;

    [RelayCommand(CanExecute = nameof(CanLoadStartScript))]
    private async Task LoadStartScriptAsync()
    {
        await RunBusyOperationAsync(
            "正在加载启动脚本...",
            async token =>
            {
                var remoteScriptPath = GetRemoteScriptPath("start.sh");
                AppendActivity($"加载启动脚本: {remoteScriptPath}");
                StartScriptContent = await _customApplicationService.LoadConfigContentAsync(_host, remoteScriptPath, token);
                AppendActivity("启动脚本加载完成", LogLevel.Success);
                StatusMessage = "启动脚本已加载，可在下方编辑";
            },
            "加载启动脚本失败");
    }

    [RelayCommand(CanExecute = nameof(CanLoadStopScript))]
    private async Task LoadStopScriptAsync()
    {
        await RunBusyOperationAsync(
            "正在加载停止脚本...",
            async token =>
            {
                var remoteScriptPath = GetRemoteScriptPath("stop.sh");
                AppendActivity($"加载停止脚本: {remoteScriptPath}");
                StopScriptContent = await _customApplicationService.LoadConfigContentAsync(_host, remoteScriptPath, token);
                AppendActivity("停止脚本加载完成", LogLevel.Success);
                StatusMessage = "停止脚本已加载，可在下方编辑";
            },
            "加载停止脚本失败");
    }

    [RelayCommand(CanExecute = nameof(CanSaveStartScript))]
    private async Task SaveStartScriptAsync()
    {
        await RunBusyOperationAsync(
            "正在保存启动脚本...",
            async token =>
            {
                var remoteScriptPath = GetRemoteScriptPath("start.sh");
                AppendActivity($"保存启动脚本: {remoteScriptPath}");
                await _customApplicationService.SaveConfigContentAsync(_host, remoteScriptPath, StartScriptContent, token);
                AppendActivity("启动脚本保存完成", LogLevel.Success);
                StatusMessage = "启动脚本已保存";
            },
            "保存启动脚本失败");
    }

    [RelayCommand(CanExecute = nameof(CanSaveStopScript))]
    private async Task SaveStopScriptAsync()
    {
        await RunBusyOperationAsync(
            "正在保存停止脚本...",
            async token =>
            {
                var remoteScriptPath = GetRemoteScriptPath("stop.sh");
                AppendActivity($"保存停止脚本: {remoteScriptPath}");
                await _customApplicationService.SaveConfigContentAsync(_host, remoteScriptPath, StopScriptContent, token);
                AppendActivity("停止脚本保存完成", LogLevel.Success);
                StatusMessage = "停止脚本已保存";
            },
            "保存停止脚本失败");
    }

    private string GetRemoteScriptPath(string scriptName)
    {
        var dir = RemoteDirectory.TrimEnd('/');
        return $"{dir}/{scriptName}";
    }

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
        _logSink?.Invoke(message, level);
    }

    private void AppendCommandOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return;

        var normalized = output.Trim();
        if (normalized.Length > 800)
        {
            normalized = normalized[..800] + Environment.NewLine + "...";
        }

        if (_activityBuilder.Length > 0)
        {
            _activityBuilder.AppendLine();
        }
        _activityBuilder.Append(normalized);
        ActivityLog = _activityBuilder.ToString();
    }

    private string GetSourceSummary()
    {
        if (string.IsNullOrWhiteSpace(LocalSourcePath))
        {
            return "未选择本地文件或目录";
        }

        if (File.Exists(LocalSourcePath))
        {
            var fileInfo = new FileInfo(LocalSourcePath);
            return $"文件: {fileInfo.Name} ({FormatSize(fileInfo.Length)})";
        }

        if (Directory.Exists(LocalSourcePath))
        {
            var directoryInfo = new DirectoryInfo(LocalSourcePath);
            return $"目录: {directoryInfo.Name}";
        }

        return "本地路径不存在";
    }

    private string GetRemoteTargetPreview()
    {
        if (string.IsNullOrWhiteSpace(RemoteDirectory))
        {
            return "请输入远程目录";
        }

        if (string.IsNullOrWhiteSpace(LocalSourcePath))
        {
            return RemoteDirectory;
        }

        if (File.Exists(LocalSourcePath))
        {
            return CombineRemotePath(RemoteDirectory, Path.GetFileName(LocalSourcePath));
        }

        if (Directory.Exists(LocalSourcePath))
        {
            return PreserveTopLevelDirectory
                ? CombineRemotePath(RemoteDirectory, new DirectoryInfo(LocalSourcePath).Name)
                : RemoteDirectory;
        }

        return RemoteDirectory;
    }

    private static string CombineRemotePath(string basePath, string childPath)
    {
        var normalizedBasePath = NormalizeRemotePath(basePath);
        var normalizedChildPath = NormalizeRemotePath(childPath).TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalizedBasePath))
        {
            return normalizedChildPath;
        }

        if (string.IsNullOrWhiteSpace(normalizedChildPath))
        {
            return normalizedBasePath;
        }

        return normalizedBasePath.EndsWith("/", StringComparison.Ordinal)
            ? normalizedBasePath + normalizedChildPath
            : $"{normalizedBasePath}/{normalizedChildPath}";
    }

    private static string NormalizeRemotePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }

    private static string FormatSize(long length)
    {
        var value = (double)Math.Max(0, length);
        var units = new[] { "B", "KB", "MB", "GB" };
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private static string InferApplicationName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        if (File.Exists(path)) return Path.GetFileNameWithoutExtension(path);
        if (Directory.Exists(path)) return new DirectoryInfo(path).Name;
        return string.Empty;
    }
}
