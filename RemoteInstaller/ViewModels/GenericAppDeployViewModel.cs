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

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 自定义应用部署 ViewModel（用于 WMS/WCS/FMS）
/// </summary>
public partial class GenericAppDeployViewModel : ObservableObject
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
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedConfigItemCommand))]
    private ConfigKeyValueItem? _selectedConfigItem;

    [ObservableProperty]
    private YamlTreeNode? _selectedYamlNode;

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
                LoadConfigItems(ConfigFileContent);
                AppendActivity("配置加载完成。", LogLevel.Success);
                StatusMessage = "配置文件已加载，可按键值编辑";
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
                if (!IsXmlMode)
                {
                    SyncContentFromConfigItems();
                }

                AppendActivity($"保存配置文件: {ConfigFilePath}");
                await _customApplicationService.SaveConfigContentAsync(_host, ConfigFilePath, ConfigFileContent, token);
                AppendActivity("配置文件保存完成", LogLevel.Success);
                StatusMessage = "配置文件已保存";
            },
            "保存配置文件失败");
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
        !IsBusy && !string.IsNullOrWhiteSpace(ConfigFilePath) && !string.IsNullOrWhiteSpace(ConfigFileContent);

    private bool CanAddConfigItem() =>
        !IsBusy && !IsXmlMode && !string.IsNullOrWhiteSpace(ConfigFilePath);

    private bool CanRemoveSelectedConfigItem()
    {
        if (IsBusy || IsXmlMode)
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
        return ConfigFilePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               ConfigFilePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsXmlFile()
    {
        return ConfigFilePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
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
