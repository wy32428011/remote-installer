# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## 项目概述

**远程安装应用 (Remote Installer)** - 基于 C# WPF 的跨平台服务器管理工具，支持在 Windows Server、CentOS、Ubuntu 上远程安装和管理中间件（MySQL、Redis、Elasticsearch、RabbitMQ、Nacos、Nginx）。

## 构建命令

```bash
# 还原依赖并构建
dotnet build RemoteInstaller.sln

# 运行应用
dotnet run --project RemoteInstaller/RemoteInstaller.csproj

# 运行测试
dotnet test

# 发布 Release 版本
dotnet publish RemoteInstaller/RemoteInstaller.csproj -c Release -o publish
```

## 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│  WPF 客户端 (MaterialDesignInXaml + CommunityToolkit.MVVM)  │
├─────────────────────────────────────────────────────────────┤
│  View 层 (XAML)                                             │
│  ├─ MainWindow, Views/Dialogs/*, Views/Controls/*          │
├─────────────────────────────────────────────────────────────┤
│  ViewModel 层 (CommunityToolkit.Mvvm)                       │
│  ├─ MainViewModel, AddHostViewModel, InstallProgressViewModel│
│  ├─ SettingsViewModel, TerminalViewModel                    │
├─────────────────────────────────────────────────────────────┤
│  Service 层                                                 │
│  ├─ SshService (SSH/SFTP 连接)                              │
│  ├─ InstallerService (安装/卸载逻辑)                        │
│  ├─ DatabaseService (SQLite 数据持久化)                      │
│  ├─ EncryptionService (AES-256 加密)                        │
│  ├─ LoggerService / FileLoggerService (日志)               │
│  ├─ LogCollector (远程日志收集)                              │
│  └─ HostManagerService (主机管理)                           │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure                                             │
│  ├─ Renci.SshNet (SSH/SFTP)                                │
│  ├─ Microsoft.Data.Sqlite (SQLite)                         │
│  └─ System.Security.Cryptography (AES-256)                 │
└─────────────────────────────────────────────────────────────┘
```

## 核心约定

- **MVVM 模式**: View 通过 DataBinding 绑定 ViewModel，ViewModel 通过 DependencyInjection 获取 Service
- **依赖注入**: Services 在 `App.xaml.cs` 中注册，通过构造函数注入 ViewModel
- **值转换器**: `Converters/Converters.cs` 中的转换器用于 XAML 数据绑定格式化
- **加密存储**: 主机密码使用 `EncryptionService` 加密后存入 SQLite
- **主题**: 支持亮色/暗色主题，主题资源在 `Themes/` 目录
- **日志**: 使用 Serilog 写入 `logs/app-YYYY-MM-DD.log`，`LogCollector` 通过 SSH 收集远程日志
- **安装脚本**: Bash 脚本存于 `Scripts/Linux/`，PowerShell 脚本存于 `Scripts/Windows/`

## 关键文件

| 文件 | 用途 |
|------|------|
| `App.xaml.cs` | 应用入口，DI 容器配置 |
| `Locator.cs` | ViewModel 定位器（已标记 Obsolete，推荐使用 DI） |
| `Models/RemoteHost.cs` | 主机模型，含连接信息 |
| `Models/InstallTask.cs` | 安装任务状态模型 |
| `Services/SshService.cs` | SSH 连接、会话管理、命令执行 |
| `Services/InstallerService.cs` | 安装/卸载/检测核心逻辑 |
| `Services/DatabaseService.cs` | SQLite CRUD 操作 |
| `ViewModels/MainViewModel.cs` | 主窗口业务逻辑 |

## 远程脚本协议

脚本通过 `PROGRESS:阶段:百分比` 格式上报进度，普通输出作为日志。`InstallerService` 解析此格式并触发 `ProgressChanged` 事件。
