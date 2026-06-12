# 远程安装应用 (Remote Installer)

一个运行在 Windows 上的 WPF 桌面工具，用于通过 SSH 连接远程主机，完成应用安装、卸载、状态检测、终端运维、脚本管理和自定义应用部署等操作。

## 文档导航

- 完整使用说明（终端用户）：[`用户手册.md`](./用户手册.md)
- 项目架构说明（开发/维护）：[`技术架构.md`](./技术架构.md)
- 需求与设计文档：[`需求文档.md`](./需求文档.md)、[`PRD.md`](./PRD.md)、[`UI_SPEC.md`](./UI_SPEC.md)

> 如果你是最终使用人员，或需要查看界面操作、批量安装、终端、自定义应用、脚本管理等说明，请直接阅读 [`用户手册.md`](./用户手册.md)。

---

## 核心能力概览

当前项目的核心能力包括：

- 主机管理：添加、编辑、删除、分组、连接测试
- 应用市场：安装、卸载、状态检测、配置入口
- 安装弹窗：公共安装配置窗口支持整体纵向滚动，便于查看长说明、离线资源信息与参数列表；JDK 已收敛为统一入口，应用市场安装时在弹窗中选择版本，独立的 JDK 上传入口则直接选择本地 JDK 文件夹并自动识别版本（当前统一 JDK 入口暂不支持批量安装，请使用单机安装流程）
- 批量操作：批量检测、批量安装（全局串行逐个执行）、批量卸载（主机级并发、单主机内应用串行）
- 任务与日志：任务进度、历史记录、日志查看；安装、检测、卸载与批量操作已接入统一操作管线，进度、日志和终态事件会缓冲后刷新以减少 UI 卡顿
- 性能优化：主窗口过滤集合和任务列表刷新已改为批量通知，批量状态检测增加主机级并发限流，自定义应用列表启用虚拟化，并降低高频列表项阴影效果以改善大量主机、应用和任务场景下的滚动与刷新流畅度
- 远程运维：SSH 终端、基于 SFTP 的远程文件管理；终端底部使用类 shell 提示符的内联命令行输入，不再显示独立输入框，终端输出区会按窗口可视尺寸同步本地文档宽度与远端 PTY 列数和行数，命令执行完成后会自动把光标恢复到命令行，输出追加采用增量计数以减少长日志场景下的 UI 卡顿
- 扩展能力：脚本管理、自定义应用部署
- Traefik：默认生成 `traefik.yml` 与 `dynamic.yml`，配置编辑器会按 YAML 文件提供结构化/文本双模式编辑，编辑区支持键值、树形和原始文本内容修改，并可使用鼠标滚轮滚动
- Mosquitto：完全离线安装、支持匿名访问或用户名密码认证、按系统与 CPU 架构匹配离线资源
- Linux 安装脚本：CentOS/RHEL 与 Ubuntu 优先使用 `install_centos.sh`、`install_ubuntu.sh` 系统专属入口，`install_linux.sh` 保留为兼容分发入口，便于后续按发行版维护安装逻辑

> 说明：
> 应用市场中的应用、版本和安装参数由当前程序配置驱动，实际以程序界面显示为准，不在 README 中维护固定清单。
> Nacos 已按当前需求从应用配置、脚本、UI 回退入口、数据库内置种子、测试和当前说明文档中移除，不再作为内置应用或待补离线资源目标。
> 批量安装会按队列全局串行执行，同一时间只安装一个“服务器 + 应用”任务，避免多台主机并行安装时争用 SSH、上传或本地离线资源；系统设置中的最大并发任务数不影响批量安装。
>
> 最近验证：2026-05-13 已完成全应用与主要 WPF UI 冒烟测试，后续修复复验后 `dotnet build` 0 警告、0 错误且完整测试 280 个通过；Nacos 移除后，剩余 24 个“主机 + 应用”真实链路组合当前为 15 个 PASS、0 个 FAIL、9 个 BLOCKED，CentOS 7 MySQL、Nginx、Elasticsearch 已完成真实安装、检测、卸载和残留复验；2026-06-02 已补齐 CentOS 7 Mosquitto、CentOS 7 MariaDB 与 Ubuntu 24 MariaDB 离线依赖，并完成安装、状态、卸载、状态复查冒烟；Nginx 已补充主 RPM 元数据校验、防火墙状态文件回收边界和跨发行版运行目录处理，Elasticsearch 已补充参数白名单与主包元数据校验；脚本管理、安装入口和配置入口已补充稳定 `AutomationProperties.AutomationId`，并由结构测试覆盖关键标识与静态重复检查；`SSH.NET` 已升级到 `2025.1.0` 且漏洞包检查清零；详细记录见 [`docs/full-smoke-validation-2026-05-13.md`](./docs/full-smoke-validation-2026-05-13.md)，操作管线专项记录见 [`docs/operation-validation-2026-05-13.md`](./docs/operation-validation-2026-05-13.md)。
>
> 当前 MQTT 相关能力已切换为 **Mosquitto**，要求使用离线资源安装；参数仅保留 `MQTT Port`、`Username`、`Password`，不再包含 WebSocket 端口；用户名与密码可同时留空以启用匿名访问，但不能只填写其中一项。
>
> 当前仓库内已落盘的 Mosquitto 离线资源版本为：Windows `2.0.21`、Ubuntu 22 `2.0.22`、Ubuntu 24 `2.1.2`、CentOS 7 `1.6.10`。安装弹窗会按目标系统优先选择对应版本。
>
> Mosquitto 的 Ubuntu 离线目录必须带齐主包和依赖包。当前脚本会在安装前检查 `mosquitto`、`libmosquitto1`、`libcjson1`，Ubuntu 22 还会检查 `libmicrohttpd12`，Ubuntu 24 还会检查 `libmicrohttpd12t64`；启用账号密码时还需要 `mosquitto-clients`。如果 `dpkg` 留下半安装状态，状态检测必须看到 `install ok installed` 才会判定为已安装。

---

## 运行与开发要求

- 操作系统：Windows 10/11 或 Windows Server
- SDK：.NET 10 SDK
- 运行时：需要可用的 `.NET 10 Windows Desktop Runtime`
- IDE：Visual Studio 2022（推荐）

当前主程序项目：

- `RemoteInstaller/RemoteInstaller.csproj`

目标框架：

- `net10.0-windows`

---

## 快速启动

### 还原依赖

```bash
dotnet restore
```

### 构建项目

```bash
dotnet build RemoteInstaller.sln
```

### 运行应用

如果你已经把 .NET 10 SDK 安装到系统默认位置，可以直接运行：

```bash
dotnet run --project RemoteInstaller/RemoteInstaller.csproj
```

如果你使用的是用户目录安装方式（例如本机当前会话使用 `C:/Users/WY/.dotnet-10`），请先设置 `DOTNET_ROOT`，否则 `RemoteInstaller.exe` 可能因为找不到 `Microsoft.WindowsDesktop.App` 而启动后立即退出：

```bash
DOTNET_ROOT="C:/Users/WY/.dotnet-10" "C:/Users/WY/.dotnet-10/dotnet.exe" run --project RemoteInstaller/RemoteInstaller.csproj
```

如果已经完成构建，也可以直接启动生成的 exe：

```bash
DOTNET_ROOT="C:/Users/WY/.dotnet-10" "RemoteInstaller/bin/Debug/net10.0-windows/RemoteInstaller.exe"
```

### 运行测试

```bash
dotnet test
```

### 发布与安装包生成

1. 先安装 Inno Setup 6，确保本机存在 `ISCC.exe`。
2. 在仓库根目录执行以下命令生成发布产物并打包安装程序：

```powershell
./build-installer.ps1
```

如需指定版本号或仅重新生成安装包，可使用：

```powershell
./build-installer.ps1 -Version 1.0.0
./build-installer.ps1 -SkipPublish
./build-installer.ps1 -IconPath "E:\公司文件\ZENDING 品牌化应用\zending.ico"
```

默认行为说明：

- 脚本会先执行面向 `win-x64` 的自包含发布：`dotnet publish RemoteInstaller/RemoteInstaller.csproj -c Release -r win-x64 --self-contained true`
- 打包默认图标使用仓库内 `RemoteInstaller/Assets/Brand/zending.ico`；该文件来源于 `E:\公司文件\ZENDING 品牌化应用\zending.ico`，脚本会在发布时同步覆盖应用 exe 图标，并传给 Inno Setup 作为安装包图标
- 安装器输入目录默认使用脚本显式指定的 publish 输出目录，当前默认仍为 `RemoteInstaller/bin/<Configuration>/net10.0-windows/publish`，并非 `dotnet publish -r` 的默认 RID 输出结构
- 安装包输出目录默认使用 `artifacts/installer`
- 安装内容会保留发布目录原始结构，包含 `Assets/`、`Scripts/` 和 `Scripts/app-configuration.json`
- 安装器默认安装到当前用户目录下的 `AppData/Local/Programs/RemoteInstaller`
- 用户数据目录位于 `AppData/Local/RemoteInstaller`，数据库、日志和缓存不会因重新安装而被安装目录覆盖
- 安装器界面默认使用仓库内置的简体中文语言资源，不依赖本机 Inno Setup 额外安装中文语言包
- 安装器会显示根目录 `LICENSE` 文件中的 MIT 许可证正文
- 如果自定义 `-InnoCompilerPath`、`-PublishDir` 或 `-OutputDir`，建议传绝对路径
- 如需改回依赖运行时的发布方式，可显式传入 `-SelfContained:$false`
- 使用 `-SkipPublish` 前，请先确认 `PublishDir` 中已经存在与当前参数匹配的最新发布产物

已知限制：

- 当前程序会将 SQLite 数据库写入 `%LocalAppData%\RemoteInstaller\data.db`。
- 程序日志位于 `%LocalAppData%\RemoteInstaller\Logs`，缓存默认目录位于 `%LocalAppData%\RemoteInstaller\Cache`。
- 新版本首次启动时，如果安装目录中存在旧版 `data.db`，会自动迁移到用户数据目录。
- 重新安装或覆盖安装不会替换 `%LocalAppData%\RemoteInstaller` 下的数据库内容。
- 当前安装包默认采用 self-contained 发布，目标机器通常无需预装 `.NET 10 Windows Desktop Runtime`。
- 项目内置旧图标文件仍保留在 `RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico`，打包流程默认改用 `RemoteInstaller/Assets/Brand/zending.ico`。
- 如需重新生成图标，可执行 `powershell.exe -ExecutionPolicy Bypass -File ".\\tools\\generate-app-icon.ps1"`。

### 使用 Visual Studio

1. 打开 `RemoteInstaller.sln`
2. 还原 NuGet 包
3. 按 F5 运行

---

## 仓库结构

```text
RemoteInstaller/        主程序（WPF 客户端）
RemoteInstaller.Tests/  测试项目
Scripts/                应用安装配置与脚本资源
用户手册.md             面向最终用户的使用说明
技术架构.md             项目架构说明
```

---

## 补充说明

- 客户端是 Windows 桌面程序。
- 目标主机可以是 Linux 或 Windows，但当前远程连接方式统一基于 SSH。
- 详细的界面说明、操作步骤、FAQ、日志位置等内容已收敛到 [`用户手册.md`](./用户手册.md)。
- 状态检测脚本的 `PORT` 字段只表示应用配置或默认端口，不再作为“正在运行”的证据；如需明确上报端口监听事实，应输出 `PORT_LISTENING:true|false`，运行态仍以 `RUNNING:true|false`、进程或 active 服务为准。若配置中的检测命令没有输出机器可读状态协议，程序会回退到内置检测逻辑，避免旧式命令被静默解析成未安装。
- Mosquitto 在 Debian/Ubuntu 上只把 dpkg 精确状态 `install ok installed` 作为完整安装证据，`unpacked`、`half-configured` 等依赖缺失后的残留状态不会再被当作安装成功。
- RabbitMQ 状态检测只认可 RabbitMQ 服务端二进制、`rabbitmq-server` 完整包、RabbitMQ 专属 Erlang 命令行进程、active 的 RabbitMQ 服务或 `rabbitmqctl status` 成功。单独的 Erlang 进程、默认端口被其他进程占用、`rabbitmqctl` 残留命令、inactive 服务定义和配置目录残留不会再被判定为已安装或运行中。
- 2026-06-02 在 CentOS 7 与 Ubuntu 24 测试机执行 Linux 脚本安装、状态、卸载、状态复查冒烟：Consul、Traefik、Redis、Nginx、RabbitMQ、Elasticsearch、MySQL 已完成可用性闭环；Ubuntu 上 Redis 与 Mosquitto 因基线已有安装/运行而跳过破坏性卸载；JDK 系列因仓库没有本地离线包未纳入真实安装冒烟。
- 2026-06-02 已修复同次冒烟的问题 2/3/4/5：`RemoteInstaller/Scripts/**/*.sh` 已统一为 LF 并由 `.gitattributes` 固化；CentOS 7 Mosquitto 已补入 `libwebsockets-3.0.1-2.el7` 与 `libuv-1.44.2-1.el7`；CentOS 7 MariaDB 已补入 `galera-4`、`Judy`、`libzstd`、`unixODBC`、`lsof`、`rsync`、`pv`、`python3`、Java、Perl 与 NSS/NSPR 等离线依赖；Ubuntu 24 MariaDB 已补入 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync`，并针对实际 Ubuntu 24.10 测试机补入 `libncurses6`、`libtinfo6`。
- 本轮复验结论：CentOS 7 Mosquitto `1.6.10` 安装、状态、卸载、卸载后状态均通过；CentOS 7 MariaDB `11.4.3` 安装、状态、卸载、卸载后状态均通过；Ubuntu 测试机实际为 Ubuntu 24.10，使用 `mariadb-ubuntu/24` 目录安装 MariaDB `11.4.7`，安装、状态、卸载、卸载后状态均通过。

---

## 许可证

MIT License
