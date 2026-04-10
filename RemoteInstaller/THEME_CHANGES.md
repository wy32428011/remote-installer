# 主题切换功能完善 - 变更清单

## 任务概述
完善远程安装应用的主题切换功能，使所有控件（服务器列表、应用列表、任务列表等）都能响应主题切换。

## 变更文件

### 1. 新建文件

#### `Themes/DarkTheme.xaml`
- **功能**: 深色主题资源文件
- **内容**:
  - 基础颜色定义（背景、卡片背景、边框、文本颜色、强调色）
  - MaterialDesign 主题颜色覆盖
  - Border 样式
  - TextBlock 样式
  - Button 样式（FlatButton、ToolButton）
  - ListBox 和 ListBoxItem 样式
  - DataGrid 样式
  - TextBox 样式
  - ComboBox 和 ComboBoxItem 样式
  - ProgressBar 样式
  - ScrollViewer 样式
  - ContextMenu 样式
  - 日志相关样式（Info、Success、Warning、Error）

#### `Themes/LightTheme.xaml`
- **功能**: 浅色主题资源文件
- **内容**: 与 DarkTheme.xaml 结构相同，但使用浅色配色方案

### 2. 修改文件

#### `App.xaml`
- **变更**:
  - 移除主题资源的 x:Key 标识方式
  - 默认加载深色主题资源
  - 主题切换通过代码动态替换资源字典

#### `App.xaml.cs`
- **新增**:
  - `CurrentThemeType` 静态属性：跟踪当前主题
  - `ThemeChanged` 静态事件：主题切换事件
  - `SwitchTheme(ThemeType)` 静态方法：主题切换逻辑
    - 清除现有主题资源字典
    - 加载新主题资源字典
    - 触发主题变更事件

#### `ViewModels/MainViewModel.cs`
- **修改 `ApplyTheme` 方法**:
  - 调用 `App.SwitchTheme()` 进行完整的主题切换
  - 更新窗口背景色
  - 添加主题切换日志

#### `MainWindow.xaml`
- **修改**:
  - 日志区域背景色从硬编码 `#1E1E1E` 改为动态资源 `{DynamicResource MaterialDesign50}`
  - 多处 `Foreground="Gray"` 改为 `{DynamicResource MaterialDesignBodyLight}`
  - 确保所有控件使用动态资源引用

## 配色方案

### 深色主题
| 元素 | 颜色值 |
|------|--------|
| 背景 | #1E1E1E |
| 卡片背景 | #2D2D2D |
| 边框 | #3D3D3D |
| 主文本 | #FFFFFF |
| 次文本 | #B0B0B0 |
| 强调色 | #3B82F6 |

### 浅色主题
| 元素 | 颜色值 |
|------|--------|
| 背景 | #FFFFFF |
| 卡片背景 | #F5F5F5 |
| 边框 | #E0E0E0 |
| 主文本 | #1A1A1A |
| 次文本 | #666666 |
| 强调色 | #3B82F6 |

## 主题切换流程

1. 用户点击主题切换按钮
2. `MainViewModel.ToggleThemeCommand` 被触发
3. 调用 `ApplyTheme(newTheme)` 方法
4. `App.SwitchTheme(theme)` 执行主题切换：
   - 更新主窗口资源字典
   - 所有使用 `{DynamicResource}` 的控件自动更新
5. 触发 `ThemeChanged` 事件通知其他组件
6. 主题设置保存到数据库

## 测试建议

1. 启动应用，默认为深色主题
2. 点击主题切换按钮，切换到浅色主题
3. 检查以下控件是否正确切换：
   - 窗口背景色
   - 服务器列表边框和背景
   - 应用市场卡片
   - 任务列表
   - 日志区域
   - 所有按钮样式
   - 所有文本颜色
4. 再次切换回深色主题，验证切换效果
5. 关闭应用后重新启动，验证主题设置是否持久化

## 注意事项

1. 所有需要响应主题切换的控件必须使用 `{DynamicResource}` 而不是 `{StaticResource}`
2. 硬编码的颜色值需要改为动态资源引用
3. MaterialDesign 控件通过主题资源字典自动适配
4. 主题切换时，窗口资源字典会被替换，确保所有样式定义在主题资源文件中
