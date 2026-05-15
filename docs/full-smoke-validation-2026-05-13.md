# 全应用与全 UI 冒烟测试报告（2026-05-13）

## 结论摘要

- 总体结论：**PARTIAL PASS**。
- 自动构建测试：`dotnet build RemoteInstaller.sln` 通过，0 个错误；`dotnet test RemoteInstaller.sln` 通过，243 个测试全部通过。
- 真实主机应用矩阵：Nacos 移除后共保留 24 个“主机 + 应用”组合，当前修复复验后结果为 15 个 `PASS`、0 个 `FAIL`、9 个 `BLOCKED`。
- WPF UI 冒烟：主窗口、主机管理弹窗、设置、历史记录、日志查看、批量检测、批量安装、批量卸载、终端、自定义应用、JDK 上传等核心入口可打开；脚本管理、安装入口和配置入口已补充稳定 `AutomationProperties.AutomationId`，并由结构测试覆盖关键标识与静态重复检查。
- 凭据处理：本轮验证未将主机密码、应用密码、Token、私钥或敏感命令写入报告、README、项目本地设置或本轮应用日志。

## 环境

| 项目 | 值 |
|---|---|
| 本地系统 | Windows 10 Pro for Workstations 10.0.19045 |
| 主程序 | `RemoteInstaller/RemoteInstaller.csproj` |
| 目标框架 | `net10.0-windows` |
| 验证链路 | `OperationExecutor -> InstallerService -> SshService/SFTP -> 远端脚本` |
| 结果文件 | `C:\Users\WY\AppData\Local\Temp\remoteinstaller-validation\full-app-smoke-results.json` |

### 真实主机

| 主机 | IP | 系统信息 | 资源摘要 |
|---|---|---|---|
| CentOS 7 | `192.168.60.152` | CentOS Linux 7 (Core)，Linux 3.10.0-1160.el7.x86_64 | systemd running；根分区约 50G，约 49G 可用；内存约 7.8G，约 6.9G 可用 |
| Ubuntu 24 | `192.168.60.154` | 远端报告为 Ubuntu 24.10，Linux 6.11.0-8-generic | systemd running；根分区约 96G，约 77G 可用；内存约 7.2Gi，约 1.2Gi 可用 |

凭据只通过本地加密数据库、进程内对象或验证程序运行时参数传递；报告不记录任何明文密码。

## 自动构建与测试

| 项目 | 命令 | 结果 | 说明 |
|---|---|---|---|
| 构建 | `dotnet build RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` | PASS | 0 个警告；0 个错误 |
| 完整测试 | `dotnet test RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` | PASS | 修复复验后 274 个测试全部通过 |
| 审查修复回归 | `dotnet test RemoteInstaller.Tests\\RemoteInstaller.Tests.csproj --filter "OperationExecutorTests|InstallerServicePersistenceThrottlingTests" --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` | PASS | 8 个受影响测试全部通过 |
| Nginx / Elasticsearch 安全收口回归 | `dotnet test RemoteInstaller.Tests/RemoteInstaller.Tests.csproj --filter "ElasticsearchStatusTests|NginxOfflineInstallScriptTests|LinuxServiceResidueScriptTests" --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` | PASS | 参数白名单、离线包元数据校验、防火墙/SELinux 状态文件边界和卸载残留相关回归通过 |

当前已将 `SSH.NET` 升级到 `2025.1.0`，并移除 `System.Text.Json`、`System.Security.Cryptography.ProtectedData`、`System.Text.Encoding.CodePages` 等由目标框架提供 API 的冗余显式包引用；`dotnet list package --vulnerable --include-transitive` 已无漏洞包，构建恢复为 0 个警告、0 个错误。

## 应用矩阵

| 应用 | CentOS 7 | Ubuntu 24 | 说明 |
|---|---|---|---|
| MySQL | PASS | PASS | CentOS 7 已修复离线 RPM 事务、非默认端口与自定义数据目录 SELinux 处理，版本 `8.0.44`，安装、检测、卸载和残留检查均通过；Ubuntu 24 已修复自定义数据目录与 AppArmor 配置，安装、检测、卸载和残留检查均通过 |
| MariaDB | BLOCKED | BLOCKED | CentOS 7 缺少本地 MariaDB 离线包；Ubuntu 24 脚本已补充严格离线 Debian 依赖预检，当前明确阻塞于离线目录缺少 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync` 等 Ubuntu 24 DEB 依赖包，卸载清理后残留为 `CLEAN` |
| Redis | PASS | PASS | CentOS 7 版本 `6.2.14`，Ubuntu 24 版本 `7.0.15`；安装、检测、卸载、残留检查均通过 |
| Nginx | PASS | PASS | CentOS 7 已修复离线 RPM 依赖选择、主 RPM 元数据校验、SELinux 非默认端口授权、systemd PIDFile 配置和防火墙状态文件卸载边界，版本 `1.24.0`，安装、检测、卸载和残留检查均通过；Ubuntu 24 版本 `1.24.0` 全链路通过 |
| Elasticsearch | PASS | PASS | CentOS 7 已修复 RPM 版本化依赖静态预检误判，并补充 HTTP 端口、集群名、节点名、JVM 内存白名单和 RPM/DEB 主包元数据校验，版本 `8.5.3`，安装、检测、卸载和残留检查均通过；Ubuntu 24 版本 `8.5.3` 全链路通过 |
| RabbitMQ | PASS | PASS | CentOS 7 版本 `3.9.16`，Ubuntu 24 版本 `3.12.0`；安装、检测、卸载、残留检查均通过 |
| Mosquitto | BLOCKED | PASS | CentOS 7 脚本已补充离线 RPM 依赖预检，当前明确阻塞于缺少 `libwebsockets.so.13()(64bit)` 对应 EL7 RPM；卸载清理后残留为 `CLEAN`；Ubuntu 24 版本 `2.1.2` 全链路通过 |
| Consul | PASS | PASS | 两台主机均验证版本 `1.22.6`，安装、检测、卸载、残留检查均通过 |
| Traefik | PASS | PASS | 两台主机均验证版本 `3.6.13`，安装、检测、卸载、残留检查均通过 |
| JDK 8 | BLOCKED | BLOCKED | 缺少本地 JDK 8 包或目录 |
| JDK 11 | BLOCKED | BLOCKED | 缺少本地 JDK 11 包或目录 |
| JDK 17 | BLOCKED | BLOCKED | 缺少本地 JDK 17 包或目录 |

### 应用结果统计

| 主机 | PASS | FAIL | BLOCKED |
|---|---:|---:|---:|
| CentOS 7 | 7 | 0 | 5 |
| Ubuntu 24 | 8 | 0 | 4 |
| 合计 | 15 | 0 | 9 |

## UI 冒烟矩阵

| UI 流程 | 结果 | 证据 | 备注 |
|---|---|---|---|
| 主窗口启动 | PASS | 找到窗口标题 `中鼎智能-软件研发-Remote Installer Console` | 程序可启动，无启动崩溃 |
| 主导航与工具栏 | PASS | 可见 `添加服务器`、`批量检测`、`批量安装`、`批量卸载`、`终端`、`自定义应用`、`JDK 上传`、`历史记录`、`设置`、`日志` | 入口按钮可被 UI Automation 发现 |
| 应用市场 Tab | PASS | `应用市场` Tab 可选择 | 应用列表和安装入口可见 |
| 自定义应用 Tab | PASS | `自定义应用` Tab 可选择 | 自定义应用入口可达 |
| 脚本管理 Tab | PASS | Tab 可通过鼠标点击；关键控件已补充 `AutomationProperties.AutomationId` | 脚本目录、文件列表、编辑区、上传和保存入口具备稳定自动化锚点 |
| 设置弹窗 | PASS | 点击 `设置` 后出现 `系统设置` 内容 | 取消/关闭路径可恢复 |
| 历史记录弹窗 | PASS | 点击 `历史记录` 后出现 `安装/卸载历史记录` 内容 | 筛选控件可见 |
| 日志查看器 | PASS | 点击 `日志` 后出现 `应用日志` 内容 | 刷新、打开文件夹、清除日志、关闭入口可见 |
| 添加服务器弹窗 | PASS | 点击 `添加服务器` 后出现 `添加远程主机` 内容 | 主机名称、地址、端口、用户名、认证方式等控件可见 |
| 批量检测入口 | PASS | 点击 `批量检测` 后出现批量相关内容 | 未执行破坏性批量操作，只验证入口与弹窗 |
| 批量安装入口 | PASS | 点击 `批量安装` 后出现批量相关内容 | 未再次执行真实批量安装，避免重复改动远端状态 |
| 批量卸载入口 | PASS | 点击 `批量卸载` 后出现批量相关内容 | 未再次执行真实批量卸载，避免重复改动远端状态 |
| 终端入口 | PASS | 点击 `终端` 后出现终端/连接相关内容 | 未执行交互式长会话 |
| 自定义应用入口 | PASS | 点击 `自定义应用` 后出现自定义应用相关内容 | 验证打开与关闭路径 |
| JDK 上传入口 | PASS | 点击 `JDK 上传` 后出现 JDK/上传相关内容 | 未提供本地 JDK 包，因此只验证入口 |
| 单应用安装入口 | PASS | `安装` 按钮使用按应用 ID 生成的稳定 `AutomationId`；安装配置弹窗关键控件已补标识 | 真实安装链路已由应用矩阵覆盖，本轮补齐自动化定位锚点 |
| 配置入口 | PASS | 配置相关按钮使用按应用 ID 生成的稳定 `AutomationId`；配置编辑器关键控件已补标识 | Traefik 与 Elasticsearch 配置编辑器入口具备稳定自动化定位锚点 |

## 缺陷与阻塞项

### 已修复项

1. **WPF 脚本管理、安装弹窗、配置编辑器自动化佐证不足**
   已为脚本管理、应用安装按钮、配置按钮、安装配置弹窗和配置编辑器关键控件补充稳定 `AutomationProperties.AutomationId`，并新增结构测试覆盖关键标识与静态重复检查。

### 前置资源阻塞项

1. **MariaDB CentOS 7**：缺少本地 CentOS 7 离线包。
2. **MariaDB Ubuntu 24**：离线目录缺少 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync` 等严格离线安装必需的 Ubuntu 24 DEB 依赖包；脚本已能在安装前明确报缺失依赖，卸载清理后残留为 `CLEAN`。
3. **Mosquitto CentOS 7**：离线目录缺少提供 `libwebsockets.so.13()(64bit)` 的 EL7 RPM；脚本已能在安装前明确报缺失 capability。
4. **JDK 8 / 11 / 17**：缺少本地 JDK 包或目录。

### 已移除项

- **Nacos**：用户已确认不再需要，已从应用配置、脚本、UI 回退入口、数据库内置种子、测试和当前说明文档中移除，不再作为内置应用或待补资源项。

## 安全复查

| 检查项 | 结果 | 说明 |
|---|---|---|
| README / docs / 项目本地设置明文凭据扫描 | PASS | 未发现本轮主机凭据明文命中 |
| 本轮应用日志明文凭据扫描 | PASS | `20260513` 日志未发现本轮主机凭据明文 |
| 本轮应用日志未脱敏敏感字段扫描 | PASS | 未发现未脱敏的 `PASSWORD`、`ROOT_PASSWORD`、`TOKEN`、`SECRET`、`PRIVATE_KEY`、`ACCESS_KEY` 形式字段 |
| 历史日志快速扫描 | WARN | 历史日志存在敏感字段样式命中，需要单独区分旧日志与本轮验证日志进行治理 |
| 报告内容 | PASS | 不包含主机密码、应用密码、Token、私钥或敏感命令 |

## 修复复验补充（2026-05-14）

- CentOS 7 Nginx 已补充主 RPM 真实包名校验，避免仅凭文件名安装离线 RPM；防火墙规则只在本次新增成功后写入 `/var/lib/remote-installer/nginx/firewall-port-*.state`，卸载只按可信状态文件回收，不再无状态删除 firewalld service、ufw profile 或 iptables 规则；运行目录兼容 `nginx`、`www-data` 或 `root`，并拒绝不可信 `/run/nginx` 符号链接和错误权限。真实复验结果：安装、检测、卸载、卸载后检测和残留检查均 `PASS`。
- CentOS 7 Elasticsearch 已补充 `HTTP_PORT`、`CLUSTER_NAME`、`NODE_NAME`、`MEMORY_LIMIT` 白名单校验，DEB/RPM 主包元数据校验，以及 tar fallback 的不可预测临时目录。真实复验结果：安装、检测、卸载、卸载后检测和残留检查均 `PASS`。
- Ubuntu 24 MariaDB 已补充严格离线 Debian 依赖预检、`nullglob` 下的本地包存在性判断和 `--no-install-recommends` 安装约束，并脱敏安装完成输出中的 root 密码字段。真实复验结果：安装前明确阻塞于缺少 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync` 等 Ubuntu 24 DEB 依赖，卸载后检测和残留检查均为 `CLEAN`。
- 本轮安全收口后验证：`bash -n` 通过；Nginx 相关回归 51 个通过；Nginx/Elasticsearch/残留相关回归 58 个通过；MariaDB 相关回归 49 个通过；`dotnet build RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 0 个错误；`dotnet test RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 274 个测试全部通过。
- WPF 自动化佐证补强后验证：`dotnet test RemoteInstaller.Tests\\RemoteInstaller.Tests.csproj --filter "WpfAutomationIdCoverageTests" --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 6 个测试通过；`dotnet build RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 0 个错误。
- 依赖漏洞与包引用治理后验证：`SSH.NET` 已升级到 `2025.1.0`，`dotnet list RemoteInstaller\\RemoteInstaller.csproj package --vulnerable --include-transitive` 无漏洞包；`dotnet build RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 0 个警告、0 个错误；`dotnet test RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly` 280 个测试全部通过。

## 后续建议

1. 补齐 MariaDB Ubuntu 24 缺失的 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync` 等 DEB 依赖包后，重新执行 MariaDB 完整安装、检测、卸载和残留验证。
2. 补齐 Mosquitto CentOS 7 缺失的 `libwebsockets.so.13()(64bit)` 对应 EL7 RPM 后，重新执行 Mosquitto 完整安装、检测、卸载和残留验证。
3. 补齐 MariaDB CentOS 7、JDK 8/11/17 的离线资源后，再重新执行对应 `BLOCKED` 项。Nacos 已按用户要求完全删除，不再作为待补资源项。
4. 后续 UI 冒烟可优先复用已补充的 `AutomationProperties.AutomationId`，把脚本管理、安装配置弹窗和配置编辑器纳入可重复自动化测试。
5. 后续如继续升级其它小版本依赖，应单独验证 WPF 样式、SQLite 数据访问和依赖注入启动链路。
