using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.ViewModels.Shared.ConfigEditing;

public enum ConfigEditMode
{
    Structured,
    Text
}

public enum StructuredEditorKind
{
    None,
    KeyValue,
    YamlTree
}

public partial class ConfigKeyValueItem : ObservableObject
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

public partial class ConfigEditingSession : ObservableObject
{
    private readonly List<ConfigKeyValueItem> _allConfigLines = new();
    private string _savedContent = string.Empty;
    private bool _suppressContentChange;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private ObservableCollection<ConfigKeyValueItem> _configItems = new();

    [ObservableProperty]
    private ObservableCollection<YamlTreeNode> _yamlTreeNodes = new();

    [ObservableProperty]
    private StructuredEditorKind _structuredEditorKind;

    [ObservableProperty]
    private ConfigEditMode _currentEditMode = ConfigEditMode.Text;

    [ObservableProperty]
    private ConfigKeyValueItem? _selectedItem;

    [ObservableProperty]
    private YamlTreeNode? _selectedYamlNode;

    public bool SupportsStructuredEditing => StructuredEditorKind != StructuredEditorKind.None;

    public bool IsYamlMode => StructuredEditorKind == StructuredEditorKind.YamlTree;

    public bool IsXmlMode => FilePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    public bool IsRawTextMode => CurrentEditMode == ConfigEditMode.Text;

    public bool IsStructuredMode => CurrentEditMode == ConfigEditMode.Structured;

    public bool ShowKeyValueEditor => CurrentEditMode == ConfigEditMode.Structured && StructuredEditorKind == StructuredEditorKind.KeyValue;

    public bool ShowYamlEditor => CurrentEditMode == ConfigEditMode.Structured && StructuredEditorKind == StructuredEditorKind.YamlTree;

    public bool ShowTextEditor => CurrentEditMode == ConfigEditMode.Text;

    public bool ShowStructuredActions => CurrentEditMode == ConfigEditMode.Structured && SupportsStructuredEditing;

    public bool CanEnterStructuredMode => SupportsStructuredEditing && CurrentEditMode != ConfigEditMode.Structured;

    public bool CanEnterTextMode => CurrentEditMode != ConfigEditMode.Text;

    public void Load(string filePath, string content, ConfigEditMode? preferredMode = null)
    {
        FilePath = filePath ?? string.Empty;
        ReplaceContent(content ?? string.Empty);
        LoadStructuredState(Content, strictValidation: false, out _);
        _savedContent = Content;
        IsModified = false;
        SelectedItem = null;
        SelectedYamlNode = null;

        var targetMode = SupportsStructuredEditing && preferredMode != ConfigEditMode.Text
            ? ConfigEditMode.Structured
            : ConfigEditMode.Text;

        if (preferredMode == ConfigEditMode.Structured && SupportsStructuredEditing)
        {
            targetMode = ConfigEditMode.Structured;
        }

        if (preferredMode == ConfigEditMode.Text)
        {
            targetMode = ConfigEditMode.Text;
        }

        CurrentEditMode = SupportsStructuredEditing && targetMode == ConfigEditMode.Structured
            ? ConfigEditMode.Structured
            : ConfigEditMode.Text;
    }

    public string GetContentForSave()
    {
        if (SupportsStructuredEditing && CurrentEditMode == ConfigEditMode.Structured)
        {
            SyncContentFromItems(rebuildYamlTree: false);
        }

        return Content;
    }

    public void ApplyExternalContent(string content, bool markAsSaved)
    {
        var preferredMode = CurrentEditMode;
        ReplaceContent(content ?? string.Empty);
        LoadStructuredState(Content, strictValidation: false, out _);

        if (markAsSaved)
        {
            _savedContent = Content;
            IsModified = false;
        }
        else
        {
            IsModified = Content != _savedContent;
        }

        CurrentEditMode = SupportsStructuredEditing && preferredMode == ConfigEditMode.Structured
            ? ConfigEditMode.Structured
            : ConfigEditMode.Text;
    }

    public void MarkModified()
    {
        IsModified = true;
    }

    public bool TrySwitchToTextMode()
    {
        if (CurrentEditMode == ConfigEditMode.Text)
        {
            return true;
        }

        if (SupportsStructuredEditing)
        {
            SyncContentFromItems(rebuildYamlTree: false);
        }

        CurrentEditMode = ConfigEditMode.Text;
        return true;
    }

    public bool TrySwitchToStructuredMode(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!SupportsStructuredEditing)
        {
            errorMessage = "当前文件仅支持文本模式。";
            return false;
        }

        if (CurrentEditMode == ConfigEditMode.Structured)
        {
            return true;
        }

        if (!LoadStructuredState(Content, strictValidation: true, out errorMessage))
        {
            return false;
        }

        CurrentEditMode = ConfigEditMode.Structured;
        return true;
    }

    public void AddItem()
    {
        if (!SupportsStructuredEditing || CurrentEditMode != ConfigEditMode.Structured)
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
        SyncContentFromItems(rebuildYamlTree: false);
    }

    public void RemoveSelectedItem()
    {
        if (!SupportsStructuredEditing || CurrentEditMode != ConfigEditMode.Structured)
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
        SyncContentFromItems(rebuildYamlTree: false);
    }

    private bool LoadStructuredState(string content, bool strictValidation, out string errorMessage)
    {
        errorMessage = string.Empty;
        ClearStructuredState();

        StructuredEditorKind = DetectStructuredEditorKind(FilePath);
        if (!SupportsStructuredEditing)
        {
            return true;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!TryParseLine(line, strictValidation, i + 1, out var lineItem, out errorMessage))
            {
                return false;
            }

            _allConfigLines.Add(lineItem);
            if (!lineItem.IsEditable)
            {
                continue;
            }

            lineItem.PropertyChanged += OnConfigItemChanged;
            ConfigItems.Add(lineItem);
        }

        RebuildYamlTreeNodes();
        return true;
    }

    private void ReplaceContent(string content)
    {
        _suppressContentChange = true;
        Content = content;
        _suppressContentChange = false;
    }

    private StructuredEditorKind DetectStructuredEditorKind(string filePath)
    {
        if (filePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredEditorKind.YamlTree;
        }

        if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredEditorKind.None;
        }

        return StructuredEditorKind.KeyValue;
    }

    private bool TryParseLine(string line, bool strictValidation, int lineNumber, out ConfigKeyValueItem lineItem, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            lineItem = new ConfigKeyValueItem
            {
                IsBlank = true,
                OriginalLine = line,
                IsEditable = false
            };
            return true;
        }

        var trimmedStart = line.TrimStart();
        if (trimmedStart.StartsWith("#", StringComparison.Ordinal) || trimmedStart.StartsWith(";", StringComparison.Ordinal))
        {
            lineItem = new ConfigKeyValueItem
            {
                IsComment = true,
                OriginalLine = line,
                IsEditable = false
            };
            return true;
        }

        if (StructuredEditorKind == StructuredEditorKind.YamlTree)
        {
            return TryParseYamlLine(line, strictValidation, lineNumber, out lineItem, out errorMessage);
        }

        var separatorIndex = GetSeparatorIndex(line, out var separator);
        if (separatorIndex <= 0)
        {
            lineItem = new ConfigKeyValueItem
            {
                OriginalLine = line,
                IsEditable = false
            };
            return true;
        }

        var keyText = line.Substring(0, separatorIndex).Trim().Trim('"', '\'');
        var valueText = line.Substring(separatorIndex + 1).Trim();

        lineItem = new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = keyText,
            Value = valueText,
            Separator = separator,
            OriginalLine = line
        };
        return true;
    }

    private static bool TryParseYamlLine(string line, bool strictValidation, int lineNumber, out ConfigKeyValueItem lineItem, out string errorMessage)
    {
        errorMessage = string.Empty;
        var trimmedStart = line.TrimStart();
        var indentLength = line.Length - trimmedStart.Length;

        if (strictValidation)
        {
            if (line.StartsWith("\t", StringComparison.Ordinal) || line[..indentLength].Contains('\t'))
            {
                lineItem = new ConfigKeyValueItem();
                errorMessage = $"第 {lineNumber} 行包含制表符缩进，无法切换到结构化模式。";
                return false;
            }

            if (indentLength % 2 != 0)
            {
                lineItem = new ConfigKeyValueItem();
                errorMessage = $"第 {lineNumber} 行缩进不是 2 的倍数，无法切换到结构化模式。";
                return false;
            }
        }

        var leadingWhitespace = indentLength > 0 ? new string(' ', indentLength) : string.Empty;
        var content = trimmedStart;
        var isListItem = false;

        if (content.StartsWith("- ", StringComparison.Ordinal))
        {
            isListItem = true;
            content = content[2..].TrimStart();
        }

        if (content.EndsWith(":", StringComparison.Ordinal))
        {
            var containerKey = content[..^1].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(containerKey))
            {
                lineItem = new ConfigKeyValueItem
                {
                    IsEditable = false,
                    IsYamlContainer = true,
                    Key = containerKey,
                    Separator = ':',
                    LeadingWhitespace = leadingWhitespace,
                    IsYamlListItem = isListItem,
                    OriginalLine = line
                };
                return true;
            }
        }

        var yamlSeparatorIndex = content.IndexOf(':');
        if (yamlSeparatorIndex <= 0)
        {
            if (strictValidation)
            {
                lineItem = new ConfigKeyValueItem();
                errorMessage = $"第 {lineNumber} 行不是可编辑的 YAML 键值结构，无法切换到结构化模式。";
                return false;
            }

            lineItem = new ConfigKeyValueItem
            {
                OriginalLine = line,
                IsEditable = false
            };
            return true;
        }

        var key = content[..yamlSeparatorIndex].Trim().Trim('"', '\'');
        var value = content[(yamlSeparatorIndex + 1)..].Trim();

        lineItem = new ConfigKeyValueItem
        {
            IsEditable = true,
            Key = key,
            Value = value,
            Separator = ':',
            LeadingWhitespace = leadingWhitespace,
            IsYamlListItem = isListItem,
            OriginalLine = line
        };
        return true;
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

    private char GetDefaultSeparator()
    {
        return StructuredEditorKind == StructuredEditorKind.YamlTree ? ':' : '=';
    }

    private void OnConfigItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigKeyValueItem.Key) || e.PropertyName == nameof(ConfigKeyValueItem.Value))
        {
            SyncContentFromItems(rebuildYamlTree: false);
        }
    }

    private void SyncContentFromItems(bool rebuildYamlTree)
    {
        if (!SupportsStructuredEditing)
        {
            IsModified = Content != _savedContent;
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

        ReplaceContent(string.Join(Environment.NewLine, lines));
        IsModified = Content != _savedContent;

        if (rebuildYamlTree)
        {
            RebuildYamlTreeNodes();
        }
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

        SyncContentFromItems(rebuildYamlTree: true);
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
        SyncContentFromItems(rebuildYamlTree: true);
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

    private void ClearStructuredState()
    {
        foreach (var item in ConfigItems)
        {
            item.PropertyChanged -= OnConfigItemChanged;
        }

        ConfigItems.Clear();
        YamlTreeNodes.Clear();
        _allConfigLines.Clear();
    }

    partial void OnContentChanged(string value)
    {
        if (_suppressContentChange)
        {
            return;
        }

        IsModified = value != _savedContent;
    }

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsXmlMode));
    }

    partial void OnStructuredEditorKindChanged(StructuredEditorKind value)
    {
        NotifyModeDependentProperties();
    }

    partial void OnCurrentEditModeChanged(ConfigEditMode value)
    {
        NotifyModeDependentProperties();
    }

    partial void OnSelectedItemChanged(ConfigKeyValueItem? value)
    {
        NotifyModeDependentProperties();
    }

    partial void OnSelectedYamlNodeChanged(YamlTreeNode? value)
    {
        NotifyModeDependentProperties();
    }

    private void NotifyModeDependentProperties()
    {
        OnPropertyChanged(nameof(SupportsStructuredEditing));
        OnPropertyChanged(nameof(IsYamlMode));
        OnPropertyChanged(nameof(IsRawTextMode));
        OnPropertyChanged(nameof(IsStructuredMode));
        OnPropertyChanged(nameof(ShowKeyValueEditor));
        OnPropertyChanged(nameof(ShowYamlEditor));
        OnPropertyChanged(nameof(ShowTextEditor));
        OnPropertyChanged(nameof(ShowStructuredActions));
        OnPropertyChanged(nameof(CanEnterStructuredMode));
        OnPropertyChanged(nameof(CanEnterTextMode));
    }
}
