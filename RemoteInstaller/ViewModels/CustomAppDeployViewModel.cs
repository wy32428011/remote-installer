using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.Views.Dialogs;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// ViewModel for custom application deployment.
/// </summary>
public partial class CustomAppDeployViewModel : ObservableObject
{
    private readonly CustomApplicationService _customApplicationService;
    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly Action<string, LogLevel>? _logSink;
    private readonly StringBuilder _activityBuilder = new();
    private CancellationTokenSource? _operationCts;

    [ObservableProperty]
    private string _windowTitle;

    [ObservableProperty]
    private string _hostDisplay;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadAndStartCommand))]
    private string _applicationName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadAndStartCommand))]
    private string _localSourcePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadAndStartCommand))]
    private string _remoteDirectory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadAndStartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartApplicationCommand))]
    private string _startCommand = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditConfigCommand))]
    private string _configFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadAndStartCommand))]
    private bool _preserveTopLevelDirectory;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _activityLog = string.Empty;

    public Action? CloseAction { get; set; }

    public string RemoteDirectoryHint => _host.OsType == OperatingSystemType.Windows
        ? @"示例: C:\apps\my-service"
        : "示例: /opt/apps/my-service";

    public string SourceSummary => GetSourceSummary();

    public string RemoteTargetPreview => GetRemoteTargetPreview();

    public bool HasActivity => !string.IsNullOrWhiteSpace(ActivityLog);

    public CustomAppDeployViewModel(
        CustomApplicationService customApplicationService,
        ConfigurationService configurationService,
        RemoteHost host,
        Action<string, LogLevel>? logSink = null)
    {
        _customApplicationService = customApplicationService;
        _configurationService = configurationService;
        _host = host;
        _logSink = logSink;

        WindowTitle = $"自定义应用部署 - {host.Name}";
        HostDisplay = $"{host.Name} ({host.IpAddress}:{host.Port})";
        RemoteDirectory = host.OsType == OperatingSystemType.Windows ? @"C:\apps" : "/opt/apps";
    }

    partial void OnLocalSourcePathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            var inferredName = InferApplicationName(value);
            if (!string.IsNullOrWhiteSpace(inferredName))
            {
                ApplicationName = inferredName;
            }
        }

        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(RemoteTargetPreview));
    }

    partial void OnRemoteDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteTargetPreview));
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
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用文件",
            Filter = "应用文件 (*.jar;*.zip;*.tar;*.gz;*.war)|*.jar;*.zip;*.tar;*.gz;*.war|所有文件 (*.*)|*.*",
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
        var dialog = new OpenFolderDialog
        {
            Title = "选择要上传的应用目录",
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
        await UploadCoreAsync(startAfterUpload: false);
    }

    [RelayCommand(CanExecute = nameof(CanUploadAndStart))]
    private async Task UploadAndStartAsync()
    {
        await UploadCoreAsync(startAfterUpload: true);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartApplicationAsync()
    {
        await RunStartCommandAsync(openedFromUpload: false);
    }

    [RelayCommand(CanExecute = nameof(CanEditConfig))]
    private async Task EditConfigAsync()
    {
        await RunBusyOperationAsync(
            "正在加载配置文件...",
            async token =>
            {
                AppendActivity($"正在加载配置: {ConfigFilePath}");
                var configContent = await _customApplicationService.LoadConfigContentAsync(_host, ConfigFilePath, token);
                AppendActivity("配置加载完成。", LogLevel.Success);

                var configViewModel = new ConfigEditorViewModel(
                    _configurationService,
                    _host,
                    string.IsNullOrWhiteSpace(ApplicationName) ? "自定义应用" : ApplicationName,
                    _host.OsType,
                    ConfigFilePath,
                    configContent,
                    supportsRestart: false);

                var dialog = new ConfigEditorDialog(configViewModel);
                var owner = Application.Current?.Windows.OfType<Window>().LastOrDefault(window => window.IsActive);
                if (owner != null)
                {
                    dialog.Owner = owner;
                }

                dialog.ShowDialog();
                AppendActivity("配置编辑器已关闭。");
                StatusMessage = "配置编辑器已关闭";
            },
            "加载配置失败");
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        AppendActivity("正在取消当前任务...", LogLevel.Warning);
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

    private bool CanUpload()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(LocalSourcePath) &&
               (File.Exists(LocalSourcePath) || Directory.Exists(LocalSourcePath)) &&
               !string.IsNullOrWhiteSpace(RemoteDirectory);
    }

    private bool CanUploadAndStart()
    {
        return CanUpload() && !string.IsNullOrWhiteSpace(StartCommand);
    }

    private bool CanStart()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(StartCommand);
    }

    private bool CanEditConfig()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ConfigFilePath);
    }

    private bool CanCancelOperation()
    {
        return IsBusy && _operationCts != null && !_operationCts.IsCancellationRequested;
    }

    private async Task UploadCoreAsync(bool startAfterUpload)
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

                if (startAfterUpload)
                {
                    await RunStartCommandAsync(openedFromUpload: true, token);
                }
            },
            "上传失败");
    }

    private async Task RunStartCommandAsync(bool openedFromUpload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(StartCommand))
        {
            if (openedFromUpload)
            {
                AppendActivity("启动命令为空。上传完成但未启动。", LogLevel.Warning);
            }

            return;
        }

        if (openedFromUpload)
        {
            AppendActivity("正在执行启动命令...");
            var output = await _customApplicationService.StartApplicationAsync(_host, StartCommand, cancellationToken);
            AppendActivity("启动命令执行完成。", LogLevel.Success);
            AppendCommandOutput(output);
            StatusMessage = "上传并启动完成";
            return;
        }

        await RunBusyOperationAsync(
            "正在执行启动命令...",
            async token =>
            {
                AppendActivity("正在执行启动命令...");
                var output = await _customApplicationService.StartApplicationAsync(_host, StartCommand, token);
                AppendActivity("启动命令执行完成。", LogLevel.Success);
                AppendCommandOutput(output);
                StatusMessage = "启动命令执行完成";
            },
            "启动命令执行失败");
    }

    private async Task RunBusyOperationAsync(string busyMessage, Func<CancellationToken, Task> action, string failureMessage)
    {
        if (IsBusy)
        {
            return;
        }

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
            MessageBox.Show(
                $"{failureMessage}: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var normalized = output.Trim();
        if (normalized.Length > 1200)
        {
            normalized = normalized[..1200] + Environment.NewLine + "...";
        }

        AppendActivity(normalized);
    }

    private void AppendActivity(string message, LogLevel level = LogLevel.Info)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_activityBuilder.Length > 0)
        {
            _activityBuilder.AppendLine();
        }

        _activityBuilder.Append(line);
        ActivityLog = _activityBuilder.ToString();
        OnPropertyChanged(nameof(HasActivity));
        _logSink?.Invoke(message, level);
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

    private static string InferApplicationName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (File.Exists(path))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).Name;
        }

        return string.Empty;
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
}
