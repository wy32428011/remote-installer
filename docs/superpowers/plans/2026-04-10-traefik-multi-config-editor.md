# Traefik Multi-Config Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Traefik 配置编辑器默认打开 `traefik.toml`，并在同一编辑器内支持切换到 `dynamic.toml`，同时保留“保存”和“保存并重启”。

**Architecture:** 保持现有单文件编辑器主流程不变，只对 Traefik 注入“可切换文件列表”。`MainViewModel` 负责在打开编辑器时提供 Traefik 的可切换配置文件，`ConfigEditorViewModel` 负责当前文件状态、切换保护和重新加载，`ConfigEditorDialog` 负责呈现文件切换 UI，`ConfigurationService` 提供 Traefik 已存在配置文件列表的轻量辅助方法。

**Tech Stack:** C# 13 / .NET 10 WPF, CommunityToolkit.Mvvm, MaterialDesignInXaml, SSH.NET, xUnit

---

### Task 1: 为 Traefik 配置切换补充失败测试与服务层文件列表能力

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\Services\ConfigurationService.cs:218-366`
- Create: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`

- [ ] **Step 1: 写一个失败测试，先锁定服务层需要暴露 Traefik 可切换文件列表**

```csharp
using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class TraefikConfigSwitchTests
{
    private static string GetProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RemoteInstaller.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 RemoteInstaller.sln，无法定位项目根目录。");
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ConfigurationService_DefinesTraefikDynamicConfigCandidate()
    {
        var service = ReadProjectFile("RemoteInstaller", "Services", "ConfigurationService.cs");

        Assert.Contains("/etc/traefik/dynamic.toml", service);
        Assert.Contains("GetSwitchableConfigFilesAsync", service);
    }
}
```

- [ ] **Step 2: 运行测试，确认它先失败**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigurationService_DefinesTraefikDynamicConfigCandidate"`
Expected: FAIL，提示 `dynamic.toml` 或 `GetSwitchableConfigFilesAsync` 尚未出现在 `ConfigurationService.cs` 中。

- [ ] **Step 3: 在 `ConfigurationService` 增加 Traefik 可切换配置文件定义和查询方法，使用最小实现**

```csharp
public sealed class ConfigFileOption
{
    public string DisplayName { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
}

private static readonly List<ConfigFileOption> _traefikSwitchablePaths = new()
{
    new() { DisplayName = "主配置 (traefik.toml)", RemotePath = "/etc/traefik/traefik.toml" },
    new() { DisplayName = "动态配置 (dynamic.toml)", RemotePath = "/etc/traefik/dynamic.toml" }
};

public async Task<List<ConfigFileOption>> GetSwitchableConfigFilesAsync(
    string softwareName,
    CancellationToken cancellationToken = default)
{
    if (!string.Equals(softwareName, "Traefik", StringComparison.OrdinalIgnoreCase))
    {
        return new List<ConfigFileOption>();
    }

    var results = new List<ConfigFileOption>();
    foreach (var option in _traefikSwitchablePaths)
    {
        if (await _sshService.FileExistsAsync(option.RemotePath, cancellationToken))
        {
            results.Add(new ConfigFileOption
            {
                DisplayName = option.DisplayName,
                RemotePath = option.RemotePath
            });
        }
    }

    return results;
}
```

- [ ] **Step 4: 运行测试，确认服务层定义已经就位**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigurationService_DefinesTraefikDynamicConfigCandidate"`
Expected: PASS

- [ ] **Step 5: 提交服务层改动**

```bash
git add RemoteInstaller/Services/ConfigurationService.cs RemoteInstaller.Tests/TraefikConfigSwitchTests.cs
git commit -m "feat: add switchable Traefik config file definitions"
```

---

### Task 2: 让 MainViewModel 在打开 Traefik 编辑器时注入可切换文件列表

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\ViewModels\MainViewModel.cs:2203-2229`
- Modify: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`

- [ ] **Step 1: 写一个失败测试，锁定 MainViewModel 只对 Traefik 注入可切换文件列表**

```csharp
[Fact]
public void MainViewModel_InjectsSwitchableFilesWhenOpeningTraefikConfigEditor()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "MainViewModel.cs");

    Assert.Contains("GetSwitchableConfigFilesAsync(app.Name", viewModel);
    Assert.Contains("switchableFiles", viewModel);
}
```

- [ ] **Step 2: 运行测试，确认它先失败**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.MainViewModel_InjectsSwitchableFilesWhenOpeningTraefikConfigEditor"`
Expected: FAIL，提示 `MainViewModel.cs` 中尚未注入 `switchableFiles`。

- [ ] **Step 3: 在 `MainViewModel.ConfigureApplication` 中为 Traefik 传入文件列表，保持其他中间件不变**

```csharp
var switchableFiles = new List<ConfigurationService.ConfigFileOption>();
if (string.Equals(app.Name, "Traefik", StringComparison.OrdinalIgnoreCase))
{
    switchableFiles = await _configurationService.GetSwitchableConfigFilesAsync(app.Name);
}

var configViewModel = new ConfigEditorViewModel(
    _configurationService,
    remoteHost,
    app.Name,
    osType,
    configPath,
    configContent,
    supportsRestart: true,
    switchableFiles: switchableFiles);
```

- [ ] **Step 4: 运行测试，确认注入逻辑已存在**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.MainViewModel_InjectsSwitchableFilesWhenOpeningTraefikConfigEditor"`
Expected: PASS

- [ ] **Step 5: 提交入口层改动**

```bash
git add RemoteInstaller/ViewModels/MainViewModel.cs RemoteInstaller.Tests/TraefikConfigSwitchTests.cs
git commit -m "feat: pass Traefik switchable config files into editor"
```

---

### Task 3: 扩展 ConfigEditorViewModel，支持文件切换与未保存保护

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\ViewModels\ConfigEditorViewModel.cs:18-579`
- Modify: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`

- [ ] **Step 1: 写一个失败测试，锁定编辑器要支持切换文件元数据**

```csharp
[Fact]
public void ConfigEditorViewModel_DefinesSwitchableFileStateForTraefik()
{
    var viewModel = ReadProjectFile("RemoteInstaller", "ViewModels", "ConfigEditorViewModel.cs");

    Assert.Contains("AvailableFiles", viewModel);
    Assert.Contains("SelectedFile", viewModel);
    Assert.Contains("SupportsFileSwitch", viewModel);
    Assert.Contains("SwitchToFileAsync", viewModel);
}
```

- [ ] **Step 2: 运行测试，确认它先失败**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigEditorViewModel_DefinesSwitchableFileStateForTraefik"`
Expected: FAIL，提示 `ConfigEditorViewModel` 尚未定义文件切换状态和切换方法。

- [ ] **Step 3: 在 `ConfigEditorViewModel` 中增加切换文件状态、构造参数和切换方法，保持保存/重启逻辑复用当前 `ConfigFilePath`**

```csharp
public partial class ConfigEditorViewModel : ObservableObject
{
    public sealed class ConfigFileOption : ObservableObject
    {
        public string DisplayName { get; init; } = string.Empty;
        public string RemotePath { get; init; } = string.Empty;
    }

    [ObservableProperty]
    private ObservableCollection<ConfigFileOption> _availableFiles = new();

    [ObservableProperty]
    private ConfigFileOption? _selectedFile;

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
        // 现有初始化逻辑保持不变

        if (switchableFiles != null)
        {
            foreach (var file in switchableFiles)
            {
                AvailableFiles.Add(new ConfigFileOption
                {
                    DisplayName = file.DisplayName,
                    RemotePath = file.RemotePath
                });
            }
        }

        SelectedFile = AvailableFiles.FirstOrDefault(x => x.RemotePath == configFilePath);
        UpdateTitle();
    }

    partial void OnSelectedFileChanged(ConfigFileOption? value)
    {
        if (value == null || value.RemotePath == ConfigFilePath || IsLoading)
        {
            return;
        }

        _ = SwitchToFileAsync(value);
    }

    private async Task SwitchToFileAsync(ConfigFileOption targetFile)
    {
        if (IsModified)
        {
            var result = MessageBox.Show(
                "当前文件有未保存修改。是否先保存后再切换？",
                "切换文件",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                SelectedFile = AvailableFiles.FirstOrDefault(x => x.RemotePath == ConfigFilePath);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                await SaveAsync();
                if (IsModified)
                {
                    SelectedFile = AvailableFiles.FirstOrDefault(x => x.RemotePath == ConfigFilePath);
                    return;
                }
            }
        }

        var content = await _configurationService.ReadConfigAsync(targetFile.RemotePath);
        ConfigFilePath = targetFile.RemotePath;
        ConfigContent = content;
        _savedContent = content;
        IsModified = false;
        LoadConfigItems(content);
        UpdateTitle();
        StatusMessage = $"已加载 {targetFile.DisplayName}";
    }

    private void UpdateTitle()
    {
        var fileName = System.IO.Path.GetFileName(ConfigFilePath);
        Title = string.IsNullOrWhiteSpace(fileName)
            ? $"编辑{_softwareName}配置"
            : $"编辑{_softwareName}配置 - {fileName}";
    }
}
```

- [ ] **Step 4: 运行测试，确认 ViewModel 文件切换结构已具备**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigEditorViewModel_DefinesSwitchableFileStateForTraefik"`
Expected: PASS

- [ ] **Step 5: 提交 ViewModel 改动**

```bash
git add RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller.Tests/TraefikConfigSwitchTests.cs
git commit -m "feat: add switchable file state to config editor"
```

---

### Task 4: 在 ConfigEditorDialog 中显示文件切换控件

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\Views\Dialogs\ConfigEditorDialog.xaml:30-117`
- Modify: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`

- [ ] **Step 1: 写一个失败测试，锁定对话框新增文件切换下拉框**

```csharp
[Fact]
public void ConfigEditorDialog_ShowsFileSelectorForSwitchableConfigs()
{
    var xaml = ReadProjectFile("RemoteInstaller", "Views", "Dialogs", "ConfigEditorDialog.xaml");

    Assert.Contains("ItemsSource=\"{Binding AvailableFiles}\"", xaml);
    Assert.Contains("SelectedItem=\"{Binding SelectedFile, Mode=TwoWay}\"", xaml);
}
```

- [ ] **Step 2: 运行测试，确认它先失败**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigEditorDialog_ShowsFileSelectorForSwitchableConfigs"`
Expected: FAIL，提示 `ConfigEditorDialog.xaml` 尚未包含文件切换下拉框绑定。

- [ ] **Step 3: 在对话框顶部路径区域前增加文件切换控件，仅在 `SupportsFileSwitch` 为 true 时显示**

```xml
<StackPanel Grid.Row="1" Margin="0,0,0,12">
    <Grid Visibility="{Binding SupportsFileSwitch, Converter={StaticResource BoolToVisibilityConverter}}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <TextBlock VerticalAlignment="Center"
                   Margin="0,0,12,0"
                   Text="文件"
                   Foreground="{DynamicResource MaterialDesignBody}"/>

        <ComboBox Grid.Column="1"
                  ItemsSource="{Binding AvailableFiles}"
                  SelectedItem="{Binding SelectedFile, Mode=TwoWay}"
                  DisplayMemberPath="DisplayName"/>
    </Grid>

    <Border Margin="0,12,0,0"
            Padding="12,10"
            Background="{DynamicResource MaterialDesignCardBackground}"
            CornerRadius="6">
        <!-- 保留现有 ConfigFilePath 显示块 -->
    </Border>
</StackPanel>
```

- [ ] **Step 4: 运行测试，确认 UI 绑定已存在**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests.ConfigEditorDialog_ShowsFileSelectorForSwitchableConfigs"`
Expected: PASS

- [ ] **Step 5: 提交对话框改动**

```bash
git add RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml RemoteInstaller.Tests/TraefikConfigSwitchTests.cs
git commit -m "feat: show switchable config selector in editor dialog"
```

---

### Task 5: 运行构建与回归验证

**Files:**
- Test: `C:\projects\远程安装应用\RemoteInstaller.Tests\TraefikConfigSwitchTests.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller\ViewModels\MainViewModel.cs`
- Test: `C:\projects\远程安装应用\RemoteInstaller\Views\Dialogs\ConfigEditorDialog.xaml`

- [ ] **Step 1: 运行新增回归测试**

Run: `dotnet test "C:/projects/远程安装应用/RemoteInstaller.Tests/RemoteInstaller.Tests.csproj" --filter "TraefikConfigSwitchTests"`
Expected: PASS，新增 Traefik 多文件编辑相关测试全部通过。

- [ ] **Step 2: 运行完整构建验证**

Run: `dotnet build "C:/projects/远程安装应用/RemoteInstaller.sln"`
Expected: Build succeeded，0 error；允许保留仓库当前已有 warning。

- [ ] **Step 3: 手工功能验证 Traefik 主配置默认打开**

Run: `dotnet run --project "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"`
Expected:
- 连接一台已安装 Traefik 的 Linux 主机
- 点击“配置”后默认打开 `traefik.toml`
- 标题包含 `traefik.toml`
- 下拉框显示主配置与动态配置（若 `dynamic.toml` 存在）

- [ ] **Step 4: 手工功能验证未保存切换与保存并重启**

Expected:
- 修改 `traefik.toml` 后切换到 `dynamic.toml`，弹出“保存后切换 / 放弃并切换 / 取消”
- 在 `dynamic.toml` 中点击“保存”，实际保存路径是 `dynamic.toml`
- 在 `dynamic.toml` 中点击“保存并重启”，保存成功后调用 Traefik 服务重启
- 非 Traefik 中间件打开配置编辑器时，不显示文件切换控件

- [ ] **Step 5: 提交验证结果**

```bash
git add RemoteInstaller/Services/ConfigurationService.cs RemoteInstaller/ViewModels/MainViewModel.cs RemoteInstaller/ViewModels/ConfigEditorViewModel.cs RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml RemoteInstaller.Tests/TraefikConfigSwitchTests.cs
git commit -m "test: verify Traefik multi-config editor flow"
```

---

## Self-Review Check

**1. Spec coverage:**
- ✅ 默认打开 `traefik.toml`
- ✅ 编辑器中支持切换到 `dynamic.toml`
- ✅ 切换时未保存修改保护
- ✅ `dynamic.toml` 保留保存并重启
- ✅ 非 Traefik 中间件保持原有行为

**2. Placeholder scan:**
- ✅ 无 TBD / TODO / “后续实现” 占位符
- ✅ 每个代码步骤都给出明确代码或命令
- ✅ 每个测试步骤都给出预期结果

**3. Type consistency:**
- ✅ `AvailableFiles` / `SelectedFile` / `SupportsFileSwitch` 命名一致
- ✅ `GetSwitchableConfigFilesAsync` 在任务之间命名一致
- ✅ `ConfigFileOption` 在服务层与 ViewModel 层职责清晰，未混用 UI 类型和服务类型
