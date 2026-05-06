# Elasticsearch Memory Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保持现有 UI 风格不变的前提下，为 Elasticsearch 补齐安装后可编辑 JVM 内存配置的能力，并将保存结果同步到 `jvm.options` 与 systemd service 的 `ES_JAVA_OPTS`。

**Architecture:** 继续复用现有 `ConfigEditorDialog` 与 `ConfigEditorViewModel`，不新增独立窗口。通过 `ConfigurationService` 为 Elasticsearch 增加多文件切换，默认仍打开 `elasticsearch.yml`；当切换到 `jvm.options` 时，由 `ConfigEditorViewModel` 激活 Elasticsearch 专用内存视图模型与保存同步逻辑，仅对该文件类型生效。

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, MaterialDesignInXaml, xUnit, shell config files (`jvm.options`, `systemd service`)

---

## File Map

- **Modify:** `RemoteInstaller/Services/ConfigurationService.cs`
  - 为 Elasticsearch 增加 switchable config files 定义与路径发现
- **Modify:** `RemoteInstaller/ViewModels/MainViewModel.cs`
  - 打开 Elasticsearch 配置时传入 switchable files
- **Modify:** `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs`
  - 增加 Elasticsearch `jvm.options` 专用状态、解析、保存与同步逻辑
- **Modify:** `RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml`
  - 在现有对话框里条件显示内存限制 / 高级模式 / Xms / Xmx 区域
- **Test:** `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`
  - 增加路径、传参、UI 绑定与内存同步相关回归测试

---

### Task 1: 为 Elasticsearch 增加多文件配置发现

**Files:**
- Modify: `RemoteInstaller/Services/ConfigurationService.cs:16-221`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 末尾新增以下测试：

```csharp
[Fact]
public void ConfigurationService_DefinesSwitchableElasticsearchConfigFiles()
{
    var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

    Assert.Contains("[\"Elasticsearch\"] =", service);
    Assert.Contains("/etc/elasticsearch/jvm.options", service);
    Assert.Contains("/etc/systemd/system/elasticsearch.service", service);
    Assert.Contains("/opt/elasticsearch/config/jvm.options", service);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigurationService_DefinesSwitchableElasticsearchConfigFiles"`

Expected: FAIL，提示 `Assert.Contains` 未找到 Elasticsearch 的 switchable config files 定义。

- [ ] **Step 3: Write minimal implementation**

在 `RemoteInstaller/Services/ConfigurationService.cs` 中做以下最小实现：

1. 在 Traefik 常量附近新增 Elasticsearch 常量：

```csharp
private const string TraefikMainConfigPath = "/etc/traefik/traefik.toml";
private const string TraefikDynamicConfigPath = "/etc/traefik/dynamic.toml";
private const string ElasticsearchMainConfigPath = "/etc/elasticsearch/elasticsearch.yml";
private const string ElasticsearchJvmOptionsPath = "/etc/elasticsearch/jvm.options";
```

2. 在 `SwitchableConfigFiles` 中加入 Elasticsearch：

```csharp
["Elasticsearch"] =
[
    new() { DisplayName = "主配置 (elasticsearch.yml)", RemotePath = ElasticsearchMainConfigPath },
    new() { DisplayName = "JVM 配置 (jvm.options)", RemotePath = ElasticsearchJvmOptionsPath },
    new() { DisplayName = "服务配置 (elasticsearch.service)", RemotePath = "/etc/systemd/system/elasticsearch.service" },
    new() { DisplayName = "服务配置 (elasticsearch.service)", RemotePath = "/usr/lib/systemd/system/elasticsearch.service" },
    new() { DisplayName = "服务配置 (elasticsearch.service)", RemotePath = "/lib/systemd/system/elasticsearch.service" },
    new() { DisplayName = "主配置 (elasticsearch.yml)", RemotePath = "/opt/elasticsearch/config/elasticsearch.yml" },
    new() { DisplayName = "JVM 配置 (jvm.options)", RemotePath = "/opt/elasticsearch/config/jvm.options" }
]
```

3. 保持 `_configPaths["Elasticsearch"]` 默认主配置优先返回 `elasticsearch.yml`，不要把 `jvm.options` 混进 `GetConfigFilePathAsync` 的默认返回列表。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigurationService_DefinesSwitchableElasticsearchConfigFiles"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/Services/ConfigurationService.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: add elasticsearch config file switching"
```

---

### Task 2: 打通 MainViewModel 到配置编辑器的 Elasticsearch 文件切换

**Files:**
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs:2217-2231`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 追加测试：

```csharp
[Fact]
public void MainViewModel_PassesSwitchableFilesToElasticsearchConfigEditor()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

    Assert.Contains("string.Equals(app.Name, \"Elasticsearch\"", viewModel);
    Assert.Contains("GetSwitchableConfigFilesAsync(app.Name", viewModel);
    Assert.Contains("switchableFiles", viewModel);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.MainViewModel_PassesSwitchableFilesToElasticsearchConfigEditor"`

Expected: FAIL，因为当前只对 Traefik 传递 switchable files。

- [ ] **Step 3: Write minimal implementation**

将 `RemoteInstaller/ViewModels/MainViewModel.cs:2219-2221` 的判断改为同时支持 Traefik 与 Elasticsearch：

```csharp
var switchableFiles = string.Equals(app.Name, "Traefik", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(app.Name, "Elasticsearch", StringComparison.OrdinalIgnoreCase)
    ? await _configurationService.GetSwitchableConfigFilesAsync(app.Name)
    : null;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.MainViewModel_PassesSwitchableFilesToElasticsearchConfigEditor"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/ViewModels/MainViewModel.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: pass elasticsearch switchable config files"
```

---

### Task 3: 在 ViewModel 中建模 Elasticsearch JVM 内存编辑状态

**Files:**
- Modify: `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs:88-200`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 追加测试：

```csharp
[Fact]
public void ConfigEditorViewModel_DefinesElasticsearchMemoryEditingState()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

    Assert.Contains("IsElasticsearchJvmOptions", viewModel);
    Assert.Contains("MemoryLimit", viewModel);
    Assert.Contains("IsElasticsearchMemoryAdvancedMode", viewModel);
    Assert.Contains("JvmXms", viewModel);
    Assert.Contains("JvmXmx", viewModel);
    Assert.Contains("MemoryConfigHint", viewModel);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_DefinesElasticsearchMemoryEditingState"`

Expected: FAIL，因为这些属性尚不存在。

- [ ] **Step 3: Write minimal implementation**

在 `ConfigEditorViewModel` 中新增只用于 Elasticsearch `jvm.options` 页面的一组状态：

```csharp
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

public bool IsElasticsearchJvmOptions =>
    string.Equals(_softwareName, "Elasticsearch", StringComparison.OrdinalIgnoreCase) &&
    ConfigFilePath.EndsWith("jvm.options", StringComparison.OrdinalIgnoreCase);
```

并补一个统一刷新方法签名，后续任务会实现细节：

```csharp
private void UpdateElasticsearchMemoryState()
{
}
```

然后在以下位置调用：
- 构造函数 `LoadConfigItems(_configContent);` 后
- `SwitchToFileAsync` 中切换文件成功后
- `SyncContentFromItems()` 末尾

调用形式：

```csharp
UpdateElasticsearchMemoryState();
OnPropertyChanged(nameof(IsElasticsearchJvmOptions));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_DefinesElasticsearchMemoryEditingState"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: add elasticsearch memory editor state"
```

---

### Task 4: 实现 `jvm.options` 的 Xms/Xmx 解析与逻辑字段映射

**Files:**
- Modify: `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs:615-816`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 中追加：

```csharp
[Fact]
public void ConfigEditorViewModel_ParsesElasticsearchJvmMemoryValues()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

    Assert.Contains("-Xms", viewModel);
    Assert.Contains("-Xmx", viewModel);
    Assert.Contains("UpdateElasticsearchMemoryState", viewModel);
    Assert.Contains("MemoryConfigHint", viewModel);
    Assert.Contains("值不一致", viewModel);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_ParsesElasticsearchJvmMemoryValues"`

Expected: FAIL

- [ ] **Step 3: Write minimal implementation**

在 `ConfigEditorViewModel` 中新增以下私有辅助方法：

```csharp
private static string ExtractJvmOptionValue(string content, string optionName)
{
    foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
        {
            continue;
        }

        if (line.StartsWith(optionName, StringComparison.OrdinalIgnoreCase))
        {
            return line.Substring(optionName.Length).Trim();
        }
    }

    return string.Empty;
}
```

并实现 `UpdateElasticsearchMemoryState()`：

```csharp
private void UpdateElasticsearchMemoryState()
{
    if (!IsElasticsearchJvmOptions)
    {
        MemoryLimit = string.Empty;
        JvmXms = string.Empty;
        JvmXmx = string.Empty;
        MemoryConfigHint = string.Empty;
        return;
    }

    JvmXms = ExtractJvmOptionValue(ConfigContent, "-Xms");
    JvmXmx = ExtractJvmOptionValue(ConfigContent, "-Xmx");

    if (!string.IsNullOrWhiteSpace(JvmXms) && string.Equals(JvmXms, JvmXmx, StringComparison.OrdinalIgnoreCase))
    {
        MemoryLimit = JvmXms;
        MemoryConfigHint = string.Empty;
    }
    else
    {
        MemoryLimit = string.Empty;
        MemoryConfigHint = "当前 Xms 与 Xmx 值不一致，建议保持一致。";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_ParsesElasticsearchJvmMemoryValues"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: parse elasticsearch jvm memory values"
```

---

### Task 5: 实现保存时同步 `jvm.options` 与 systemd service

**Files:**
- Modify: `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs:446-497`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 中追加：

```csharp
[Fact]
public void ConfigEditorViewModel_SynchronizesElasticsearchMemoryToServiceFile()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

    Assert.Contains("ES_JAVA_OPTS", viewModel);
    Assert.Contains("UpdateElasticsearchServiceMemoryAsync", viewModel);
    Assert.Contains("/etc/systemd/system/elasticsearch.service", viewModel);
    Assert.Contains("/usr/lib/systemd/system/elasticsearch.service", viewModel);
    Assert.Contains("/lib/systemd/system/elasticsearch.service", viewModel);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_SynchronizesElasticsearchMemoryToServiceFile"`

Expected: FAIL

- [ ] **Step 3: Write minimal implementation**

在 `ConfigEditorViewModel` 中新增 service 同步逻辑。

1. 新增候选路径：

```csharp
private static readonly string[] ElasticsearchServicePaths =
[
    "/etc/systemd/system/elasticsearch.service",
    "/usr/lib/systemd/system/elasticsearch.service",
    "/lib/systemd/system/elasticsearch.service"
];
```

2. 新增 service 文本替换方法：

```csharp
private static string UpsertElasticsearchJavaOpts(string serviceContent, string xms, string xmx)
{
    var expected = $"Environment=\"ES_JAVA_OPTS=-Xms{xms} -Xmx{xmx}\"";
    var lines = serviceContent.Replace("\r\n", "\n").Split('\n');

    for (var i = 0; i < lines.Length; i++)
    {
        if (lines[i].Contains("ES_JAVA_OPTS", StringComparison.OrdinalIgnoreCase))
        {
            lines[i] = expected;
            return string.Join(Environment.NewLine, lines);
        }
    }

    var insertIndex = Array.FindLastIndex(lines, line =>
        line.Trim().StartsWith("Environment=", StringComparison.OrdinalIgnoreCase) ||
        line.Trim().StartsWith("WorkingDirectory=", StringComparison.OrdinalIgnoreCase) ||
        line.Trim().StartsWith("ExecStart=", StringComparison.OrdinalIgnoreCase));

    var result = new List<string>(lines);
    result.Insert(insertIndex >= 0 ? insertIndex + 1 : result.Count, expected);
    return string.Join(Environment.NewLine, result);
}
```

3. 新增同步方法：

```csharp
private async Task UpdateElasticsearchServiceMemoryAsync(string xms, string xmx, CancellationToken cancellationToken)
{
    foreach (var servicePath in ElasticsearchServicePaths)
    {
        if (!await _configurationService.FileExistsAsync(servicePath, cancellationToken))
        {
            continue;
        }

        var current = await _configurationService.ReadConfigAsync(servicePath);
        var updated = UpsertElasticsearchJavaOpts(current, xms, xmx);
        await _configurationService.SaveConfigAsync(_host, servicePath, updated, _osType, backup: true, cancellationToken: cancellationToken);
        StatusMessage = $"已同步 service 内存配置: {servicePath}";
        return;
    }

    StatusMessage = "未找到 Elasticsearch service 文件，已仅保存 jvm.options";
}
```

4. 为了让上面的方法可用，在 `ConfigurationService.cs` 中增加一个简单透传方法：

```csharp
public Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default)
{
    return _sshService.FileExistsAsync(remotePath, cancellationToken);
}
```

5. 在 `SaveAsync()` 中，在成功保存 `ConfigFilePath` 之后追加：

```csharp
if (IsElasticsearchJvmOptions)
{
    var xms = string.IsNullOrWhiteSpace(JvmXms) ? MemoryLimit : JvmXms;
    var xmx = string.IsNullOrWhiteSpace(JvmXmx) ? MemoryLimit : JvmXmx;
    await UpdateElasticsearchServiceMemoryAsync(xms, xmx, _cts.Token);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_SynchronizesElasticsearchMemoryToServiceFile"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/Services/ConfigurationService.cs RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: sync elasticsearch memory to service config"
```

---

### Task 6: 让 UI 仅在 Elasticsearch `jvm.options` 时显示内存编辑区

**Files:**
- Modify: `RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml:148-339`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 追加：

```csharp
[Fact]
public void ConfigEditorDialog_ShowsElasticsearchMemoryEditorOnlyForJvmOptions()
{
    var dialog = ReadProjectFile("RemoteInstaller", "Views", "Dialogs", "ConfigEditorDialog.xaml");

    Assert.Contains("MemoryLimit", dialog);
    Assert.Contains("IsElasticsearchJvmOptions", dialog);
    Assert.Contains("IsElasticsearchMemoryAdvancedMode", dialog);
    Assert.Contains("JvmXms", dialog);
    Assert.Contains("JvmXmx", dialog);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorDialog_ShowsElasticsearchMemoryEditorOnlyForJvmOptions"`

Expected: FAIL

- [ ] **Step 3: Write minimal implementation**

在 `ConfigEditorDialog.xaml` 的主编辑区顶部、`DataGrid` 前增加一个条件显示的内存编辑区域，结构如下：

```xml
<Border Grid.Row="1"
        Margin="0,10,0,0"
        Padding="12"
        Background="{DynamicResource MaterialDesignCardBackground}"
        CornerRadius="6">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsElasticsearchJvmOptions}" Value="True">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>

    <StackPanel>
        <TextBox Text="{Binding MemoryLimit, UpdateSourceTrigger=PropertyChanged}"
                 materialDesign:HintAssist.Hint="内存限制，例如 2g"/>
        <CheckBox Margin="0,12,0,0"
                  Content="高级模式"
                  IsChecked="{Binding IsElasticsearchMemoryAdvancedMode, Mode=TwoWay}"/>
        <TextBlock Margin="0,8,0,0"
                   Foreground="{DynamicResource WarningBrush}"
                   Text="{Binding MemoryConfigHint}"/>
        <Grid Margin="0,12,0,0">
            <Grid.Style>
                <Style TargetType="Grid">
                    <Setter Property="Visibility" Value="Collapsed"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsElasticsearchMemoryAdvancedMode}" Value="True">
                            <Setter Property="Visibility" Value="Visible"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Grid.Style>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="12"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBox Grid.Column="0"
                     Text="{Binding JvmXms, UpdateSourceTrigger=PropertyChanged}"
                     materialDesign:HintAssist.Hint="Xms"/>
            <TextBox Grid.Column="2"
                     Text="{Binding JvmXmx, UpdateSourceTrigger=PropertyChanged}"
                     materialDesign:HintAssist.Hint="Xmx"/>
        </Grid>
    </StackPanel>
</Border>
```

然后把原来的 `DataGrid` / `TreeView` / 原始文本框整体下移一行，使该内存区位于编辑区顶部。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorDialog_ShowsElasticsearchMemoryEditorOnlyForJvmOptions"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml RemoteInstaller.Tests/ConfigurationServicePathTests.cs
git commit -m "feat: add elasticsearch memory editor ui"
```

---

### Task 7: 把内存输入回写到 `ConfigContent` 并完成整体验证

**Files:**
- Modify: `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs:760-816`
- Test: `RemoteInstaller.Tests/ConfigurationServicePathTests.cs`

- [ ] **Step 1: Write the failing test**

在 `RemoteInstaller.Tests/ConfigurationServicePathTests.cs` 追加：

```csharp
[Fact]
public void ConfigEditorViewModel_WritesMemoryLimitBackToJvmOptionsContent()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

    Assert.Contains("ApplyElasticsearchMemoryToConfigContent", viewModel);
    Assert.Contains("MemoryLimit", viewModel);
    Assert.Contains("JvmXms", viewModel);
    Assert.Contains("JvmXmx", viewModel);
    Assert.Contains("-Xms", viewModel);
    Assert.Contains("-Xmx", viewModel);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests.ConfigEditorViewModel_WritesMemoryLimitBackToJvmOptionsContent"`

Expected: FAIL

- [ ] **Step 3: Write minimal implementation**

在 `ConfigEditorViewModel` 中新增方法：

```csharp
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
    var updated = new List<string>();
    var sawXms = false;
    var sawXmx = false;

    foreach (var line in lines)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
        {
            updated.Add($"-Xms{xms}");
            sawXms = true;
            continue;
        }

        if (trimmed.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
        {
            updated.Add($"-Xmx{xmx}");
            sawXmx = true;
            continue;
        }

        updated.Add(line);
    }

    if (!sawXms)
    {
        updated.Add($"-Xms{xms}");
    }

    if (!sawXmx)
    {
        updated.Add($"-Xmx{xmx}");
    }

    ConfigContent = string.Join(Environment.NewLine, updated);
}
```

然后在 `SaveAsync()` 的 `SaveConfigAsync(...)` 调用前追加：

```csharp
ApplyElasticsearchMemoryToConfigContent();
```

并在 `OnPropertyChanged` / 对应 partial methods 中确保：
- `MemoryLimit`、`JvmXms`、`JvmXmx` 变化时会更新 `IsModified`
- 在默认模式下修改 `MemoryLimit` 会同步刷新 `JvmXms` / `JvmXmx`

推荐最小实现为新增 partial methods：

```csharp
partial void OnMemoryLimitChanged(string value)
{
    if (!IsElasticsearchJvmOptions || IsElasticsearchMemoryAdvancedMode)
    {
        return;
    }

    JvmXms = value;
    JvmXmx = value;
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
```

- [ ] **Step 4: Run tests to verify it passes**

Run these commands in order:

1. `dotnet test "C:/projects/远程安装应用/RemoteInstaller.sln" --filter "FullyQualifiedName~ConfigurationServicePathTests"`
   
   Expected: PASS

2. `dotnet build "C:/projects/远程安装应用/RemoteInstaller.sln"`
   
   Expected: Build succeeds with warnings only if there were pre-existing warnings

- [ ] **Step 5: Commit**

```bash
git add RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller.Tests/ConfigurationServicePathTests.cs RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml RemoteInstaller/ViewModels/MainViewModel.cs RemoteInstaller/Services/ConfigurationService.cs
git commit -m "feat: add elasticsearch memory config editing"
```

---

## Self-Review Checklist

- Spec coverage: covered install panel remains single-field, multi-file config switching, `jvm.options` memory editor, advanced mode, dual-write to service, service-missing behavior, validation path
- Placeholder scan: no TBD/TODO/fill-later placeholders remain
- Type consistency: uses `MemoryLimit`, `JvmXms`, `JvmXmx`, `IsElasticsearchMemoryAdvancedMode`, `IsElasticsearchJvmOptions`, `MemoryConfigHint`, `UpdateElasticsearchServiceMemoryAsync`, `ApplyElasticsearchMemoryToConfigContent` consistently across tasks
