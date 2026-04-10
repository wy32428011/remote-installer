using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// SUPPORT 应用部署 ViewModel
/// </summary>
public partial class SupportDeployViewModel : ObservableObject
{
    public class ConfigKeyValueItem : ObservableObject
    {
        private string _key = string.Empty;
        private string _value = string.Empty;

        public bool IsEditable { get; init; }

        public bool IsComment { get; init; }

        public bool IsBlank { get; init; }

        public string OriginalLine { get; set; } = string.Empty;

        public char Separator { get; set; } = '=';

        public string LeadingWhitespace { get; set; } = string.Empty;

        public bool IsYamlListItem { get; set; }

        public bool IsYamlContainer { get; set; }

        public string Key
        {
            get => _key;
            set => SetProperty(ref _key, value);
        }

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }

    public partial class YamlTreeNode : ObservableObject
    {
        private string _value = string.Empty;

        public string DisplayKey { get; set; } = string.Empty;

        public bool IsEditable { get; set; }

        public int Level { get; set; }

        public YamlTreeNode? Parent { get; set; }

        public ConfigKeyValueItem? SourceItem { get; set; }

        public ObservableCollection<YamlTreeNode> Children { get; } = new();

        public string Value
        {
            get => _value;
            set
            {
                if (!SetProperty(ref _value, value))
                {
                    return;
                }

                if (SourceItem != null && SourceItem.Value != value)
                {
                    SourceItem.Value = value;
                }
            }
        }
    }

    private readonly CustomApplicationService _customApplicationService;
    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly Action<string, LogLevel>? _logSink;
    private readonly List<ConfigKeyValueItem> _allConfigLines = new();
    private CancellationTokenSource? _operationCts;

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
    private string _configDirectory = "/opt/zeus-support/conf";

    [ObservableProperty]
    private string _configFileName = "application-prod.properties";

    [ObservableProperty]
    private string _configFileContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConfigKeyValueItem> _configItems = new();

    [ObservableProperty]
    private ObservableCollection<YamlTreeNode> _yamlTreeNodes = new();

    [ObservableProperty]
    private bool _isYamlMode;

    [ObservableProperty]
    private bool _isXmlMode;

    public bool IsRawTextMode => IsXmlMode;

    [ObservableProperty]
    private ConfigKeyValueItem? _selectedConfigItem;

    [ObservableProperty]
    private YamlTreeNode? _selectedYamlNode;

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
        SaveConfigCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConfigItemChanged(ConfigKeyValueItem? value)
    {
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsYamlModeChanged(bool value)
    {
        AddConfigItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsXmlModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRawTextMode));
        AddConfigItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedYamlNodeChanged(YamlTreeNode? value)
    {
        RemoveSelectedConfigItemCommand.NotifyCanExecuteChanged();
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
    private void Close()
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
        if (value != null && !value.IsDirectory)
        {
            ConfigFileName = value.Name;
            _ = LoadConfigAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddConfigItem))]
    private void AddConfigItem()
    {
        if (IsXmlMode)
        {
            return;
        }

        if (IsYamlMode)
        {
            AddYamlItem();
            return;
        }

        var item = new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = string.Empty,
            Value = string.Empty,
            Separator = GetDefaultSeparator()
        };

        item.PropertyChanged += OnConfigItemChanged;
        _allConfigLines.Add(item);
        ConfigItems.Add(item);
        SelectedConfigItem = item;
        SyncContentFromConfigItems();
    }

    private bool CanAddConfigItem() => !IsBusy && !IsXmlMode;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedConfigItem))]
    private void RemoveSelectedConfigItem()
    {
        if (IsXmlMode)
        {
            return;
        }

        if (IsYamlMode)
        {
            RemoveSelectedYamlNode();
            return;
        }

        if (SelectedConfigItem == null)
        {
            return;
        }

        SelectedConfigItem.PropertyChanged -= OnConfigItemChanged;
        _allConfigLines.Remove(SelectedConfigItem);
        ConfigItems.Remove(SelectedConfigItem);
        SelectedConfigItem = null;
        SyncContentFromConfigItems();
    }

    private bool CanRemoveSelectedConfigItem()
    {
        if (IsBusy || IsXmlMode)
        {
            return false;
        }

        return IsYamlMode ? SelectedYamlNode != null : SelectedConfigItem != null;
    }

    private void LoadConfigItems(string content)
    {
        foreach (var item in ConfigItems)
        {
            item.PropertyChanged -= OnConfigItemChanged;
        }

        ConfigItems.Clear();
        YamlTreeNodes.Clear();
        _allConfigLines.Clear();

        IsYamlMode = IsYamlFile();
        IsXmlMode = IsXmlFile();

        if (IsXmlMode)
        {
            return;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var lineItem = ParseLine(line);
            _allConfigLines.Add(lineItem);

            if (!lineItem.IsEditable)
            {
                continue;
            }

            lineItem.PropertyChanged += OnConfigItemChanged;
            ConfigItems.Add(lineItem);
        }

        RebuildYamlTreeNodes();
    }

    private ConfigKeyValueItem ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new ConfigKeyValueItem
            {
                IsBlank = true,
                OriginalLine = line,
                IsEditable = false
            };
        }

        var trimmedStart = line.TrimStart();
        if (trimmedStart.StartsWith("#") || trimmedStart.StartsWith(";"))
        {
            return new ConfigKeyValueItem
            {
                IsComment = true,
                OriginalLine = line,
                IsEditable = false
            };
        }

        if (IsYamlFile())
        {
            var indentLength = line.Length - trimmedStart.Length;
            var leadingWhitespace = indentLength > 0 ? new string(' ', indentLength) : string.Empty;
            var content = trimmedStart;
            var isListItem = false;

            if (content.StartsWith("- "))
            {
                isListItem = true;
                content = content.Substring(2).TrimStart();
            }

            if (content.EndsWith(":"))
            {
                var containerKey = content.Substring(0, content.Length - 1).Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(containerKey))
                {
                    return new ConfigKeyValueItem
                    {
                        IsEditable = false,
                        IsYamlContainer = true,
                        Key = containerKey,
                        Separator = ':',
                        LeadingWhitespace = leadingWhitespace,
                        IsYamlListItem = isListItem,
                        OriginalLine = line
                    };
                }
            }

            var yamlSeparatorIndex = content.IndexOf(':');
            if (yamlSeparatorIndex <= 0)
            {
                return new ConfigKeyValueItem
                {
                    OriginalLine = line,
                    IsEditable = false
                };
            }

            var key = content.Substring(0, yamlSeparatorIndex).Trim().Trim('"', '\'');
            var value = content.Substring(yamlSeparatorIndex + 1).Trim();

            return new ConfigKeyValueItem
            {
                IsEditable = true,
                Key = key,
                Value = value,
                Separator = ':',
                LeadingWhitespace = leadingWhitespace,
                IsYamlListItem = isListItem,
                OriginalLine = line
            };
        }

        var separatorIndex = GetSeparatorIndex(line, out var separator);
        if (separatorIndex <= 0)
        {
            return new ConfigKeyValueItem
            {
                OriginalLine = line,
                IsEditable = false
            };
        }

        var keyText = line.Substring(0, separatorIndex).Trim().Trim('"', '\'');
        var valueText = line.Substring(separatorIndex + 1).Trim();

        return new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = keyText,
            Value = valueText,
            Separator = separator,
            OriginalLine = line
        };
    }

    private static int GetSeparatorIndex(string line, out char separator)
    {
        separator = '=';

        var eqIndex = line.IndexOf('=');
        var colonIndex = line.IndexOf(':');

        if (eqIndex < 0 && colonIndex < 0)
        {
            return -1;
        }

        if (eqIndex >= 0 && (colonIndex < 0 || eqIndex < colonIndex))
        {
            separator = '=';
            return eqIndex;
        }

        separator = ':';
        return colonIndex;
    }

    private bool IsYamlFile()
    {
        return ConfigFileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               ConfigFileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsXmlFile()
    {
        return ConfigFileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private char GetDefaultSeparator()
    {
        if (IsYamlFile())
        {
            return ':';
        }

        return '=';
    }

    private void OnConfigItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigKeyValueItem.Key) || e.PropertyName == nameof(ConfigKeyValueItem.Value))
        {
            SyncContentFromConfigItems();
        }
    }

    private void SyncContentFromConfigItems()
    {
        if (IsXmlMode)
        {
            return;
        }

        var lines = new List<string>(_allConfigLines.Count);

        foreach (var item in _allConfigLines)
        {
            if (!item.IsEditable)
            {
                if (item.IsYamlContainer && item.Separator == ':')
                {
                    var yamlPrefix = item.IsYamlListItem ? "- " : string.Empty;
                    lines.Add($"{item.LeadingWhitespace}{yamlPrefix}{item.Key}:");
                }
                else
                {
                    lines.Add(item.OriginalLine);
                }

                continue;
            }

            var key = item.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = item.Value ?? string.Empty;
            if (item.Separator == ':')
            {
                var yamlPrefix = item.IsYamlListItem ? "- " : string.Empty;
                lines.Add($"{item.LeadingWhitespace}{yamlPrefix}{key}: {value}");
            }
            else
            {
                lines.Add($"{key} = {value}");
            }
        }

        ConfigFileContent = string.Join(Environment.NewLine, lines);

    }

    private void AddYamlItem()
    {
        var parentNode = SelectedYamlNode;
        var targetLevel = parentNode != null ? parentNode.Level + 1 : 0;
        var newItem = new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = "newKey",
            Value = string.Empty,
            Separator = ':',
            LeadingWhitespace = new string(' ', targetLevel * 2),
            OriginalLine = string.Empty
        };

        newItem.PropertyChanged += OnConfigItemChanged;
        var insertIndex = GetYamlInsertIndex(parentNode);
        _allConfigLines.Insert(insertIndex, newItem);
        ConfigItems.Add(newItem);
        SelectedConfigItem = newItem;

        SyncContentFromConfigItems();
        SelectYamlNodeBySourceItem(newItem);
    }

    private int GetYamlInsertIndex(YamlTreeNode? parentNode)
    {
        if (parentNode?.SourceItem == null)
        {
            return _allConfigLines.Count;
        }

        var parentIndex = _allConfigLines.IndexOf(parentNode.SourceItem);
        if (parentIndex < 0)
        {
            return _allConfigLines.Count;
        }

        var parentLevel = parentNode.Level;
        for (var i = parentIndex + 1; i < _allConfigLines.Count; i++)
        {
            var lineItem = _allConfigLines[i];
            if (lineItem.Separator != ':')
            {
                continue;
            }

            var lineLevel = lineItem.LeadingWhitespace.Length / 2;
            if (lineLevel <= parentLevel)
            {
                return i;
            }
        }

        return _allConfigLines.Count;
    }

    private void RemoveSelectedYamlNode()
    {
        if (SelectedYamlNode?.SourceItem == null)
        {
            return;
        }

        var startIndex = _allConfigLines.IndexOf(SelectedYamlNode.SourceItem);
        if (startIndex < 0)
        {
            return;
        }

        var startLevel = SelectedYamlNode.Level;
        var endIndex = startIndex + 1;
        while (endIndex < _allConfigLines.Count)
        {
            var lineItem = _allConfigLines[endIndex];
            if (lineItem.Separator == ':')
            {
                var level = lineItem.LeadingWhitespace.Length / 2;
                if (level <= startLevel)
                {
                    break;
                }
            }

            endIndex++;
        }

        for (var i = endIndex - 1; i >= startIndex; i--)
        {
            var lineItem = _allConfigLines[i];
            if (lineItem.IsEditable)
            {
                lineItem.PropertyChanged -= OnConfigItemChanged;
                ConfigItems.Remove(lineItem);
            }

            _allConfigLines.RemoveAt(i);
        }

        SelectedYamlNode = null;
        SelectedConfigItem = null;
        SyncContentFromConfigItems();
    }

    private void SelectYamlNodeBySourceItem(ConfigKeyValueItem sourceItem)
    {
        foreach (var root in YamlTreeNodes)
        {
            var found = FindYamlNodeBySourceItem(root, sourceItem);
            if (found != null)
            {
                SelectedYamlNode = found;
                return;
            }
        }
    }

    private static YamlTreeNode? FindYamlNodeBySourceItem(YamlTreeNode node, ConfigKeyValueItem sourceItem)
    {
        if (ReferenceEquals(node.SourceItem, sourceItem))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindYamlNodeBySourceItem(child, sourceItem);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void RebuildYamlTreeNodes()
    {
        YamlTreeNodes.Clear();
        if (!IsYamlMode)
        {
            return;
        }

        var roots = new List<(int Level, YamlTreeNode Node)>();
        var stack = new Stack<(int Level, YamlTreeNode Node)>();

        foreach (var item in _allConfigLines)
        {
            if (item.Separator != ':' || (!item.IsEditable && !item.IsYamlContainer))
            {
                continue;
            }

            var level = item.LeadingWhitespace.Length / 2;
            var node = new YamlTreeNode
            {
                DisplayKey = item.IsYamlListItem ? $"- {item.Key}" : item.Key,
                Value = item.Value ?? string.Empty,
                IsEditable = item.IsEditable,
                SourceItem = item,
                Level = level
            };

            while (stack.Count > 0 && stack.Peek().Level >= level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add((level, node));
            }
            else
            {
                node.Parent = stack.Peek().Node;
                stack.Peek().Node.Children.Add(node);
            }

            stack.Push((level, node));
        }

        foreach (var (_, node) in roots)
        {
            YamlTreeNodes.Add(node);
        }
    }

    private string GetRemoteConfigPath()
    {
        var dir = ConfigDirectory.TrimEnd('/');
        return $"{dir}/{ConfigFileName}";
    }

    private bool CanLoadConfig() => !IsBusy && !string.IsNullOrWhiteSpace(ConfigDirectory) && !string.IsNullOrWhiteSpace(ConfigFileName);
    private bool CanSaveConfig() => !IsBusy && !string.IsNullOrWhiteSpace(ConfigFileContent);

    [RelayCommand(CanExecute = nameof(CanLoadConfig))]
    private async Task LoadConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在加载配置文件...",
            async token =>
            {
                var remoteConfigPath = GetRemoteConfigPath();
                AppendActivity($"加载配置文件: {remoteConfigPath}");
                ConfigFileContent = await _customApplicationService.LoadConfigContentAsync(_host, remoteConfigPath, token);
                LoadConfigItems(ConfigFileContent);
                AppendActivity("配置文件加载完成", LogLevel.Success);
                StatusMessage = "配置文件已加载，可按键值编辑";
            },
            "加载配置文件失败");
    }

    [RelayCommand(CanExecute = nameof(CanSaveConfig))]
    private async Task SaveConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在保存配置文件...",
            async token =>
            {
                if (!IsXmlMode)
                {
                    SyncContentFromConfigItems();
                }

                var remoteConfigPath = GetRemoteConfigPath();
                AppendActivity($"保存配置文件: {remoteConfigPath}");
                await _customApplicationService.SaveConfigContentAsync(_host, remoteConfigPath, ConfigFileContent, token);
                AppendActivity("配置文件保存完成", LogLevel.Success);
                StatusMessage = "配置文件已保存";
            },
            "保存配置文件失败");
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
