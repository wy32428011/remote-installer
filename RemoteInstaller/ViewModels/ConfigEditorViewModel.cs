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

namespace RemoteInstaller.ViewModels;

/// <summary>
/// ViewModel for remote config file editing.
/// </summary>
public partial class ConfigEditorViewModel : ObservableObject
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

    private readonly ConfigurationService _configurationService;
    private readonly RemoteHost _host;
    private readonly string _softwareName;
    private readonly OperatingSystemType _osType;
    private readonly List<ConfigKeyValueItem> _allConfigLines = new();
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

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _supportsRestart = true;

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
    private ConfigKeyValueItem? _selectedItem;

    [ObservableProperty]
    private YamlTreeNode? _selectedYamlNode;

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

        LoadConfigItems(_configContent);
        StatusMessage = $"已加载 {ConfigItems.Count} 个配置项";

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigContent))
            {
                IsModified = ConfigContent != _savedContent;
            }
        };

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

    partial void OnSelectedItemChanged(ConfigKeyValueItem? value)
    {
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsYamlModeChanged(bool value)
    {
        AddItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsXmlModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRawTextMode));
        AddItemCommand.NotifyCanExecuteChanged();
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedYamlNodeChanged(YamlTreeNode? value)
    {
        RemoveSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFileChanged(ConfigurationService.ConfigFileOption? value)
    {
        if (_suppressSelectedFileChange || value == null || value.RemotePath == ConfigFilePath || IsLoading)
        {
            return;
        }

        _ = SwitchToFileAsync(value);
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
            ConfigContent = content;
            _savedContent = content;
            IsModified = false;
            LoadConfigItems(content);
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

    private void UpdateTitle()
    {
        var fileName = System.IO.Path.GetFileName(ConfigFilePath);
        Title = string.IsNullOrWhiteSpace(fileName)
            ? $"编辑{_softwareName}配置"
            : $"编辑{_softwareName}配置 - {fileName}";
    }

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
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
        SelectedItem = item;
        SyncContentFromItems();
    }

    private bool CanAddItem() => !IsSaving && !IsXmlMode;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedItem))]
    private void RemoveSelectedItem()
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

        if (SelectedItem == null)
        {
            return;
        }

        SelectedItem.PropertyChanged -= OnConfigItemChanged;
        _allConfigLines.Remove(SelectedItem);
        ConfigItems.Remove(SelectedItem);
        SelectedItem = null;
        SyncContentFromItems();
    }

    private bool CanRemoveSelectedItem()
    {
        if (IsSaving || IsXmlMode)
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
            if (!IsXmlMode)
            {
                SyncContentFromItems();
            }

            var backupPath = await _configurationService.SaveConfigAsync(
                _host,
                ConfigFilePath,
                ConfigContent,
                _osType,
                backup: true,
                cancellationToken: _cts.Token);

            _savedContent = ConfigContent;
            IsModified = false;
            StatusMessage = $"保存完成。备份: {backupPath}";

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

        var plainKey = line.Substring(0, separatorIndex).Trim().Trim('"', '\'');
        var plainValue = line.Substring(separatorIndex + 1).Trim();

        return new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = plainKey,
            Value = plainValue,
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
            SyncContentFromItems();
        }
    }

    private void SyncContentFromItems()
    {
        if (IsXmlMode)
        {
            IsModified = ConfigContent != _savedContent;
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

        ConfigContent = string.Join(Environment.NewLine, lines);
        IsModified = ConfigContent != _savedContent;

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
        SelectedItem = newItem;

        SyncContentFromItems();
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
        SelectedItem = null;
        SyncContentFromItems();
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
}
