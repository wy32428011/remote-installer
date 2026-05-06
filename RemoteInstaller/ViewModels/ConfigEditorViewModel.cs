using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// ViewModel for remote config file editing.
/// </summary>
public partial class ConfigEditorViewModel : ObservableObject
{
    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly string _softwareName;
    private readonly OperatingSystemType _osType;
    private static readonly string[] ElasticsearchServicePaths =
    [
        "/etc/systemd/system/elasticsearch.service",
        "/usr/lib/systemd/system/elasticsearch.service",
        "/lib/systemd/system/elasticsearch.service"
    ];

    private string _savedContent;
    private CancellationTokenSource? _cts;
    private bool _suppressSelectedFileChange;
    private long _fileSwitchVersion;

    public Action? CloseAction { get; set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _configFilePath = string.Empty;

    [ObservableProperty]
    private string _configContent = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public ConfigEditingSession EditingSession { get; } = new();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _supportsRestart = true;

    [ObservableProperty]
    private bool _isElasticsearchMemoryAdvancedMode;

    [ObservableProperty]
    private string _memoryLimit = string.Empty;

    [ObservableProperty]
    private string _jvmXms = string.Empty;

    [ObservableProperty]
    private string _jvmXmx = string.Empty;

    [ObservableProperty]
    private string _memoryConfigHint = string.Empty;

    public bool IsRawTextMode => EditingSession.IsRawTextMode;

    public bool IsYamlMode => EditingSession.IsYamlMode;

    public bool IsXmlMode => EditingSession.IsXmlMode;

    public ObservableCollection<ConfigKeyValueItem> ConfigItems => EditingSession.ConfigItems;

    public ObservableCollection<YamlTreeNode> YamlTreeNodes => EditingSession.YamlTreeNodes;

    public ConfigKeyValueItem? SelectedItem
    {
        get => EditingSession.SelectedItem;
        set => EditingSession.SelectedItem = value;
    }

    public YamlTreeNode? SelectedYamlNode
    {
        get => EditingSession.SelectedYamlNode;
        set => EditingSession.SelectedYamlNode = value;
    }

    public bool CanSwitchToStructuredMode => EditingSession.CanEnterStructuredMode;

    public bool CanSwitchToTextMode => EditingSession.CanEnterTextMode;

    public bool ShowKeyValueEditor => EditingSession.ShowKeyValueEditor;

    public bool ShowYamlEditor => EditingSession.ShowYamlEditor;

    public bool ShowTextEditor => EditingSession.ShowTextEditor;

    public bool ShowStructuredActions => EditingSession.ShowStructuredActions;

    public bool SupportsStructuredEditing => EditingSession.SupportsStructuredEditing;

    public bool IsElasticsearchJvmOptions => string.Equals(_softwareName, "Elasticsearch", StringComparison.OrdinalIgnoreCase) && ConfigFilePath.EndsWith("jvm.options", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private ObservableCollection<ConfigurationService.ConfigFileOption> _availableFiles = new();

    [ObservableProperty]
    private ConfigurationService.ConfigFileOption? _selectedFile;

    public bool SupportsFileSwitch => AvailableFiles.Count > 1;

    public ConfigEditorViewModel(
        ConfigurationService configurationService,
        RemoteHost host,
        string softwareName,
        OperatingSystemType osType,
        string configFilePath,
        string configContent,
        bool supportsRestart = true,
        IEnumerable<ConfigurationService.ConfigFileOption>? switchableFiles = null)
    {
        _configurationService = configurationService;
        _host = host;
        _softwareName = softwareName;
        _osType = osType;
        _configFilePath = configFilePath;
        _configContent = configContent;
        _savedContent = configContent;
        _supportsRestart = supportsRestart;

        EditingSession.PropertyChanged += OnEditingSessionPropertyChanged;

        if (switchableFiles != null)
        {
            foreach (var file in switchableFiles)
            {
                AvailableFiles.Add(file);
            }
        }

        var currentFileOption = EnsureCurrentFileOption(configFilePath);

        _suppressSelectedFileChange = true;
        SelectedFile = currentFileOption;
        _suppressSelectedFileChange = false;

        UpdateTitle();

        EditingSession.Load(configFilePath, configContent);
        ConfigContent = EditingSession.Content;
        UpdateElasticsearchMemoryState();
        RaiseEditingStateChanged();
        StatusMessage = $"已加载 {ConfigItems.Count} 个配置项";

        IsLoading = false;
    }

    partial void OnIsModifiedChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAndRestartCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAndRestartCommand.NotifyCanExecuteChanged();
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSupportsRestartChanged(bool value)
    {
        SaveAndRestartCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFileChanged(ConfigurationService.ConfigFileOption? value)
    {
        if (_suppressSelectedFileChange || value == null || value.RemotePath == ConfigFilePath || IsLoading)
        {
            return;
        }

        _ = SwitchToFileAsync(value);
    }

    partial void OnMemoryLimitChanged(string value)
    {
        if (!IsElasticsearchJvmOptions || IsElasticsearchMemoryAdvancedMode)
        {
            return;
        }

        if (!string.Equals(JvmXms, value, StringComparison.Ordinal))
        {
            JvmXms = value;
        }

        if (!string.Equals(JvmXmx, value, StringComparison.Ordinal))
        {
            JvmXmx = value;
        }

        MemoryConfigHint = string.Empty;
        IsModified = true;
    }

    partial void OnJvmXmsChanged(string value)
    {
        if (IsElasticsearchJvmOptions)
        {
            IsModified = true;
        }
    }

    partial void OnJvmXmxChanged(string value)
    {
        if (IsElasticsearchJvmOptions)
        {
            IsModified = true;
        }
    }

    private void OnEditingSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigEditingSession.Content) && ConfigContent != EditingSession.Content)
        {
            ConfigContent = EditingSession.Content;
        }
        else if (e.PropertyName == nameof(ConfigEditingSession.IsModified))
        {
            IsModified = EditingSession.IsModified || ConfigContent != _savedContent;
        }

        RaiseEditingStateChanged();

        if (e.PropertyName == nameof(ConfigEditingSession.Content))
        {
            UpdateElasticsearchMemoryState();
            OnPropertyChanged(nameof(IsElasticsearchJvmOptions));
        }
    }

    private void RaiseEditingStateChanged()
    {
        OnPropertyChanged(nameof(IsRawTextMode));
        OnPropertyChanged(nameof(IsYamlMode));
        OnPropertyChanged(nameof(IsXmlMode));
        OnPropertyChanged(nameof(ConfigItems));
        OnPropertyChanged(nameof(YamlTreeNodes));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedYamlNode));
        OnPropertyChanged(nameof(CanSwitchToStructuredMode));
        OnPropertyChanged(nameof(CanSwitchToTextMode));
        OnPropertyChanged(nameof(ShowKeyValueEditor));
        OnPropertyChanged(nameof(ShowYamlEditor));
        OnPropertyChanged(nameof(ShowTextEditor));
        OnPropertyChanged(nameof(ShowStructuredActions));
        OnPropertyChanged(nameof(SupportsStructuredEditing));
        AddItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
        SwitchToStructuredModeCommand.NotifyCanExecuteChanged();
        SwitchToTextModeCommand.NotifyCanExecuteChanged();
    }

    private ConfigurationService.ConfigFileOption? FindFileOption(string remotePath)
    {
        foreach (var file in AvailableFiles)
        {
            if (string.Equals(file.RemotePath, remotePath, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private ConfigurationService.ConfigFileOption EnsureCurrentFileOption(string remotePath)
    {
        var existingOption = FindFileOption(remotePath);
        if (existingOption != null)
        {
            return existingOption;
        }

        var displayName = System.IO.Path.GetFileName(remotePath);
        var currentOption = new ConfigurationService.ConfigFileOption
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? remotePath : displayName,
            RemotePath = remotePath
        };

        AvailableFiles.Insert(0, currentOption);
        return currentOption;
    }

    private void RestoreSelectedFile(string remotePath)
    {
        _suppressSelectedFileChange = true;
        SelectedFile = EnsureCurrentFileOption(remotePath);
        _suppressSelectedFileChange = false;
    }

    private async Task SwitchToFileAsync(ConfigurationService.ConfigFileOption targetFile)
    {
        var switchVersion = Interlocked.Increment(ref _fileSwitchVersion);
        var originalPath = ConfigFilePath;

        if (IsModified)
        {
            var result = MessageBox.Show(
                "当前文件有未保存修改。是否先保存后再切换？",
                "切换文件",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                RestoreSelectedFile(originalPath);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                await SaveAsync();
                if (switchVersion != _fileSwitchVersion)
                {
                    return;
                }

                if (IsModified)
                {
                    RestoreSelectedFile(originalPath);
                    return;
                }
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"正在加载 {targetFile.DisplayName}...";

            var content = await _configurationService.ReadConfigAsync(targetFile.RemotePath);
            if (switchVersion != _fileSwitchVersion)
            {
                return;
            }

            ConfigFilePath = targetFile.RemotePath;
            _savedContent = content;
            EditingSession.Load(targetFile.RemotePath, content, EditingSession.CurrentEditMode);
            ConfigContent = EditingSession.Content;
            IsModified = false;
            UpdateElasticsearchMemoryState();
            OnPropertyChanged(nameof(IsElasticsearchJvmOptions));
            RestoreSelectedFile(targetFile.RemotePath);
            UpdateTitle();
            StatusMessage = $"已加载 {targetFile.DisplayName}";
        }
        catch (Exception ex)
        {
            if (switchVersion != _fileSwitchVersion)
            {
                return;
            }

            StatusMessage = $"加载配置失败: {ex.Message}";
            MessageBox.Show(
                $"加载配置文件失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            RestoreSelectedFile(originalPath);
        }
        finally
        {
            if (switchVersion == _fileSwitchVersion)
            {
                IsLoading = false;
            }
        }
    }

    partial void OnConfigContentChanged(string value)
    {
        if (value == EditingSession.Content)
        {
            return;
        }

        EditingSession.ApplyExternalContent(value, markAsSaved: false);
    }

    private void UpdateTitle()
    {
        var fileName = System.IO.Path.GetFileName(ConfigFilePath);
        Title = string.IsNullOrWhiteSpace(fileName)
            ? $"编辑{_softwareName}配置"
            : $"编辑{_softwareName}配置 - {fileName}";
    }

    private void UpdateElasticsearchMemoryState()
    {
        if (!IsElasticsearchJvmOptions)
        {
            IsElasticsearchMemoryAdvancedMode = false;
            MemoryLimit = string.Empty;
            JvmXms = string.Empty;
            JvmXmx = string.Empty;
            MemoryConfigHint = string.Empty;
            return;
        }

        IsElasticsearchMemoryAdvancedMode = true;
        JvmXms = ExtractJvmOptionValue(ConfigContent, "-Xms");
        JvmXmx = ExtractJvmOptionValue(ConfigContent, "-Xmx");

        if (!string.IsNullOrWhiteSpace(JvmXms) &&
            !string.IsNullOrWhiteSpace(JvmXmx) &&
            string.Equals(JvmXms, JvmXmx, StringComparison.Ordinal))
        {
            MemoryLimit = JvmXms;
            MemoryConfigHint = "当前堆内存配置已保持 Xms 与 Xmx 一致。";
            return;
        }

        MemoryLimit = string.Empty;
        MemoryConfigHint = "当前 Xms 与 Xmx 值不一致，建议保持一致。";
    }

    private static string ExtractJvmOptionValue(string content, string optionName)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(optionName))
        {
            return string.Empty;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var commentIndex = trimmedLine.IndexOf('#');
            if (commentIndex >= 0)
            {
                trimmedLine = trimmedLine.Substring(0, commentIndex).TrimEnd();
            }

            var optionStartIndex = trimmedLine.IndexOf(optionName, StringComparison.Ordinal);
            if (optionStartIndex < 0)
            {
                continue;
            }

            if (optionStartIndex > 0 && trimmedLine[optionStartIndex - 1] != ':')
            {
                continue;
            }

            return trimmedLine.Substring(optionStartIndex + optionName.Length).Trim();
        }

        return string.Empty;
    }

    private static string UpsertElasticsearchJavaOpts(string serviceContent, string xms, string xmx)
    {
        var expectedLine = $"Environment=\"ES_JAVA_OPTS=-Xms{xms} -Xmx{xmx}\"";
        var normalizedContent = serviceContent.Replace("\r\n", "\n");
        var lines = normalizedContent.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("ES_JAVA_OPTS", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = expectedLine;
                return string.Join(Environment.NewLine, lines);
            }
        }

        var result = new List<string>(lines);
        var workingDirectoryIndex = result.FindIndex(line =>
            line.Trim().StartsWith("WorkingDirectory=", StringComparison.OrdinalIgnoreCase));
        if (workingDirectoryIndex >= 0)
        {
            result.Insert(workingDirectoryIndex, expectedLine);
            return string.Join(Environment.NewLine, result);
        }

        var insertIndex = result.FindLastIndex(line =>
        {
            var trimmedLine = line.Trim();
            return trimmedLine.StartsWith("Environment=", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.StartsWith("RestartSec=", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.StartsWith("Restart=", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.StartsWith("PIDFile=", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.StartsWith("ExecStop=", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.StartsWith("ExecStart=", StringComparison.OrdinalIgnoreCase);
        });

        result.Insert(insertIndex >= 0 ? insertIndex + 1 : result.Count, expectedLine);
        return string.Join(Environment.NewLine, result);
    }

    private async Task UpdateElasticsearchServiceMemoryAsync(string xms, string xmx, CancellationToken cancellationToken)
    {
        foreach (var servicePath in ElasticsearchServicePaths)
        {
            if (!await _configurationService.FileExistsAsync(servicePath, cancellationToken))
            {
                continue;
            }

            var currentContent = await _configurationService.ReadConfigAsync(servicePath, cancellationToken);
            var updatedContent = UpsertElasticsearchJavaOpts(currentContent, xms, xmx);
            await _configurationService.SaveConfigAsync(_host, servicePath, updatedContent, _osType, backup: true, cancellationToken: cancellationToken);
            StatusMessage = $"已同步 Elasticsearch 服务内存配置: {servicePath}";
            return;
        }

        StatusMessage = "未找到 Elasticsearch service 文件，已仅保存 jvm.options";
    }

    private void ApplyElasticsearchMemoryToConfigContent()
    {
        if (!IsElasticsearchJvmOptions)
        {
            return;
        }

        var xms = IsElasticsearchMemoryAdvancedMode && !string.IsNullOrWhiteSpace(JvmXms)
            ? JvmXms
            : MemoryLimit;
        var xmx = IsElasticsearchMemoryAdvancedMode && !string.IsNullOrWhiteSpace(JvmXmx)
            ? JvmXmx
            : MemoryLimit;

        if (string.IsNullOrWhiteSpace(xms) || string.IsNullOrWhiteSpace(xmx))
        {
            return;
        }

        var lines = ConfigContent.Replace("\r\n", "\n").Split('\n');
        var updatedLines = new List<string>();
        var sawXms = false;
        var sawXmx = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Contains(":-Xms", StringComparison.OrdinalIgnoreCase))
            {
                updatedLines.Add($"-Xms{xms}");
                sawXms = true;
                continue;
            }

            if (trimmedLine.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Contains(":-Xmx", StringComparison.OrdinalIgnoreCase))
            {
                updatedLines.Add($"-Xmx{xmx}");
                sawXmx = true;
                continue;
            }

            updatedLines.Add(line);
        }

        if (!sawXms)
        {
            updatedLines.Add($"-Xms{xms}");
        }

        if (!sawXmx)
        {
            updatedLines.Add($"-Xmx{xmx}");
        }

        ConfigContent = string.Join(Environment.NewLine, updatedLines);
    }

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        EditingSession.AddItem();
        ConfigContent = EditingSession.Content;
    }

    private bool CanAddItem() => !IsSaving && EditingSession.ShowStructuredActions;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedItem))]
    private void RemoveSelectedItem()
    {
        EditingSession.RemoveSelectedItem();
        ConfigContent = EditingSession.Content;
    }

    private bool CanRemoveSelectedItem()
    {
        if (IsSaving || !EditingSession.ShowStructuredActions)
        {
            return false;
        }

        return IsYamlMode ? SelectedYamlNode != null : SelectedItem != null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsSaving || !IsModified)
        {
            return;
        }

        IsSaving = true;
        StatusMessage = "正在保存配置...";
        _cts = new CancellationTokenSource();

        try
        {
            ConfigContent = EditingSession.GetContentForSave();
            ApplyElasticsearchMemoryToConfigContent();
            EditingSession.ApplyExternalContent(ConfigContent, markAsSaved: false);

            var backupPath = await _configurationService.SaveConfigAsync(
                _host,
                ConfigFilePath,
                ConfigContent,
                _osType,
                backup: true,
                cancellationToken: _cts.Token);

            var statusMessage = $"保存完成。备份: {backupPath}";
            if (IsElasticsearchJvmOptions)
            {
                var xms = string.IsNullOrWhiteSpace(JvmXms) ? MemoryLimit : JvmXms;
                var xmx = string.IsNullOrWhiteSpace(JvmXmx) ? MemoryLimit : JvmXmx;
                if (!string.IsNullOrWhiteSpace(xms) && !string.IsNullOrWhiteSpace(xmx))
                {
                    await UpdateElasticsearchServiceMemoryAsync(xms, xmx, _cts.Token);
                    statusMessage = StatusMessage;
                }
            }

            _savedContent = ConfigContent;
            EditingSession.ApplyExternalContent(ConfigContent, markAsSaved: true);
            IsModified = false;
            StatusMessage = statusMessage;

            MessageBox.Show(
                $"配置保存成功。\n备份路径: {backupPath}",
                "保存完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
            MessageBox.Show(
                $"保存配置失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsSaving = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanSave() => IsModified && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSaveAndRestart))]
    private async Task SaveAndRestartAsync()
    {
        if (!SupportsRestart || IsSaving || !IsModified)
        {
            return;
        }

        await SaveAsync();
        if (IsModified)
        {
            return;
        }

        StatusMessage = "正在重启服务...";
        try
        {
            await _configurationService.RestartServiceAsync(_softwareName, _cts?.Token ?? default);
            StatusMessage = "服务重启成功";

            MessageBox.Show(
                "配置已保存，服务已成功重启。",
                "操作完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CloseAction?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"重启失败: {ex.Message}";
            MessageBox.Show(
                $"配置已保存，但重启失败: {ex.Message}\n请手动重启服务。",
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool CanSaveAndRestart() => SupportsRestart && CanSave();

    [RelayCommand(CanExecute = nameof(CanExecuteSwitchToStructuredMode))]
    private void SwitchToStructuredMode()
    {
        if (EditingSession.TrySwitchToStructuredMode(out var errorMessage))
        {
            ConfigContent = EditingSession.Content;
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
        ConfigContent = EditingSession.Content;
    }

    private bool CanExecuteSwitchToTextMode() => EditingSession.CanEnterTextMode;

    private async Task CloseAfterSaveAsync()
    {
        await SaveAsync();
        if (!IsModified)
        {
            CloseAction?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsModified)
        {
            var result = MessageBox.Show(
                "配置已修改，关闭前是否保存？",
                "确认关闭",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = CloseAfterSaveAsync();
                return;
            }

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        CloseAction?.Invoke();
    }

}
