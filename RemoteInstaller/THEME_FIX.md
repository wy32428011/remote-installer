# 主题切换样式绑定修复说明

## 问题描述
主题切换时，以下区域没有效果：
1. 服务器列表
2. 应用列表
3. 任务列表
4. 安装日志

## 根本原因分析

### 1. App.xaml.cs 问题
- `SwitchTheme` 方法尝试修改 `mainWindow.Resources.MergedDictionaries`
- 但主题资源字典应该被添加到 `Application.Resources.MergedDictionaries` 而不是主窗口
- 这导致主题切换时，资源字典没有被正确应用到全局

### 2. DarkTheme.xaml 资源 Key 命名问题
- DarkTheme.xaml 中使用了 `MaterialDesignPaperDark`、`MaterialDesign50Dark` 等带 Dark 后缀的 Key
- 但 MainWindow.xaml 使用的是 `MaterialDesignPaper`、`MaterialDesign50` 等不带后缀的 Key
- 这导致深色主题的资源无法覆盖浅色主题的资源

### 3. MainWindow.xaml 硬编码颜色
- 应用卡片中的图标背景使用了硬编码颜色 `#6366F1`
- 状态标签（运行中、已安装）使用了硬编码背景色和文字颜色
- 这些硬编码颜色无法响应主题切换

## 修复内容

### 1. App.xaml.cs 修复
```csharp
// 修改前：修改 mainWindow.Resources.MergedDictionaries
var mainWindow = Current?.MainWindow;
if (mainWindow != null) {
    mainWindow.Resources.MergedDictionaries.Add(...);
}

// 修改后：修改 Application.Resources.MergedDictionaries
var resources = Current?.Resources;
if (resources != null && resources.MergedDictionaries != null) {
    resources.MergedDictionaries.Add(...);
}
```

### 2. DarkTheme.xaml 修复
修改了资源 Key 命名，使其与 LightTheme.xaml 一致：

**修改前：**
```xml
<Color x:Key="MaterialDesignPaperDark">#1E1E1E</Color>
<Color x:Key="MaterialDesign50Dark">#2D2D2D</Color>
<SolidColorBrush x:Key="MaterialDesignPaperDark" Color="{StaticResource MaterialDesignPaperDark}"/>
```

**修改后：**
```xml
<Color x:Key="MaterialDesignPaper">#1E1E1E</Color>
<Color x:Key="MaterialDesign50">#2D2D2D</Color>
<SolidColorBrush x:Key="MaterialDesignPaper" Color="{StaticResource MaterialDesignPaper}"/>
```

### 3. 新增应用卡片主题资源
在 DarkTheme.xaml 和 LightTheme.xaml 中新增了应用卡片相关资源：

**DarkTheme.xaml:**
```xml
<!-- 应用卡片颜色 -->
<Color x:Key="AppIconBackgroundColor">#5B59D9</Color>
<Color x:Key="AppOsTagBackgroundColor">#2A2A4A</Color>
<Color x:Key="AppOsTagTextColor">#818CF8</Color>
<Color x:Key="AppRunningTagBackgroundColor">#1A3828</Color>
<Color x:Key="AppRunningTagTextColor">#34D399</Color>
<Color x:Key="AppInstalledTagBackgroundColor">#423218</Color>
<Color x:Key="AppInstalledTagTextColor">#FBBF24</Color>
```

**LightTheme.xaml:**
```xml
<!-- 应用卡片颜色 -->
<Color x:Key="AppIconBackgroundColor">#6366F1</Color>
<Color x:Key="AppOsTagBackgroundColor">#EEF2FF</Color>
<Color x:Key="AppOsTagTextColor">#6366F1</Color>
<Color x:Key="AppRunningTagBackgroundColor">#D1FAE5</Color>
<Color x:Key="AppRunningTagTextColor">#10B981</Color>
<Color x:Key="AppInstalledTagBackgroundColor">#FEF3C7</Color>
<Color x:Key="AppInstalledTagTextColor">#F59E0B</Color>
```

### 4. MainWindow.xaml 修复
将硬编码颜色改为动态资源引用：

**修改前：**
```xml
<Border Background="#6366F1">
<Border Background="#EEF2FF">
    <TextBlock Foreground="#6366F1"/>
</Border>
<Border Background="#D1FAE5">
    <TextBlock Foreground="#10B981"/>
</Border>
<Border Background="#FEF3C7">
    <TextBlock Foreground="#F59E0B"/>
</Border>
```

**修改后：**
```xml
<Border Background="{DynamicResource AppIconBackgroundBrush}">
<Border Background="{DynamicResource AppOsTagBackgroundBrush}">
    <TextBlock Foreground="{DynamicResource AppOsTagTextBrush}"/>
</Border>
<Border Background="{DynamicResource AppRunningTagBackgroundBrush}">
    <TextBlock Foreground="{DynamicResource AppRunningTagTextBrush}"/>
</Border>
<Border Background="{DynamicResource AppInstalledTagBackgroundBrush}">
    <TextBlock Foreground="{DynamicResource AppInstalledTagTextBrush}"/>
</Border>
```

## 验证步骤

1. 编译项目：`dotnet build`
2. 启动应用
3. 点击主题切换按钮
4. 检查以下区域是否响应主题变化：
   - ✅ 服务器列表背景色
   - ✅ 应用卡片背景色
   - ✅ 应用图标背景色
   - ✅ 状态标签背景色和文字颜色
   - ✅ 任务列表背景色
   - ✅ 安装日志背景色
   - ✅ 所有文本颜色

## 修复文件清单

| 文件 | 修改内容 |
|------|----------|
| `App.xaml.cs` | 修复 SwitchTheme 方法，使用 Application.Resources 而不是 MainWindow.Resources |
| `Themes/DarkTheme.xaml` | 修改资源 Key 命名，新增应用卡片主题资源 |
| `Themes/LightTheme.xaml` | 新增应用卡片主题资源 |
| `MainWindow.xaml` | 将硬编码颜色改为 DynamicResource 引用 |

## 编译结果
```
已成功生成。
    211 个警告
    0 个错误
```

## 注意事项
1. 所有主题相关资源必须使用 `{DynamicResource}` 引用，不能使用 `{StaticResource}`
2. DarkTheme.xaml 和 LightTheme.xaml 必须使用相同的资源 Key 命名
3. 主题切换时，资源字典必须添加到 `Application.Resources.MergedDictionaries` 才能全局生效
