# 远程安装应用 (Remote Installer)

一个基于 C# + WPF 的跨平台服务器管理工具，支持在 Windows Server、CentOS、Ubuntu 上远程安装和管理中间件应用。

## 功能特性

### 核心功能 (P0)

- ✅ **主机管理**
  - 添加/编辑/删除远程主机
  - 支持 SSH 密码和私钥认证
  - 测试连接功能
  - 主机在线状态检测

- ✅ **应用市场**
  - 展示可安装应用列表
  - 支持 6 个应用：MySQL、Redis、Elasticsearch、RabbitMQ、Nacos、Nginx
  - 显示应用详情和安装状态

- ✅ **安装配置**
  - 动态参数配置表单
  - 参数校验（必填、格式、范围）
  - 支持本地安装包选择

- ✅ **安装执行**
  - 上传安装包到远程服务器
  - 执行远程安装脚本
  - 传递配置参数
  - 实时进度监控

- ✅ **进度监控**
  - 进度百分比显示
  - 安装阶段指示
  - 实时日志输出
  - 日志级别着色

- ✅ **应用检测**
  - 检测应用是否已安装
  - 显示已安装版本号
  - 检测服务运行状态

- ✅ **应用卸载**
  - 执行卸载脚本
  - 卸载确认提示

- ✅ **数据存储**
  - 主机信息加密存储（AES-256）
  - 任务历史记录
  - SQLite 本地数据库

## 技术栈

| 层级 | 技术选型 |
|------|----------|
| UI 框架 | WPF (.NET 8) |
| MVVM 框架 | CommunityToolkit.MVVM |
| UI 组件库 | MaterialDesignInXaml |
| 远程通信 | Renci.SshNet (SSH/SFTP) |
| 本地存储 | SQLite |
| 加密 | AES-256 |
| 日志 | Serilog |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |

## 项目结构

```
RemoteInstaller/
├── Models/                    # 数据模型
│   ├── Enums.cs              # 枚举定义
│   ├── RemoteHost.cs         # 远程主机模型
│   ├── ApplicationInfo.cs    # 应用信息模型
│   ├── InstallTask.cs        # 安装任务模型
│   ├── LogEntry.cs           # 日志条目模型
│   └── ApplicationStatus.cs  # 应用状态模型
├── ViewModels/               # ViewModel 层
│   ├── BaseViewModel.cs      # 基础 ViewModel
│   ├── MainViewModel.cs      # 主窗口 ViewModel
│   ├── AddHostViewModel.cs   # 添加主机对话框 ViewModel
│   ├── InstallConfigViewModel.cs  # 安装配置 ViewModel
│   └── InstallProgressViewModel.cs # 进度监控 ViewModel
├── Views/                    # View 层
│   ├── MainWindow.xaml       # 主窗口
│   └── MainWindow.xaml.cs
├── Services/                 # 服务层
│   ├── EncryptionService.cs  # 加密服务
│   ├── DatabaseService.cs    # 数据库服务
│   ├── SshService.cs         # SSH 连接服务
│   ├── InstallerService.cs   # 安装服务
│   └── LoggerService.cs      # 日志服务
├── Converters/               # 值转换器
│   └── Converters.cs
├── Themes/                   # 主题资源
│   ├── CustomColors.xaml
│   └── CustomStyles.xaml
├── Assets/                   # 资源文件
├── Scripts/                  # 安装脚本
├── App.xaml                  # 应用入口
├── App.xaml.cs
└── RemoteInstaller.csproj

RemoteInstaller.Tests/        # 单元测试
├── EncryptionServiceTests.cs
├── RemoteHostTests.cs
├── InstallTaskTests.cs
└── ApplicationInfoTests.cs
```

## 快速开始

### 前置要求

- .NET 8 SDK
- Visual Studio 2022 (推荐) 或 VS Code
- Windows 10/11 或 Windows Server 2016+

### 构建项目

```bash
# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行应用
dotnet run --project RemoteInstaller/RemoteInstaller.csproj

# 运行测试
dotnet test
```

### 使用 Visual Studio

1. 打开 `RemoteInstaller.sln`
2. 还原 NuGet 包
3. 按 F5 运行

## 配置说明

### 数据库文件

应用首次启动时会在程序目录创建 `data.db` SQLite 数据库文件，用于存储：
- 主机连接信息（密码加密存储）
- 任务历史记录
- 系统设置

### 日志文件

日志文件保存在 `logs/` 目录下，按天轮转：
- `app-YYYY-MM-DD.log`

### 安装包仓库

默认从本地选择安装包，后续版本支持配置远程仓库地址。

## 支持的应用

| 应用 | 版本 | Linux | Windows | 说明 |
|------|------|-------|---------|------|
| MySQL | 8.x | ✅ | ✅ | 关系型数据库 |
| Redis | 7.x | ✅ | ✅ | 内存数据库 |
| Elasticsearch | 8.x | ✅ | ✅ | 搜索引擎 |
| RabbitMQ | 3.12.x | ✅ | ✅ | 消息队列 |
| Nacos | 2.3.x | ✅ | ✅ | 服务注册中心 |
| Nginx | 1.24.x | ✅ | ✅ | Web 服务器 |

## 开发计划

### P1 - 重要功能

- [ ] 主机分组管理
- [ ] 批量测试连接
- [ ] 应用搜索/筛选
- [ ] 从远程仓库拉取安装包
- [ ] 任务控制（暂停/继续/取消）
- [ ] 系统设置界面
- [ ] 批量操作支持

### P2 - 增强功能

- [ ] 主题切换（亮色/暗色）
- [ ] 断线自动重连
- [ ] 失败自动重试
- [ ] 日志搜索功能
- [ ] 自定义安装脚本
- [ ] 应用配置管理

## 贡献指南

1. Fork 项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 许可证

MIT License

## 作者

Leon 🦁 - [初始创建]

---

**版本**: 1.0.0  
**最后更新**: 2026-03-16
