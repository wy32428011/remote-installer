using System;
using System.Collections.Generic;
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
using RemoteInstaller.ViewModels.Shared.ConfigEditing;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// SUPPORT 应用部署 ViewModel
/// </summary>
public partial class SupportDeployViewModel : ObservableObject
{
    private readonly CustomApplicationService _customApplicationService;
    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly Action<string, LogLevel>? _logSink;
    private CancellationTokenSource? _operationCts;
    private bool _hasLoadedConfig;
    private bool _suppressSelectedConfigFileChange;
    private long _configFileSwitchVersion;
    private string _currentConfigFilePath = string.Empty;
    private DirectoryItem? _currentConfigFileItem;

    [ObservableProperty]
    private string _windowTitle;

    [ObservableProperty]
    private string _applicationDisplayName = "SUPPORT";

    [ObservableProperty]
    private string _hostDisplay;

    [ObservableProperty]
    private string _localSourcePath = string.Empty;

    [ObservableProperty]
    private string _remoteDirectory = "/opt/zeus-support";

    [ObservableProperty]
    private string _localFrontendPath = string.Empty;

    [ObservableProperty]
    private string _remoteFrontendDirectory = "/var/www/zeus-support";

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
    private string _startCommandText = "cd /opt/zeus-support && bash start.sh";

    [ObservableProperty]
    private string _stopCommandText = "cd /opt/zeus-support && bash stop.sh";

    [ObservableProperty]
    private string _pidFilePath = "/opt/zeus-support/run/run.PID";

    [ObservableProperty]
    private string _startScriptContent = string.Empty;

    [ObservableProperty]
    private string _stopScriptContent = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    private string _configDirectory = "/opt/zeus-support/conf";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    private string _configFileName = "application-prod.properties";

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
    private string _logDirectory = "/opt/zeus-support/log";

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _logDirectoryItems = new();

    [ObservableProperty]
    private DirectoryItem? _selectedLogDirectoryItem;

    [ObservableProperty]
    private string _selectedLogFilePath = string.Empty;

    [ObservableProperty]
    private string _logFileContent = string.Empty;

    [ObservableProperty]
    private int _logReadLineCount = 500;

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _directoryItems = new();

    [ObservableProperty]
    private ObservableCollection<DirectoryItem> _configFileItems = new();

    [ObservableProperty]
    private DirectoryItem? _selectedConfigFileItem;

    public Action? CloseAction { get; set; }

    public string SourceSummary => GetSourceSummary();

    public SupportDeployViewModel(
        CustomApplicationService customApplicationService,
        ConfigurationService configurationService,
        RemoteHost host,
        Action<string, LogLevel>? logSink = null)
        : this(customApplicationService, configurationService, host, null, logSink)
    {
    }

    public SupportDeployViewModel(
        CustomApplicationService customApplicationService,
        ConfigurationService configurationService,
        RemoteHost host,
        CustomAppDefinition? appDefinition,
        Action<string, LogLevel>? logSink = null)
    {
        _customApplicationService = customApplicationService;
        _configurationService = configurationService;
        _host = host;
        _logSink = logSink;

        EditingSession.PropertyChanged += OnEditingSessionPropertyChanged;

        ApplyAppDefinition(appDefinition);
        WindowTitle = $"{ApplicationDisplayName} 部署 - {host.Name}";
        HostDisplay = $"{host.Name} ({host.IpAddress}:{host.Port})";
    }

    partial void OnLocalSourcePathChanged(string value)
    {
        OnPropertyChanged(nameof(SourceSummary));
        UploadCommand.NotifyCanExecuteChanged();
    }

    partial void OnLocalFrontendPathChanged(string value)
    {
        UploadFrontendCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        UploadFrontendCommand.NotifyCanExecuteChanged();
        ReplaceFileCommand.NotifyCanExecuteChanged();
        LoadStartScriptCommand.NotifyCanExecuteChanged();
        LoadStopScriptCommand.NotifyCanExecuteChanged();
        SaveStartScriptCommand.NotifyCanExecuteChanged();
        SaveStopScriptCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged();
        SaveConfigCommand.NotifyCanExecuteChanged();
        AddConfigItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
        LoadSelectedLogFileCommand.NotifyCanExecuteChanged();
        RefreshLogFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnStartScriptContentChanged(string value)
    {
        SaveStartScriptCommand.NotifyCanExecuteChanged();
    }

    partial void OnStopScriptContentChanged(string value)
    {
        SaveStopScriptCommand.NotifyCanExecuteChanged();
    }

    partial void OnConfigFileContentChanged(string value)
    {
        if (value == EditingSession.Content)
        {
            SaveConfigCommand.NotifyCanExecuteChanged();
            return;
        }

        EditingSession.ApplyExternalContent(value, markAsSaved: false);
        SaveConfigCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLogDirectoryItemChanged(DirectoryItem? value)
    {
        LoadSelectedLogFileCommand.NotifyCanExecuteChanged();
        RefreshLogFileCommand.NotifyCanExecuteChanged();

        if (value != null && !value.IsDirectory)
        {
            SelectedLogFilePath = value.RemotePath;
            _ = LoadSelectedLogFileAsync();
        }
        else
        {
            SelectedLogFilePath = string.Empty;
            LogFileContent = string.Empty;
        }
    }

    public async Task InitializeAsync()
    {
        await CheckStatusAsync();
    }

    private void ApplyAppDefinition(CustomAppDefinition? appDefinition)
    {
        var definition = appDefinition;
        var baseRemoteDirectory = string.IsNullOrWhiteSpace(definition?.RemoteDirectory)
            ? RemoteDirectory
            : definition!.RemoteDirectory.Trim();

        var appName = string.IsNullOrWhiteSpace(definition?.Name) ? "SUPPORT" : definition!.Name.Trim();
        ApplicationDisplayName = appName;

        RemoteDirectory = baseRemoteDirectory;
        RemoteFrontendDirectory = string.IsNullOrWhiteSpace(definition?.RemoteFrontendDirectory)
            ? $"/var/www/{appName.ToLowerInvariant()}"
            : definition!.RemoteFrontendDirectory.Trim();

        StartCommandText = string.IsNullOrWhiteSpace(definition?.StartCommand)
            ? $"cd {baseRemoteDirectory} && bash start.sh"
            : definition!.StartCommand.Trim();

        StopCommandText = string.IsNullOrWhiteSpace(definition?.StopCommand)
            ? $"cd {baseRemoteDirectory} && bash stop.sh"
            : definition!.StopCommand.Trim();

        PidFilePath = string.IsNullOrWhiteSpace(definition?.PidFilePath)
            ? $"{baseRemoteDirectory.TrimEnd('/')}/run/run.PID"
            : definition!.PidFilePath.Trim();

        ConfigDirectory = string.IsNullOrWhiteSpace(definition?.ConfigDirectory)
            ? $"{baseRemoteDirectory.TrimEnd('/')}/conf"
            : definition!.ConfigDirectory.Trim();

        ConfigFileName = string.IsNullOrWhiteSpace(definition?.ConfigFileName)
            ? "application-prod.properties"
            : definition!.ConfigFileName.Trim();

        ConfigFileContent = string.Empty;
        _currentConfigFilePath = GetRemoteConfigPath();
        _currentConfigFileItem = null;
        EditingSession.Load(_currentConfigFilePath, string.Empty, ConfigEditMode.Structured);

        LogDirectory = string.IsNullOrWhiteSpace(definition?.LogDirectory)
            ? $"{baseRemoteDirectory.TrimEnd('/')}/log"
            : definition!.LogDirectory.Trim();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"选择 {ApplicationDisplayName} 应用目录",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            LocalSourcePath = dialog.FolderName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await RunBusyOperationAsync(
            $"正在启动 {ApplicationDisplayName}...",
            async token =>
            {
                AppendActivity("正在执行启动命令...");
                var output = await _customApplicationService.StartApplicationAsync(_host, StartCommandText, token);
                AppendActivity("启动命令已执行");
                AppendCommandOutput(output);
                await Task.Delay(2000); // 等待服务启动
                await CheckStatusAsync();
            },
            "启动失败");
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await RunBusyOperationAsync(
            $"正在停止 {ApplicationDisplayName}...",
            async token =>
            {
                AppendActivity("正在执行停止命令...");
                var output = await _customApplicationService.StartApplicationAsync(_host, StopCommandText, token);
                AppendActivity("停止命令已执行");
                AppendCommandOutput(output);
                await Task.Delay(1000);
                await CheckStatusAsync();
            },
            "停止失败");
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        await RunBusyOperationAsync(
            $"正在重启 {ApplicationDisplayName}...",
            async token =>
            {
                AppendActivity("正在执行重启...");
                await _customApplicationService.StartApplicationAsync(_host, StopCommandText, token);
                AppendActivity("停止命令已执行，等待...");
                await Task.Delay(2000);
                await _customApplicationService.StartApplicationAsync(_host, StartCommandText, token);
                AppendActivity("启动命令已执行");
                await Task.Delay(2000);
                await CheckStatusAsync();
            },
            "重启失败");
    }

    [RelayCommand]
    private async Task CheckStatusAsync()
    {
        try
        {
            // 检查 PID 文件判断是否运行
            var quotedPidFilePath = QuoteShellArg(PidFilePath);
            var checkPidCmd = $"test -f {quotedPidFilePath} && cat {quotedPidFilePath} || echo \"\"";
            var pidResult = await _customApplicationService.StartApplicationAsync(_host, checkPidCmd, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(pidResult) && pidResult.Trim() != "0")
            {
                var pidText = pidResult.Trim();
                if (!int.TryParse(pidText, out var pid) || pid <= 0)
                {
                    IsRunning = false;
                    AppStatus = "未运行";
                    return;
                }

                // 检查进程是否存在
                var checkProcessCmd = $"ps -p {pid} > /dev/null 2>&1 && echo \"running\" || echo \"not_running\"";
                var processResult = await _customApplicationService.StartApplicationAsync(_host, checkProcessCmd, CancellationToken.None);

                if (processResult.Trim() == "running")
                {
                    IsRunning = true;
                    AppStatus = $"运行中 (PID: {pid})";
                }
                else
                {
                    IsRunning = false;
                    AppStatus = "未运行";
                }
            }
            else
            {
                IsRunning = false;
                AppStatus = "未运行";
            }
        }
        catch (Exception ex)
        {
            IsRunning = false;
            AppStatus = $"检查失败: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
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
                    preserveTopLevelDirectory: false,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"上传完成: {remoteTarget}", LogLevel.Success);
                StatusMessage = "上传完成";
            },
            "上传失败");
    }

    [RelayCommand(CanExecute = nameof(CanUploadFrontend))]
    private async Task UploadFrontendAsync()
    {
        await RunBusyOperationAsync(
            "正在上传前端...",
            async token =>
            {
                UploadProgress = 0;
                AppendActivity($"上传前端来源: {LocalFrontendPath}");
                AppendActivity($"前端远程目录: {RemoteFrontendDirectory}");

                var remoteTarget = await _customApplicationService.UploadApplicationAsync(
                    _host,
                    LocalFrontendPath,
                    RemoteFrontendDirectory,
                    preserveTopLevelDirectory: false,
                    progress => UploadProgress = progress,
                    token);

                UploadProgress = 100;
                AppendActivity($"前端上传完成: {remoteTarget}", LogLevel.Success);
                StatusMessage = "前端上传完成";
            },
            "前端上传失败");
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
                var listCmd = $"ls -la {QuoteShellArg(RemoteDirectory)} 2>/dev/null";
                var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, token);

                if (string.IsNullOrWhiteSpace(result))
                {
                    StatusMessage = "目录为空或不存在";
                    return;
                }

                AppendActivity($"ls 输出: {result}");

                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed == "total")
                    {
                        continue;
                    }

                    // 解析 ls -la 输出
                    // 格式: drwxr-xr-x  2 user group 4096 Apr 14 10:00 dirname
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 8)
                    {
                        var firstCol = parts[0];
                        var isDir = firstCol.StartsWith("d");
                        // 文件名是最后一部分
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

                var listCmd = $"ls -la {QuoteShellArg(parent.RemotePath)} 2>/dev/null";
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

    [RelayCommand]
    private void BrowseFrontendFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择前端文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            LocalFrontendPath = dialog.FolderName;
        }
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

        if (EditingSession.IsModified && !string.IsNullOrWhiteSpace(GetRemoteConfigPath()))
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

    private bool CanStart() => !IsBusy && !IsRunning;
    private bool CanStop() => !IsBusy && IsRunning;
    private bool CanRestart() => !IsBusy && IsRunning;
    private bool CanUpload() => !IsBusy && !string.IsNullOrWhiteSpace(LocalSourcePath);
    private bool CanUploadFrontend() => !IsBusy && !string.IsNullOrWhiteSpace(LocalFrontendPath) && !string.IsNullOrWhiteSpace(RemoteFrontendDirectory);

    private bool CanLoadStartScript() => !IsBusy && !string.IsNullOrWhiteSpace(RemoteDirectory);
    private bool CanLoadStopScript() => !IsBusy && !string.IsNullOrWhiteSpace(RemoteDirectory);
    private bool CanSaveStartScript() => !IsBusy && !string.IsNullOrWhiteSpace(StartScriptContent);
    private bool CanSaveStopScript() => !IsBusy && !string.IsNullOrWhiteSpace(StopScriptContent);

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

    [RelayCommand]
    private async Task LoadConfigDirectoryAsync()
    {
        if (IsBusy) return;

        await RunBusyOperationAsync(
            "正在加载配置文件目录...",
            async token =>
            {
                ConfigFileItems.Clear();

                var listCmd = $"ls -la {QuoteShellArg(ConfigDirectory)} 2>/dev/null";
                var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, token);

                if (string.IsNullOrWhiteSpace(result))
                {
                    StatusMessage = "配置文件目录为空或不存在";
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

                        var remotePath = ConfigDirectory.TrimEnd('/') + "/" + name;
                        ConfigFileItems.Add(new DirectoryItem
                        {
                            Name = name,
                            RemotePath = remotePath,
                            IsDirectory = isDir
                        });
                    }
                }

                AppendActivity($"配置文件目录加载完成，共 {ConfigFileItems.Count} 项", LogLevel.Success);
                StatusMessage = $"配置文件目录包含 {ConfigFileItems.Count} 项";
            },
            "加载配置文件目录失败");
    }

    partial void OnSelectedConfigFileItemChanged(DirectoryItem? value)
    {
        if (_suppressSelectedConfigFileChange || value == null || value.IsDirectory || IsBusy)
        {
            return;
        }

        var targetPath = value.RemotePath;
        if (string.Equals(targetPath, _currentConfigFilePath, StringComparison.Ordinal))
        {
            return;
        }

        _ = SwitchToConfigFileAsync(value);
    }

    [RelayCommand(CanExecute = nameof(CanAddConfigItem))]
    private void AddConfigItem()
    {
        EditingSession.AddItem();
        ConfigFileContent = EditingSession.Content;
    }

    private bool CanAddConfigItem() =>
        !IsBusy && EditingSession.ShowStructuredActions && !string.IsNullOrWhiteSpace(_currentConfigFilePath);

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedConfigItem))]
    private void RemoveSelectedConfigItem()
    {
        EditingSession.RemoveSelectedItem();
        ConfigFileContent = EditingSession.Content;
    }

    private bool CanRemoveSelectedConfigItem()
    {
        if (IsBusy || !EditingSession.ShowStructuredActions)
        {
            return false;
        }

        return IsYamlMode ? SelectedYamlNode != null : SelectedConfigItem != null;
    }

    private string GetRemoteConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentConfigFilePath))
        {
            return _currentConfigFilePath;
        }

        var dir = ConfigDirectory.TrimEnd('/');
        return $"{dir}/{ConfigFileName}";
    }

    private bool CanLoadConfig() => !IsBusy && !string.IsNullOrWhiteSpace(ConfigDirectory) && !string.IsNullOrWhiteSpace(ConfigFileName);
    private bool CanSaveConfig() => !IsBusy && !string.IsNullOrWhiteSpace(GetRemoteConfigPath()) && (_hasLoadedConfig || EditingSession.IsModified);

    [RelayCommand(CanExecute = nameof(CanLoadConfig))]
    private async Task LoadConfigAsync()
    {
        var targetPath = GetRemoteConfigPath();
        if (!await LoadConfigFileAsync(targetPath, EditingSession.CurrentEditMode))
        {
            return;
        }

        _currentConfigFileItem ??= FindConfigFileItemByPath(targetPath);
        RestoreSelectedConfigFile(_currentConfigFileItem);
    }

    [RelayCommand(CanExecute = nameof(CanSaveConfig))]
    private async Task SaveConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在保存配置文件...",
            async token =>
            {
                var remoteConfigPath = GetRemoteConfigPath();
                ConfigFileContent = EditingSession.GetContentForSave();
                AppendActivity($"保存配置文件: {remoteConfigPath}");
                await _customApplicationService.SaveConfigContentAsync(_host, remoteConfigPath, ConfigFileContent, token);
                EditingSession.ApplyExternalContent(ConfigFileContent, markAsSaved: true);
                AppendActivity("配置文件保存完成", LogLevel.Success);
                StatusMessage = "配置文件已保存";
            },
            "保存配置文件失败");
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

    private async Task<bool> LoadConfigFileAsync(string remoteConfigPath, ConfigEditMode? preferredMode = null)
    {
        var loaded = false;

        await RunBusyOperationAsync(
            "正在加载配置文件...",
            async token =>
            {
                AppendActivity($"加载配置文件: {remoteConfigPath}");
                var content = await _customApplicationService.LoadConfigContentAsync(_host, remoteConfigPath, token);
                _currentConfigFilePath = remoteConfigPath;
                ConfigFileName = Path.GetFileName(remoteConfigPath);
                EditingSession.Load(remoteConfigPath, content, preferredMode);
                ConfigFileContent = EditingSession.Content;
                _hasLoadedConfig = true;
                loaded = true;
                AppendActivity("配置文件加载完成", LogLevel.Success);
                StatusMessage = "配置文件已加载，可按结构化或文本模式编辑";
            },
            "加载配置文件失败");

        return loaded;
    }

    private async Task SwitchToConfigFileAsync(DirectoryItem targetFile)
    {
        var switchVersion = Interlocked.Increment(ref _configFileSwitchVersion);
        var originalPath = _currentConfigFilePath;
        var originalFileItem = _currentConfigFileItem;

        if (EditingSession.IsModified)
        {
            var result = MessageBox.Show(
                "当前文件有未保存修改。是否先保存后再切换？",
                "切换文件",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                RestoreSelectedConfigFile(originalFileItem);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                await SaveConfigAsync();
                if (switchVersion != _configFileSwitchVersion)
                {
                    return;
                }

                if (EditingSession.IsModified)
                {
                    RestoreSelectedConfigFile(originalFileItem);
                    return;
                }
            }
        }

        var loaded = await LoadConfigFileAsync(targetFile.RemotePath, EditingSession.CurrentEditMode);
        if (switchVersion != _configFileSwitchVersion)
        {
            return;
        }

        if (!loaded)
        {
            _currentConfigFilePath = originalPath;
            _currentConfigFileItem = originalFileItem;
            RestoreSelectedConfigFile(originalFileItem);
            return;
        }

        _currentConfigFileItem = targetFile;
        RestoreSelectedConfigFile(targetFile);
    }

    private void RestoreSelectedConfigFile(DirectoryItem? fileItem)
    {
        _suppressSelectedConfigFileChange = true;
        SelectedConfigFileItem = fileItem;
        _suppressSelectedConfigFileChange = false;
    }

    private DirectoryItem? FindConfigFileItemByPath(string remotePath)
    {
        foreach (var item in ConfigFileItems)
        {
            var found = FindDirectoryItemByPath(item, remotePath);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static DirectoryItem? FindDirectoryItemByPath(DirectoryItem current, string remotePath)
    {
        if (string.Equals(current.RemotePath, remotePath, StringComparison.Ordinal))
        {
            return current;
        }

        foreach (var child in current.Children)
        {
            var found = FindDirectoryItemByPath(child, remotePath);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnEditingSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
        LoadConfigCommand.NotifyCanExecuteChanged();
        SaveConfigCommand.NotifyCanExecuteChanged();
        SwitchToStructuredModeCommand.NotifyCanExecuteChanged();
        SwitchToTextModeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task LoadLogDirectoryAsync()
    {
        if (IsBusy) return;

        await RunBusyOperationAsync(
            "正在加载日志目录...",
            async token =>
            {
                LogDirectoryItems.Clear();

                var listCmd = $"ls -la {QuoteShellArg(LogDirectory)} 2>/dev/null";
                var result = await _customApplicationService.StartApplicationAsync(_host, listCmd, token);

                if (string.IsNullOrWhiteSpace(result))
                {
                    StatusMessage = "日志目录为空或不存在";
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

                        var remotePath = LogDirectory.TrimEnd('/') + "/" + name;
                        LogDirectoryItems.Add(new DirectoryItem
                        {
                            Name = name,
                            RemotePath = remotePath,
                            IsDirectory = isDir
                        });
                    }
                }

                AppendActivity($"日志目录加载完成，共 {LogDirectoryItems.Count} 项", LogLevel.Success);
                StatusMessage = $"日志目录包含 {LogDirectoryItems.Count} 项";
            },
            "加载日志目录失败");
    }

    private bool CanLoadSelectedLogFile() =>
        !IsBusy && SelectedLogDirectoryItem != null && !SelectedLogDirectoryItem.IsDirectory;

    [RelayCommand(CanExecute = nameof(CanLoadSelectedLogFile))]
    private async Task LoadSelectedLogFileAsync()
    {
        if (SelectedLogDirectoryItem == null || SelectedLogDirectoryItem.IsDirectory)
        {
            return;
        }

        await RunBusyOperationAsync(
            "正在加载日志文件...",
            async token =>
            {
                var maxLines = NormalizeLogReadLineCount(LogReadLineCount);
                var remoteFilePath = SelectedLogDirectoryItem.RemotePath;
                var command = $"test -f {QuoteShellArg(remoteFilePath)} && tail -n {maxLines} {QuoteShellArg(remoteFilePath)} || echo \"\"";

                AppendActivity($"加载日志文件: {remoteFilePath}");
                var content = await _customApplicationService.StartApplicationAsync(_host, command, token);
                LogFileContent = content ?? string.Empty;
                SelectedLogFilePath = remoteFilePath;
                StatusMessage = "日志文件已加载";
            },
            "加载日志文件失败");
    }

    [RelayCommand(CanExecute = nameof(CanLoadSelectedLogFile))]
    private async Task RefreshLogFileAsync()
    {
        await LoadSelectedLogFileAsync();
    }

    partial void OnLogReadLineCountChanged(int value)
    {
        LoadSelectedLogFileCommand.NotifyCanExecuteChanged();
        RefreshLogFileCommand.NotifyCanExecuteChanged();
    }

    private static int NormalizeLogReadLineCount(int value)
    {
        if (value < 1) return 1;
        if (value > 2000) return 2000;
        return value;
    }

    private static string QuoteShellArg(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'") + "'";
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

    private void AppendCommandOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return;

        var normalized = output.Trim();
        if (normalized.Length > 800)
        {
            normalized = normalized[..800] + Environment.NewLine + "...";
        }

        if (!string.IsNullOrEmpty(_activityBuilder.ToString()))
        {
            _activityBuilder.AppendLine();
        }
        _activityBuilder.Append(normalized);
        ActivityLog = _activityBuilder.ToString();
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

    private string GetSourceSummary()
    {
        if (string.IsNullOrWhiteSpace(LocalSourcePath))
        {
            return "未选择本地目录";
        }

        if (System.IO.Directory.Exists(LocalSourcePath))
        {
            var dirInfo = new System.IO.DirectoryInfo(LocalSourcePath);
            return $"目录: {dirInfo.Name}";
        }

        return "本地路径不存在";
    }
}
