# Elasticsearch Uninstall Fix & UI Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成 Elasticsearch 卸载功能修复和 UI 布局优化，确保功能稳定可用，UI 符合现代设计标准。

**Architecture:** 后端修复 SSH 命令执行逻辑和卸载脚本鲁棒性，前端重构 WPF 布局优化空间利用率和用户体验，保持现有 MVVM 架构不变，最小化代码侵入。

**Tech Stack:** C# WPF, .NET 6+, MaterialDesignInXaml, Bash Shell Scripting, SSH.NET

---

### Task 1: 验证 Elasticsearch 卸载后端修复

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\Services\InstallerService.cs`
- Modify: `C:\projects\远程安装应用\RemoteInstaller\Scripts\Elasticsearch\uninstall_linux.sh`

- [ ] **Step 1: 验证卸载命令执行逻辑**

```csharp
// 确认 InstallerService.cs 中的卸载命令已修改为：
public async Task UninstallElasticsearchAsync(RemoteHost host, bool keepData = false, CancellationToken cancellationToken = default)
{
    var keepDataArg = keepData ? "--keep-data" : "--no-keep-data";
    var remoteWorkDir = "/tmp/remote_installer";
    var remoteScriptPath = $"{remoteWorkDir}/uninstall_elasticsearch.sh";

    // 上传脚本
    await UploadScriptAsync(host, "Scripts/Elasticsearch/uninstall_linux.sh", remoteScriptPath, cancellationToken);

    // 执行卸载命令（已移除 sudo 前置检查和 timeout 包装，添加 stderr 重定向）
    var command = $"cd \"{remoteWorkDir}\" && sudo bash \"{remoteScriptPath}\" {keepDataArg} 2>&1";
    await ExecuteCommandWithProgressAsync(host, command, cancellationToken);
}
```

- [ ] **Step 2: 验证卸载脚本配置**

```bash
#!/bin/bash
# 确认脚本顶部已添加禁用 errexit
set +e

# 确认脚本包含完整的卸载步骤：停止服务、卸载包、清理配置、删除目录、清理用户
```

- [ ] **Step 3: 提交后端代码修改**

```bash
git add RemoteInstaller/Services/InstallerService.cs RemoteInstaller/Scripts/Elasticsearch/uninstall_linux.sh
git commit -m "fix: 修复 Elasticsearch 卸载功能，移除 sudo 前置检查和 timeout 包装"
```

---

### Task 2: 验证 UI 布局优化修复

**Files:**
- Modify: `C:\projects\远程安装应用\RemoteInstaller\MainWindow.xaml`

- [ ] **Step 1: 验证 ProgressBar 无 CornerRadius 属性**

```xml
<!-- 确认所有 ProgressBar 控件已移除 CornerRadius 属性 -->
<ProgressBar Grid.Row="1"
             Value="{Binding Progress}"
             Minimum="0" Maximum="100"
             Height="6"
             Margin="0,10,0,6"
             VerticalAlignment="Center"
             Background="{DynamicResource SurfaceBrush}"
             Foreground="{DynamicResource AccentBrush}"/>
```

- [ ] **Step 2: 验证布局结构正确**

```xml
<!-- 确认布局为左右分栏结构 -->
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="300"/> <!-- 服务器列表 -->
    <ColumnDefinition Width="*"/>   <!-- 主内容区域 -->
</Grid.ColumnDefinitions>

<!-- 确认应用市场使用 WrapPanel 替代横向滚动布局 -->
<ItemsControl.ItemsPanel>
    <ItemsPanelTemplate>
        <WrapPanel Orientation="Horizontal" Margin="4"/>
    </ItemsPanelTemplate>
</ItemsControl.ItemsPanel>
```

- [ ] **Step 3: 提交 UI 代码修改**

```bash
git add RemoteInstaller/MainWindow.xaml
git commit -m "fix: 移除 ProgressBar 无效 CornerRadius 属性，修复编译错误"
```

---

### Task 3: 功能测试与验证

**Files:**
- Test: 本地测试 + 远程 Ubuntu 服务器测试

- [ ] **Step 1: 本地编译验证**

Run: `dotnet build RemoteInstaller.sln -c Release`
Expected: 编译成功，0 错误

- [ ] **Step 2: 本地 UI 功能测试**

Run: `dotnet run --project RemoteInstaller/RemoteInstaller.csproj`
Expected:
- 主窗口正常显示，布局符合预期
- 服务器列表卡片样式正确，悬停效果正常
- 应用市场网格布局正常，展示所有应用卡片
- 任务列表卡片样式正确，进度条显示正常
- 日志区域可以正常显示日志内容

- [ ] **Step 3: 远程卸载功能测试**

测试步骤：
1. 添加 Ubuntu 服务器主机
2. 在服务器上手动安装 Elasticsearch
3. 选择服务器，执行 Elasticsearch 卸载操作
4. 验证卸载过程进度上报正常，无错误
5. 登录服务器验证 Elasticsearch 已完全卸载，进程已停止，端口已释放

- [ ] **Step 4: 提交测试验证结果**

```bash
# 确认所有功能正常后标记完成
git commit --allow-empty -m "test: 验证 Elasticsearch 卸载功能和 UI 优化正常工作"
```

---

## Self-Review Check

**1. Spec coverage:**
- ✅ Elasticsearch 卸载功能修复全覆盖
- ✅ UI 布局优化全覆盖
- ✅ 编译错误修复全覆盖

**2. Placeholder scan:**
- ✅ 无 TBD/TODO 占位符
- ✅ 所有步骤包含完整代码示例
- ✅ 所有命令包含预期输出

**3. Type consistency:**
- ✅ 所有文件路径准确无误
- ✅ 代码示例与实际修改完全一致
- ✅ 无命名冲突或不一致问题