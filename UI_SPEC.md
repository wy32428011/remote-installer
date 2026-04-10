# UI 设计规范 - 远程安装应用

> **版本**: v1.0  
> **创建日期**: 2026-03-16  
> **作者**: Leon 🦁 (UI Agent)  
> **技术栈**: WPF + MaterialDesignInXaml + .NET 8

---

## 目录

1. [设计原则](#1-设计原则)
2. [配色方案](#2-配色方案)
3. [字体规范](#3-字体规范)
4. [组件样式](#4-组件样式)
5. [界面布局](#5-界面布局)
6. [交互说明](#6-交互说明)
7. [XAML 代码示例](#7-xaml-代码示例)

---

## 1. 设计原则

### 1.1 核心原则

| 原则 | 说明 |
|------|------|
| **简洁高效** | 减少视觉干扰，突出核心操作 |
| **一致性** | 统一的视觉语言和交互模式 |
| **可访问性** | 清晰的对比度和可识别的交互元素 |
| **响应式** | 适配不同屏幕尺寸（最小 1280x720） |

### 1.2 Material Design 应用

- 使用 MaterialDesignInXaml 组件库
- 遵循 Material Design 3 设计规范
- 使用卡片式设计组织内容
- 使用阴影层级表达深度关系

---

## 2. 配色方案

### 2.1 深色主题（默认）

```
主色板
┌─────────────────────────────────────────────────────┐
│  Primary       │ #6366F1 (Indigo 500)               │
│  Primary Dark  │ #4F46E5 (Indigo 600)               │
│  Primary Light │ #818CF8 (Indigo 400)               │
│  Secondary     │ #06B6D4 (Cyan 500)                 │
│  Background    │ #121212                            │
│  Surface       │ #1E1E1E                            │
│  Surface Light │ #2D2D2D                            │
│  Error         │ #EF4444 (Red 500)                  │
│  Success       │ #10B981 (Emerald 500)              │
│  Warning       │ #F59E0B (Amber 500)                │
│  Info          │ #3B82F6 (Blue 500)                 │
│  Text Primary  │ #E5E7EB (Gray 200)                 │
│  Text Secondary│ #9CA3AF (Gray 400)                 │
│  Text Disabled │ #6B7280 (Gray 500)                 │
│  Border        │ #374151 (Gray 700)                 │
└─────────────────────────────────────────────────────┘
```

### 2.2 浅色主题

```
主色板
┌─────────────────────────────────────────────────────┐
│  Primary       │ #4F46E5 (Indigo 600)               │
│  Primary Dark  │ #4338CA (Indigo 700)               │
│  Primary Light │ #6366F1 (Indigo 500)               │
│  Secondary     │ #0891B2 (Cyan 600)                 │
│  Background    │ #F9FAFB (Gray 50)                  │
│  Surface       │ #FFFFFF                             │
│  Surface Light │ #F3F4F6 (Gray 100)                 │
│  Error         │ #DC2626 (Red 600)                  │
│  Success       │ #059669 (Emerald 600)              │
│  Warning       │ #D97706 (Amber 600)                │
│  Info          │ #2563EB (Blue 600)                 │
│  Text Primary  │ #111827 (Gray 900)                 │
│  Text Secondary│ #6B7280 (Gray 500)                 │
│  Text Disabled │ #9CA3AF (Gray 400)                 │
│  Border        │ #E5E7EB (Gray 200)                 │
└─────────────────────────────────────────────────────┘
```

### 2.3 状态颜色

| 状态 | 颜色 | 用途 |
|------|------|------|
| 在线/成功 | #10B981 | 主机在线、安装成功 |
| 离线/错误 | #EF4444 | 主机离线、安装失败 |
| 警告 | #F59E0B | 连接超时、配置警告 |
| 信息 | #3B82F6 | 一般提示信息 |
| 运行中 | #06B6D4 | 服务运行中 |
| 已停止 | #6B7280 | 服务已停止 |

---

## 3. 字体规范

### 3.1 字体族

```xaml
<FontFamily>Microsoft YaHei UI, Segoe UI, sans-serif</FontFamily>
```

### 3.2 字号体系

| 用途 | 字号 | 字重 | 行高 |
|------|------|------|------|
| 页面标题 | 24px | SemiBold | 1.2 |
| 卡片标题 | 16px | SemiBold | 1.4 |
| 正文 | 14px | Regular | 1.5 |
| 辅助文字 | 12px | Regular | 1.5 |
| 按钮文字 | 14px | Medium | 1.4 |
| 标签 | 12px | Medium | 1.4 |

### 3.3 字体资源定义

```xaml
<ResourceDictionary>
    <!-- 字号 -->
    <sys:Double x:Key="FontSizeLarge">24</sys:Double>
    <sys:Double x:Key="FontSizeMedium">16</sys:Double>
    <sys:Double x:Key="FontSizeNormal">14</sys:Double>
    <sys:Double x:Key="FontSizeSmall">12</sys:Double>
    
    <!-- 字重 -->
    <FontWeight x:Key="FontWeightRegular">Regular</FontWeight>
    <FontWeight x:Key="FontWeightMedium">Medium</FontWeight>
    <FontWeight x:Key="FontWeightSemiBold">SemiBold</FontWeight>
</ResourceDictionary>
```

---

## 4. 组件样式

### 4.1 按钮样式

#### 主要按钮

```xaml
<Style x:Key="PrimaryButtonStyle" TargetType="Button">
    <Setter Property="Background" Value="{StaticResource PrimaryHueMidBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontSize" Value="{StaticResource FontSizeNormal}"/>
    <Setter Property="FontWeight" Value="{StaticResource FontWeightMedium}"/>
    <Setter Property="Padding" Value="24,12"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
        <ControlTemplate TargetType="Button">
            <materialDesign:ButtonProgressCircle
                Background="{TemplateBinding Background}"
                Foreground="{TemplateBinding Foreground}"
                BorderThickness="{TemplateBinding BorderThickness}"
                Padding="{TemplateBinding Padding}"
                SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"
                RenderTransformOrigin="0.5,0.5">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </materialDesign:ButtonProgressCircle>
        </ControlTemplate>
    </Setter>
</Style>
```

#### 次要按钮

```xaml
<Style x:Key="SecondaryButtonStyle" TargetType="Button">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{StaticResource PrimaryHueMidBrush}"/>
    <Setter Property="BorderBrush" Value="{StaticResource PrimaryHueMidBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="FontSize" Value="{StaticResource FontSizeNormal}"/>
    <Setter Property="Padding" Value="24,11"/>
</Style>
```

#### 危险按钮

```xaml
<Style x:Key="DangerButtonStyle" TargetType="Button" BasedOn="{StaticResource MaterialDesignFlatButton}">
    <Setter Property="Background" Value="#EF4444"/>
    <Setter Property="Foreground" Value="White"/>
</Style>
```

### 4.2 卡片样式

```xaml
<Style x:Key="AppCardStyle" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource MaterialDesignPaperBrush}"/>
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Padding" Value="20"/>
    <Setter Property="Margin" Value="16"/>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="Black" Opacity="0.1" BlurRadius="8" ShadowDepth="2"/>
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="Effect">
                <Setter.Value>
                    <DropShadowEffect Color="Black" Opacity="0.15" BlurRadius="12" ShadowDepth="4"/>
                </Setter.Value>
            </Setter>
        </Trigger>
    </Style.Triggers>
</Style>
```

### 4.3 进度条样式

```xaml
<Style x:Key="InstallationProgressBarStyle" TargetType="ProgressBar">
    <Setter Property="Height" Value="8"/>
    <Setter Property="Minimum" Value="0"/>
    <Setter Property="Maximum" Value="100"/>
    <Setter Property="Template">
        <ControlTemplate TargetType="ProgressBar">
            <Grid>
                <Border Background="#374151" CornerRadius="4"/>
                <Border x:Name="ProgressBarPart"
                        Background="{StaticResource PrimaryHueMidBrush}"
                        CornerRadius="4"
                        HorizontalAlignment="Left"/>
            </Grid>
        </ControlTemplate>
    </Setter>
</Style>
```

### 4.4 日志文本块样式

```xaml
<Style x:Key="LogTextBlockStyle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{StaticResource FontSizeSmall}"/>
    <Setter Property="FontFamily" Value="Consolas, monospace"/>
    <Setter Property="Foreground" Value="{StaticResource MaterialDesignBodyLightBrush}"/>
    <Setter Property="TextWrapping" Value="Wrap"/>
</Style>

<!-- 日志级别着色 -->
<Style x:Key="LogInfoStyle" BasedOn="{StaticResource LogTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="#3B82F6"/>
</Style>
<Style x:Key="LogSuccessStyle" BasedOn="{StaticResource LogTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="#10B981"/>
</Style>
<Style x:Key="LogWarningStyle" BasedOn="{StaticResource LogTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="#F59E0B"/>
</Style>
<Style x:Key="LogErrorStyle" BasedOn="{StaticResource LogTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="#EF4444"/>
</Style>
```

### 4.5 输入框样式

```xaml
<Style x:Key="FormInputStyle" TargetType="TextBox">
    <Setter Property="FontSize" Value="{StaticResource FontSizeNormal}"/>
    <Setter Property="Padding" Value="16,12"/>
    <Setter Property="MaterialDesignHintAssist.IsFloating" Value="True"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="BorderBrush" Value="#374151"/>
    <Setter Property="Background" Value="{StaticResource MaterialDesignPaperBrush}"/>
</Style>
```

### 4.6 状态徽章样式

```xaml
<Style x:Key="StatusBadgeStyle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{StaticResource FontSizeSmall}"/>
    <Setter Property="FontWeight" Value="{StaticResource FontWeightMedium}"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
    <Setter Property="Margin" Value="8,0,0,0"/>
</Style>
```

---

## 5. 界面布局

### 5.1 主界面布局

```
┌─────────────────────────────────────────────────────────────────┐
│  [Logo] 远程安装应用                              [_] [□] [X]   │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌─────────────────────────────────────────┐ │
│  │              │  │                                         │ │
│  │  服务器列表  │  │          应用市场                       │ │
│  │              │  │                                         │ │
│  │  [搜索框]    │  │  [搜索框]    [筛选器]                   │ │
│  │              │  │                                         │ │
│  │  🟢 Server1  │  │  ┌──────┐ ┌──────┐ ┌──────┐            │ │
│  │  🟢 Server2  │  │  │MySQL │ │Redis │ │Elastic│            │ │
│  │  🔴 Server3  │  │  │ 8.x  │ │ 7.x  │ │  8.x  │            │ │
│  │  🟢 Server4  │  │  │[安装]│ │[已装]│ │[安装]│            │ │
│  │              │  │  └──────┘ └──────┘ └──────┘            │ │
│  │  [+] 添加    │  │  ┌──────┐ ┌──────┐ ┌──────┐            │ │
│  │              │  │  │Rabbit│ │Nacos │ │Nginx │            │ │
│  │  分组：全部  │  │  │ 3.12 │ │ 2.3  │ │ 1.24 │            │ │
│  └──────────────┘  │  │[安装]│ │[安装]│ │[安装]│            │ │
│                    │  │ └──────┘ └──────┘ └──────┘            │ │
│                    │  │                                         │ │
│                    │  └─────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│  [设置] [关于]                        状态：就绪 | 并发：3/3   │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 连接管理对话框

```
┌─────────────────────────────────────────────────────┐
│              添加远程主机                      [X]   │
├─────────────────────────────────────────────────────┤
│                                                     │
│  基本信息                                            │
│  ┌─────────────────────────────────────────────┐   │
│  │ 主机名称 *  [____________________________]   │   │
│  │                                             │   │
│  │ IP 地址 *     [____________________________]   │   │
│  │                                             │   │
│  │ 端口 *        [____22_____]                  │   │
│  │                                             │   │
│  │ 用户名 *    [____________________________]   │   │
│  │                                             │   │
│  │ 操作系统 *    [▼ CentOS]                    │   │
│  │                                             │   │
│  │ 分组        [____________________________]   │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  认证方式                                            │
│  ○ 密码    ┌─────────────────────────────────────┐ │
│            │ 密码 *      [____________________]   │ │
│            │               [显示]                 │ │
│            └─────────────────────────────────────┘ │
│  ○ 私钥    [____________________________][浏览]   │
│            私钥密码 [____________________________] │
│                                                     │
│  [测试连接]  结果：──────────────────────────────── │
│                                                     │
│  ───────────────────────────────────────────────── │
│                       [取消]        [保存]          │
└─────────────────────────────────────────────────────┘
```

### 5.3 安装向导界面

```
┌─────────────────────────────────────────────────────┐
│              安装 MySQL 8.x                         │
├─────────────────────────────────────────────────────┤
│                                                     │
│  步骤进度                                            │
│  ● 配置参数 → ○ 上传文件 → ○ 执行安装 → ○ 完成     │
│                                                     │
│  配置参数                                            │
│  ┌─────────────────────────────────────────────┐   │
│  │ Root 密码 * [____________________________]   │   │
│  │ 端口 *        [____3306_____] (1024-65535)  │   │
│  │ 数据目录    [/var/lib/mysql ____________]   │   │
│  │ 字符集      [▼ utf8mb4]                     │   │
│  │ 最大连接数  [____150_____]                   │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  安装包来源                                          │
│  ○ 从仓库下载 (自动选择最新版本)                     │
│  ● 从本地选择  [C:\downloads\mysql-8.0.35.zip][浏览]│
│                                                     │
│  ───────────────────────────────────────────────── │
│              [上一步]    [开始安装]                 │
└─────────────────────────────────────────────────────┘
```

### 5.4 监控面板

```
┌─────────────────────────────────────────────────────┐
│  安装进度 - MySQL 8.x → Server1                 [X] │
├─────────────────────────────────────────────────────┤
│                                                     │
│  当前阶段：执行安装                                  │
│  ┌─────────────────────────────────────────────┐   │
│  │ ████████████████████░░░░░░░░░░  65%        │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  操作：[暂停] [取消]                                 │
│                                                     │
│  日志输出                                            │
│  ┌─────────────────────────────────────────────┐   │
│  │ [14:32:01] INFO  连接服务器成功...          │   │
│  │ [14:32:02] INFO  开始上传安装包...          │   │
│  │ [14:32:05] SUCCESS 上传完成 (125MB)         │   │
│  │ [14:32:06] INFO  解压安装包...              │   │
│  │ [14:32:10] INFO  配置 MySQL 参数...         │   │
│  │ [14:32:15] INFO  启动 MySQL 服务...         │   │
│  │ [14:32:18] SUCCESS 服务启动成功             │   │
│  │ [14:32:19] INFO  验证安装结果...            │   │
│  │ [14:32:20] SUCCESS 安装完成！               │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  [清空日志] [保存日志] [自动滚动 ●]                  │
└─────────────────────────────────────────────────────┘
```

---

## 6. 交互说明

### 6.1 主机管理交互

| 操作 | 交互方式 | 反馈 |
|------|----------|------|
| 添加主机 | 点击"添加"按钮 | 弹出对话框 |
| 编辑主机 | 右键菜单或双击 | 弹出对话框（预填数据） |
| 删除主机 | 右键菜单 → 删除 | 确认对话框 → 删除成功提示 |
| 测试连接 | 右键菜单 → 测试连接 | Toast 提示结果 |
| 批量操作 | Ctrl/Shift 多选 | 批量操作按钮激活 |

### 6.2 应用市场交互

| 操作 | 交互方式 | 反馈 |
|------|----------|------|
| 查看应用详情 | 点击应用卡片 | 展开详细信息 |
| 安装应用 | 点击"安装"按钮 | 弹出配置对话框 |
| 卸载应用 | 点击"卸载"按钮 | 确认对话框 → 执行卸载 |
| 刷新状态 | 右键菜单 → 刷新 | 重新检测应用状态 |
| 搜索应用 | 输入关键词 | 实时过滤列表 |

### 6.3 安装流程交互

```
1. 点击"安装" → 2. 配置参数 → 3. 开始安装 → 4. 监控进度
      ↓                ↓                ↓                ↓
   打开对话框      表单校验        打开监控面板      实时日志输出
                                                        ↓
                                                完成/失败提示
```

### 6.4 快捷键

| 快捷键 | 功能 |
|--------|------|
| Ctrl + N | 添加主机 |
| Ctrl + S | 保存 |
| Ctrl + F | 搜索 |
| Ctrl + W | 关闭当前窗口 |
| F5 | 刷新 |
| Esc | 关闭对话框/取消操作 |
| Delete | 删除选中项 |

### 6.5 动画效果

| 场景 | 动画类型 | 时长 |
|------|----------|------|
| 卡片悬停 | 阴影加深 + 轻微上浮 | 200ms |
| 按钮点击 | 缩放反馈 | 100ms |
| 对话框打开 | 淡入 + 缩放 | 250ms |
| 进度条更新 | 平滑过渡 | 150ms |
| Toast 提示 | 滑入/滑出 | 300ms |

---

## 7. XAML 代码示例

### 7.1 主窗口框架

```xaml
<materialDesign:DialogHost
    x:Class="RemoteInstaller.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
    xmlns:local="clr-namespace:RemoteInstaller"
    xmlns:conv="clr-namespace:RemoteInstaller.Converters"
    Title="远程安装应用"
    Width="1400" Height="850"
    MinWidth="1280" MinHeight="720"
    Background="{StaticResource MaterialDesignPaperBrush}"
    WindowStartupLocation="CenterScreen"
    ResizeMode="CanResize">

    <Window.Resources>
        <!-- 颜色资源 -->
        <Color x:Key="PrimaryColor">#6366F1</Color>
        <Color x:Key="SuccessColor">#10B981</Color>
        <Color x:Key="ErrorColor">#EF4444</Color>
        <Color x:Key="WarningColor">#F59E0B</Color>
        <Color x:Key="InfoColor">#3B82F6</Color>
        
        <SolidColorBrush x:Key="PrimaryBrush" Color="{StaticResource PrimaryColor}"/>
        <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
        <SolidColorBrush x:Key="ErrorBrush" Color="{StaticResource ErrorColor}"/>
        
        <!-- 转换器 -->
        <conv:BoolToVisibilityConverter x:Key="BoolToVis"/>
        <conv:StatusToColorConverter x:Key="StatusToColor"/>
        <conv:StatusToIconConverter x:Key="StatusToIcon"/>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- 标题栏 -->
            <RowDefinition Height="*"/>     <!-- 主内容区 -->
            <RowDefinition Height="Auto"/>  <!-- 状态栏 -->
        </Grid.RowDefinitions>

        <!-- 标题栏 -->
        <Border Grid.Row="0" 
                Background="{StaticResource MaterialDesignDarkBrush}"
                Padding="20,16">
            <DockPanel>
                <Image Source="pack://application:,,,/Assets/icon.png" 
                       Width="32" Height="32" 
                       VerticalAlignment="Center"/>
                <TextBlock Text="远程安装应用" 
                           FontSize="20" FontWeight="SemiBold"
                           Foreground="White"
                           VerticalAlignment="Center"
                           Margin="12,0,0,0"/>
                
                <StackPanel DockPanel.Dock="Right" 
                            Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <materialDesign:PackIcon Kind="ThemeLightDark"
                                             Width="24" Height="24"
                                             VerticalAlignment="Center"
                                             Margin="0,0,16,0"
                                             ToolTipService.ToolTip="切换主题"
                                             MouseLeftButtonUp="ThemeToggle_Click"/>
                    <materialDesign:PackIcon Kind="Cog"
                                             Width="24" Height="24"
                                             VerticalAlignment="Center"
                                             ToolTipService.ToolTip="设置"
                                             MouseLeftButtonUp="Settings_Click"/>
                </StackPanel>
            </DockPanel>
        </Border>

        <!-- 主内容区 -->
        <Grid Grid.Row="1" Margin="16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="300"/>  <!-- 服务器列表 -->
                <ColumnDefinition Width="*"/>    <!-- 应用市场 -->
            </Grid.ColumnDefinitions>

            <!-- 服务器列表面板 -->
            <Border Grid.Column="0" 
                    Style="{StaticResource AppCardStyle}"
                    Margin="0,0,16,0">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <!-- 标题 -->
                    <TextBlock Grid.Row="0"
                               Text="服务器列表"
                               FontSize="16" FontWeight="SemiBold"
                               Margin="0,0,0,16"/>

                    <!-- 搜索框 -->
                    <Grid Grid.Row="1" Margin="0,0,0,12">
                        <materialDesign:PackIcon Kind="Magnify"
                                                 VerticalAlignment="Center"
                                                 Margin="12,8"/>
                        <TextBox materialDesign:HintAssist.Hint="搜索服务器..."
                                 VerticalAlignment="Center"
                                 Padding="40,8,8,8"
                                 materialDesign:TextFieldAssist.DecorationVisibility="None"/>
                    </Grid>

                    <!-- 分组筛选 -->
                    <ComboBox Grid.Row="2"
                              materialDesign:HintAssist.Hint="分组"
                              Margin="0,0,0,12"
                              SelectedItem="{Binding SelectedGroup}"/>

                    <!-- 服务器列表 -->
                    <ListBox Grid.Row="3"
                             ItemsSource="{Binding Servers}"
                             SelectedItem="{Binding SelectedServer}"
                             ScrollViewer.VerticalScrollBarVisibility="Auto">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>

                                    <!-- 状态图标 -->
                                    <materialDesign:PackIcon Grid.Column="0"
                                                             Kind="{Binding Status, Converter={StaticResource StatusToIcon}}"
                                                             Foreground="{Binding Status, Converter={StaticResource StatusToColor}}"
                                                             Width="16" Height="16"
                                                             VerticalAlignment="Center"
                                                             Margin="0,0,8,0"/>

                                    <!-- 服务器信息 -->
                                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                        <TextBlock Text="{Binding Name}" 
                                                   FontWeight="Medium"
                                                   Foreground="{StaticResource MaterialDesignBodyLightBrush}"/>
                                        <TextBlock Text="{Binding IpAddress}" 
                                                   FontSize="12"
                                                   Foreground="{StaticResource MaterialDesignBodyDisabledBrush}"/>
                                    </StackPanel>

                                    <!-- 操作菜单 -->
                                    <materialDesign:PackIcon Grid.Column="2"
                                                             Kind="DotsVertical"
                                                             VerticalAlignment="Center"
                                                             Foreground="{StaticResource MaterialDesignBodyLightBrush}"
                                                             Cursor="Hand"
                                                             MouseLeftButtonUp="ServerMenu_Click"/>
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>

                    <!-- 添加按钮 -->
                    <Button Grid.Row="4"
                            Content="添加服务器"
                            Style="{StaticResource PrimaryButtonStyle}"
                            Margin="0,16,0,0"
                            Command="{Binding AddServerCommand}"/>
                </Grid>
            </Border>

            <!-- 应用市场面板 -->
            <Border Grid.Column="1" 
                    Style="{StaticResource AppCardStyle}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <!-- 标题和搜索 -->
                    <Grid Grid.Row="0" Margin="0,0,0,16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column="0"
                                   Text="应用市场"
                                   FontSize="16" FontWeight="SemiBold"/>

                        <Grid Grid.Column="1" Margin="0,0,16,0">
                            <materialDesign:PackIcon Kind="Magnify"
                                                     VerticalAlignment="Center"
                                                     Margin="12,8"/>
                            <TextBox materialDesign:HintAssist.Hint="搜索应用..."
                                     VerticalAlignment="Center"
                                     Padding="40,8,8,8"
                                     Width="200"
                                     materialDesign:TextFieldAssist.DecorationVisibility="None"/>
                        </Grid>

                        <ComboBox Grid.Column="2"
                                  Width="120"
                                  materialDesign:HintAssist.Hint="筛选"
                                  ItemsSource="{Binding PlatformFilters}"/>
                    </Grid>

                    <!-- 应用卡片网格 -->
                    <WrapPanel Grid.Row="1">
                        <!-- 应用卡片模板 -->
                        <Border Style="{StaticResource AppCardStyle}"
                                Width="200" Margin="0,0,16,16">
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                </Grid.RowDefinitions>

                                <!-- 应用图标 -->
                                <Image Grid.Row="0"
                                       Source="{Binding IconPath}"
                                       Width="48" Height="48"
                                       Margin="0,0,0,12"/>

                                <!-- 应用名称 -->
                                <TextBlock Grid.Row="1"
                                           Text="{Binding Name}"
                                           FontSize="16" FontWeight="SemiBold"
                                           Margin="0,0,0,4"/>

                                <!-- 版本号 -->
                                <TextBlock Grid.Row="2"
                                           Text="{Binding Version}"
                                           FontSize="12"
                                           Foreground="{StaticResource MaterialDesignBodyDisabledBrush}"
                                           Margin="0,0,0,8"/>

                                <!-- 描述 -->
                                <TextBlock Grid.Row="3"
                                           Text="{Binding Description}"
                                           FontSize="12"
                                           Foreground="{StaticResource MaterialDesignBodyLightBrush}"
                                           TextWrapping="Wrap"
                                           Margin="0,0,0,12"/>

                                <!-- 操作按钮 -->
                                <Button Grid.Row="4"
                                        Content="{Binding InstallStatus, Converter={StaticResource StatusToButtonText}}"
                                        Style="{StaticResource PrimaryButtonStyle}"
                                        Command="{Binding InstallCommand}"
                                        CommandParameter="{Binding}"/>
                            </Grid>
                        </Border>
                    </WrapPanel>
                </Grid>
            </Border>
        </Grid>

        <!-- 状态栏 -->
        <Border Grid.Row="2"
                Background="{StaticResource MaterialDesignDarkBrush}"
                Padding="16,8"
                Height="40">
            <DockPanel>
                <TextBlock Text="状态：就绪" 
                           Foreground="White"
                           VerticalAlignment="Center"/>
                <TextBlock DockPanel.Dock="Right"
                           Text="并发任务：0/3" 
                           Foreground="White"
                           VerticalAlignment="Center"/>
            </DockPanel>
        </Border>
    </Grid>
</materialDesign:DialogHost>
```

### 7.2 添加主机对话框

```xaml
<materialDesign:DialogHost.DialogContent>
    <materialDesign:Card Padding="24" MinWidth="500">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- 标题 -->
            <TextBlock Grid.Row="0"
                       Text="添加远程主机"
                       FontSize="20" FontWeight="SemiBold"
                       Margin="0,0,0,24"/>

            <!-- 基本信息 -->
            <Expander Grid.Row="1" 
                      Header="基本信息"
                      IsExpanded="True"
                      Margin="0,0,0,12">
                <Grid Margin="0,12,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Row="0,1" Grid.Column="0"
                               Text="主机名称 *" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <TextBox Grid.Row="0" Grid.Column="1"
                             Text="{Binding HostName}"
                             Style="{StaticResource FormInputStyle}"/>

                    <TextBlock Grid.Row="1,1" Grid.Column="0"
                               Text="IP 地址 *" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <TextBox Grid.Row="1" Grid.Column="1"
                             Text="{Binding IpAddress}"
                             Style="{StaticResource FormInputStyle}"/>

                    <TextBlock Grid.Row="2,1" Grid.Column="0"
                               Text="端口 *" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <TextBox Grid.Row="2" Grid.Column="1"
                             Text="{Binding Port, StringFormat={}{0:0}}"
                             Style="{StaticResource FormInputStyle}"
                             Width="120"/>

                    <TextBlock Grid.Row="3,1" Grid.Column="0"
                               Text="用户名 *" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <TextBox Grid.Row="3" Grid.Column="1"
                             Text="{Binding Username}"
                             Style="{StaticResource FormInputStyle}"/>

                    <TextBlock Grid.Row="4,1" Grid.Column="0"
                               Text="操作系统 *" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <ComboBox Grid.Row="4" Grid.Column="1"
                              ItemsSource="{Binding OperatingSystems}"
                              SelectedItem="{Binding SelectedOs}"
                              Style="{StaticResource MaterialDesignFloatingHintComboBox}"/>

                    <TextBlock Grid.Row="5,1" Grid.Column="0"
                               Text="分组" 
                               VerticalAlignment="Center"
                               Margin="0,0,16,0"/>
                    <TextBox Grid.Row="5" Grid.Column="1"
                             Text="{Binding GroupName}"
                             Style="{StaticResource FormInputStyle}"/>
                </Grid>
            </Expander>

            <!-- 认证方式 -->
            <Expander Grid.Row="2" 
                      Header="认证方式"
                      IsExpanded="True"
                      Margin="0,12,0,12">
                <Grid Margin="0,12,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <RadioButton Content="密码" 
                                 IsChecked="{Binding UsePasswordAuth}"
                                 GroupName="AuthType"
                                 Margin="0,0,0,12"/>
                    <PasswordBox Grid.Row="1"
                                 materialDesign:PasswordBoxAssist.ToggleButtonVisibility="Visible"
                                 Margin="0,0,0,12"
                                 Visibility="{Binding UsePasswordAuth, Converter={StaticResource BoolToVis}}"/>

                    <RadioButton Content="私钥" 
                                 IsChecked="{Binding UseKeyAuth}"
                                 GroupName="AuthType"
                                 Margin="0,0,0,8"/>
                    <Grid Grid.Row="1"
                          Visibility="{Binding UseKeyAuth, Converter={StaticResource BoolToVis}}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBox Text="{Binding KeyPath}"
                                 materialDesign:HintAssist.Hint="私钥文件路径"/>
                        <Button Grid.Column="1" 
                                Content="浏览" 
                                Margin="8,0,0,0"/>
                    </Grid>
                </Grid>
            </Expander>

            <!-- 测试结果 -->
            <Border Grid.Row="3"
                    Background="{StaticResource MaterialDesignDarkBrush}"
                    CornerRadius="8" Padding="16"
                    Margin="0,12,0,0">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <materialDesign:PackIcon Kind="NetworkCheck"
                                             Foreground="{StaticResource SuccessBrush}"
                                             VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding TestResult}"
                               VerticalAlignment="Center"
                               Margin="12,0,0,0"/>
                </Grid>
            </Border>

            <!-- 按钮 -->
            <Grid Grid.Row="4" Margin="0,24,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <Button Grid.Column="1"
                        Content="测试连接"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Margin="0,0,16,0"
                        Command="{Binding TestConnectionCommand}"/>
                <Button Grid.Column="2"
                        Content="保存"
                        Style="{StaticResource PrimaryButtonStyle}"
                        Command="{Binding SaveCommand}"/>
            </Grid>
        </Grid>
    </materialDesign:Card>
</materialDesign:DialogHost.DialogContent>
```

### 7.3 安装进度监控窗口

```xaml
<materialDesign:DialogHost.DialogContent>
    <materialDesign:Card Padding="24" MinWidth="700" MinHeight="400">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- 标题 -->
            <Grid Grid.Row="0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Title}" 
                           FontSize="18" FontWeight="SemiBold"/>
                <Button Grid.Column="1"
                        Style="{StaticResource MaterialDesignToolbutton}"
                        ToolTipService.ToolTip="最小化到托盘"
                        Margin="16,0,0,0">
                    <materialDesign:PackIcon Kind="ArrowDown"/>
                </Button>
            </Grid>

            <!-- 步骤进度 -->
            <Grid Grid.Row="1" Margin="0,24,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" Orientation="Horizontal">
                    <StackPanel Orientation="Horizontal" Margin="0,0,16,0">
                        <materialDesign:PackIcon Kind="CheckCircle"
                                                 Foreground="{StaticResource SuccessBrush}"/>
                        <TextBlock Text="配置参数" 
                                   FontSize="12" Margin="4,0,0,0"/>
                    </StackPanel>
                    <materialDesign:PackIcon Kind="ChevronRight"/>
                    <StackPanel Orientation="Horizontal" Margin="16,0,16,0">
                        <materialDesign:PackIcon Kind="CheckCircle"
                                                 Foreground="{StaticResource SuccessBrush}"/>
                        <TextBlock Text="上传文件" 
                                   FontSize="12" Margin="4,0,0,0"/>
                    </StackPanel>
                    <materialDesign:PackIcon Kind="ChevronRight"/>
                    <StackPanel Orientation="Horizontal" Margin="16,0,16,0">
                        <materialDesign:PackIcon Kind="Loading"
                                                 Foreground="{StaticResource PrimaryBrush}"/>
                        <TextBlock Text="执行安装" 
                                   FontSize="12" FontWeight="Medium"
                                   Margin="4,0,0,0"/>
                    </StackPanel>
                    <materialDesign:PackIcon Kind="ChevronRight"
                                             Foreground="{StaticResource MaterialDesignBodyDisabledBrush}"/>
                    <StackPanel Orientation="Horizontal" Margin="16,0,0,0">
                        <materialDesign:PackIcon Kind="CircleOutline"
                                                 Foreground="{StaticResource MaterialDesignBodyDisabledBrush}"/>
                        <TextBlock Text="完成" 
                                   FontSize="12"
                                   Foreground="{StaticResource MaterialDesignBodyDisabledBrush}"
                                   Margin="4,0,0,0"/>
                    </StackPanel>
                </StackPanel>
                <TextBlock Grid.Column="1"
                           Text="{Binding CurrentStage}"
                           FontSize="14" FontWeight="Medium"
                           Foreground="{StaticResource PrimaryBrush}"/>
            </Grid>

            <!-- 进度条 -->
            <Grid Grid.Row="2" Margin="0,24,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <ProgressBar Grid.Column="0"
                             Value="{Binding Progress}"
                             Style="{StaticResource InstallationProgressBarStyle}"/>
                <TextBlock Grid.Column="1"
                           Text="{Binding Progress, StringFormat={}{0:0}%}"
                           VerticalAlignment="Center"
                           Margin="12,0,0,0"/>
            </Grid>

            <!-- 操作按钮 -->
            <Grid Grid.Row="3" Margin="0,16,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Button Content="暂停"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Command="{Binding PauseCommand}"
                        IsEnabled="{Binding IsRunning}"/>
                <Button Grid.Column="1"
                        Content="取消"
                        Style="{StaticResource DangerButtonStyle}"
                        Command="{Binding CancelCommand}"
                        IsEnabled="{Binding IsRunning}"
                        Margin="8,0,0,0"/>
            </Grid>

            <!-- 日志输出 -->
            <Border Grid.Row="4"
                    Background="{StaticResource MaterialDesignDarkBrush}"
                    CornerRadius="8" Padding="16"
                    Margin="0,16,0,0">
                <ScrollViewer x:Name="LogScrollViewer"
                              VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding LogEntries}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Message}"
                                           Style="{Binding Level, Converter={StaticResource LogLevelToStyle}}"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>

            <!-- 日志操作 -->
            <Grid Grid.Row="5" Margin="0,12,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Button Content="清空日志"
                        Style="{StaticResource MaterialDesignFlatButton}"
                        Command="{Binding ClearLogCommand}"/>
                <Button Grid.Column="1"
                        Content="保存日志"
                        Style="{StaticResource MaterialDesignFlatButton}"
                        Command="{Binding SaveLogCommand}"
                        Margin="8,0,0,0"/>
                <CheckBox Grid.Column="3"
                          Content="自动滚动"
                          IsChecked="{Binding AutoScroll}"
                          VerticalAlignment="Center"/>
            </Grid>
        </Grid>
    </materialDesign:Card>
</materialDesign:DialogHost.DialogContent>
```

---

## 8. 资源文件

### 8.1 App.xaml

```xaml
<Application x:Class="RemoteInstaller.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- MaterialDesignInXaml 基础主题 -->
                <materialDesign:BundledTheme BaseTheme="Dark" 
                                             PrimaryColor="Indigo" 
                                             SecondaryColor="Cyan"/>
                <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml"/>
                
                <!-- 自定义主题 -->
                <ResourceDictionary Source="Themes/CustomColors.xaml"/>
                <ResourceDictionary Source="Themes/CustomStyles.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 8.2 CustomColors.xaml

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- 自定义颜色 -->
    <Color x:Key="CustomPrimary">#6366F1</Color>
    <Color x:Key="CustomSecondary">#06B6D4</Color>
    <Color x:Key="CustomSuccess">#10B981</Color>
    <Color x:Key="CustomError">#EF4444</Color>
    <Color x:Key="CustomWarning">#F59E0B</Color>
    <Color x:Key="CustomInfo">#3B82F6</Color>
    
    <SolidColorBrush x:Key="CustomPrimaryBrush" Color="{StaticResource CustomPrimary}"/>
    <SolidColorBrush x:Key="CustomSecondaryBrush" Color="{StaticResource CustomSecondary}"/>
    <SolidColorBrush x:Key="CustomSuccessBrush" Color="{StaticResource CustomSuccess}"/>
    <SolidColorBrush x:Key="CustomErrorBrush" Color="{StaticResource CustomError}"/>
    <SolidColorBrush x:Key="CustomWarningBrush" Color="{StaticResource CustomWarning}"/>
    <SolidColorBrush x:Key="CustomInfoBrush" Color="{StaticResource CustomInfo}"/>
    
    <!-- 深色主题背景 -->
    <Color x:Key="DarkBackground">#121212</Color>
    <Color x:Key="DarkSurface">#1E1E1E</Color>
    <Color x:Key="DarkSurfaceLight">#2D2D2D</Color>
    
    <SolidColorBrush x:Key="DarkBackgroundBrush" Color="{StaticResource DarkBackground}"/>
    <SolidColorBrush x:Key="DarkSurfaceBrush" Color="{StaticResource DarkSurface}"/>
    <SolidColorBrush x:Key="DarkSurfaceLightBrush" Color="{StaticResource DarkSurfaceLight}"/>
    
    <!-- 浅色主题背景 -->
    <Color x:Key="LightBackground">#F9FAFB</Color>
    <Color x:Key="LightSurface">#FFFFFF</Color>
    <Color x:Key="LightSurfaceLight">#F3F4F6</Color>
    
    <SolidColorBrush x:Key="LightBackgroundBrush" Color="{StaticResource LightBackground}"/>
    <SolidColorBrush x:Key="LightSurfaceBrush" Color="{StaticResource LightSurface}"/>
    <SolidColorBrush x:Key="LightSurfaceLightBrush" Color="{StaticResource LightSurfaceLight}"/>
    
</ResourceDictionary>
```

---

## 9. 响应式布局

### 9.1 断点定义

| 断点 | 最小宽度 | 布局调整 |
|------|----------|----------|
| Desktop | 1280px | 完整双栏布局 |
| Tablet | 1024px | 缩小侧边栏宽度 |
| Compact | 768px | 单栏布局，服务器列表折叠 |

### 9.2 响应式触发器

```xaml
<Grid>
    <Grid.Style>
        <Style TargetType="Grid">
            <Style.Triggers>
                <Trigger Width="{Binding ActualWidth, RelativeSource={RelativeSource Self}}">
                    <!-- 小屏幕时调整布局 -->
                </Trigger>
            </Style.Triggers>
        </Style>
    </Grid.Style>
</Grid>
```

---

## 10. 主题切换

### 10.1 主题切换实现

```csharp
// 在 App.xaml.cs 或 ViewModel 中
public void ToggleTheme()
{
    var theme = Theme.GetAppTheme();
    var newTheme = theme.BaseTheme == BaseTheme.Dark 
        ? new Theme(BaseTheme.Light, theme.PrimaryColor, theme.SecondaryColor)
        : new Theme(BaseTheme.Dark, theme.PrimaryColor, theme.SecondaryColor);
    
    ThemeSetter.SetTheme(this, newTheme);
}
```

---

**文档结束**

> 🦁 **UI 设计规范已完成！** 包含：
> - ✅ 深色/浅色双主题配色方案
> - ✅ 完整的组件样式定义
> - ✅ 4 个主要界面的详细布局
> - ✅ 交互说明和快捷键
> - ✅ 可直接使用的 XAML 代码示例
> - ✅ 响应式布局支持
