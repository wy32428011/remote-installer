# 安装/卸载功能实现计划

> **创建日期**: 2026-03-18  
> **开发者**: Leon 🦁  
> **状态**: ✅ 核心功能已完成

---

## 已完成的工作

### 1. 服务层 ✅
- ✅ `InstallerService.cs` - 完整的安装/卸载/检测逻辑
- ✅ `SshService.cs` - SSH 连接和命令执行
- ✅ `LogCollector.cs` - 日志收集器
- ✅ `DatabaseService.cs` - 数据库操作

### 2. 模型层 ✅
- ✅ `InstallTask.cs` - 安装任务模型
- ✅ `ApplicationStatus.cs` - 应用状态模型
- ✅ `ApplicationInfo.cs` - 应用信息模型
- ✅ `RemoteHost.cs` - 远程主机模型

### 3. ViewModel 层 ✅
- ✅ `ApplicationCardViewModel.cs` - 应用卡片命令
- ✅ `MainViewModel.cs` - 完整的安装/卸载/检测/配置逻辑

### 4. 今日完成 (2026-03-18) ✅
- ✅ 实现 `InstallApplicationCommand` - 安装应用命令
  - ✅ 调用 `InstallerService.InstallAsync()` 执行实际安装
  - ✅ 完整的异常处理
  - ✅ 安装成功后更新应用状态
  - ✅ 友好的错误提示
- ✅ 实现 `UninstallApplicationCommand` - 卸载应用命令
  - ✅ 调用 `InstallerService.UninstallAsync()` 执行实际卸载
  - ✅ 卸载确认对话框
  - ✅ 卸载成功后更新应用状态
- ✅ 实现 `CheckApplicationStatusCommand` - 检测应用状态命令
  - ✅ 调用 `InstallerService.CheckStatusAsync()` 执行实际检测
  - ✅ 30 秒超时控制
  - ✅ 更新应用卡片的状态显示
  - ✅ 显示版本信息
- ✅ 实现辅助方法
  - ✅ `GetHostFromViewModel()` - 从 ViewModel 获取 Host 对象
  - ✅ `GetApplicationInfo()` - 获取应用信息（6 个应用）
- ✅ 编译成功 (0 错误，213 警告)

---

## 待实现的功能

### 1. 安装配置对话框 ⏳
- [ ] 创建 `InstallConfigDialog.xaml`
- [ ] 动态生成配置表单
- [ ] 参数校验
- [ ] 集成到安装流程

### 2. 安装进度对话框 ⏳
- [ ] 创建 `InstallProgressDialog.xaml`
- [ ] 显示进度条和日志
- [ ] 支持取消操作
- [ ] 实时进度更新

### 3. 本地安装包支持 ⏳
- [ ] 文件选择对话框
- [ ] 集成到安装流程
- [ ] 上传进度显示

### 4. 批量操作完善 ⏳
- [ ] 批量安装实际调用
- [ ] 批量卸载实际调用
- [ ] 批量检测状态实际调用

---

## 实现进度

### ✅ 第一步：完善 MainViewModel 的核心逻辑 - 已完成
- [x] 实现 `InstallApplication()` 
- [x] 实现 `UninstallApplication()`
- [x] 实现 `CheckApplicationStatus()`
- [x] 实现 `ConfigureApplication()`
- [x] 集成 `InstallerService`
- [x] 处理异常和错误提示
- [x] 更新 UI 状态

### ⏳ 第二步：实现安装配置对话框
- [ ] 设计 UI 界面
- [ ] 实现参数绑定
- [ ] 参数校验逻辑

### ⏳ 第三步：实现安装进度对话框
- [ ] 设计 UI 界面
- [ ] 绑定进度和日志
- [ ] 实现取消功能

### ⏳ 第四步：测试和调试
- [ ] 单元测试
- [ ] 集成测试
- [ ] 真实环境测试

---

## 技术要点

### 1. 异步处理 ✅
- ✅ 使用 `async/await` 避免 UI 阻塞
- ✅ 使用 `CancellationToken` 支持取消操作
- ⏳ 使用 `IProgress<T>` 报告进度

### 2. 线程安全 ✅
- ✅ UI 更新在 UI 线程
- ✅ 使用 `ObservableProperty` 自动通知

### 3. 错误处理 ✅
- ✅ 捕获所有异常
- ✅ 友好的错误提示
- ✅ 详细的日志记录

### 4. 用户体验 ✅
- ✅ 进度实时反馈（通过日志）
- ✅ 日志实时滚动
- ⏳ 支持取消操作（需要进度对话框）

---

## 下一步计划

**建议优先实现：**
1. **安装配置对话框** - 让用户可以配置安装参数
2. **安装进度对话框** - 提供更好的用户体验
3. **本地安装包支持** - 支持离线安装

**老大，核心功能已经实现！现在可以测试安装/卸载/检测功能了。接下来建议实现配置对话框和进度对话框，提升用户体验。你觉得呢？🦁**

