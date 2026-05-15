# 操作管线性能优化与验证记录（2026-05-13）

## 范围

本次优化围绕安装、状态检测、卸载与批量操作的后台执行管线，目标是减少远程操作期间 UI 卡顿，并保持原有业务语义：

- 单机安装、单机检测、单机卸载统一通过 `OperationExecutor` 调度。
- 批量安装通过 `BatchOperationRunner.RunInstallQueueAsync` 保持全局串行。
- 批量卸载通过 `BatchOperationRunner.RunUninstallQueueAsync` 保持主机级并发、单主机内应用串行。
- UI 进度、日志与终态事件通过 `UiOperationEventBuffer` 合并后刷新，避免远程回调高频打断 UI 线程。
- `InstallerService` 对高频进度回调中的任务持久化做节流，完成、失败、取消等关键状态仍立即落库。
- `OperationRequest.Parameters` 已贯穿安装、状态检测和卸载，安装后验证、卸载执行和卸载后复检都会复用同一组参数；命令日志会对密码、Token、Key 等敏感参数做脱敏。

## 已完成验证

### 自动验证

- `dotnet build RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly`
  - 结果：通过，0 个错误。
- `dotnet test RemoteInstaller.Tests/RemoteInstaller.Tests.csproj --filter "OperationRequestResultTests|OperationExecutorTests|UiOperationEventBufferTests|BatchOperationRunnerTests|InstallerServicePersistenceThrottlingTests|MainViewModelOperationPipelineTests" --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly`
  - 结果：通过，24 个测试全部通过。
- `dotnet test RemoteInstaller.sln --nologo --verbosity quiet --consoleloggerparameters:ErrorsOnly`
  - 结果：通过，243 个测试全部通过。

### 静态与结构验证

- `MainViewModel` 中未发现 `Dispatcher.Invoke(` 残留。
- 批量卸载旧的 `_batchUninstallSemaphore` 字段和清理逻辑已移除，批量并发策略由 `BatchOperationRunner` 统一负责。
- `AddLog`、任务进度更新、任务日志追加、批量 helper 终态赋值均通过 UI Dispatcher 投递，避免后台并发路径直接修改 WPF 绑定集合或绑定属性。
- 操作事件中的任务 ID 绑定通过 `ConcurrentDictionary` 暂存，并在 UI 线程批量应用，避免后台回调直接改 `TaskViewModel`。
- 批量取消日志区分“已启动后取消”和“未启动”任务，避免统计口径误导。

### CentOS 7 真实主机验证

目标主机：CentOS 7，`192.168.60.152`。验证使用 `OperationExecutor -> InstallerService -> SSH/SFTP -> 远端脚本` 完整链路执行，没有把主机密码写入仓库文件、测试文件或验证文档。

#### Traefik 3.6.13

使用本地离线资源 `RemoteInstaller/Scripts/Traefik/traefik-centos7/traefik_v3.6.13_linux_amd64.tar.gz`，并使用非默认参数：

- `HTTP_PORT=18080`
- `HTTPS_PORT=18443`
- `DASHBOARD_PORT=18081`
- `INSTALL_DIR=/opt/traefik-validation`
- `CONFIG_DIR=/etc/traefik-validation`
- `ENABLE_DASHBOARD=true`

验证结果：

- 安装：`succeeded=True`，任务状态 `Completed`。
- 安装后检测：`installed=True`，`running=True`，版本 `3.6.13`。
- 卸载：`succeeded=True`，任务状态 `Completed`。
- 卸载后检测：`installed=False`，`running=False`，版本 `未知`。
- 远端残留检查：`/etc/traefik-validation` 与 `/opt/traefik-validation` 均不存在，结果 `CLEAN`。

本轮验证曾发现检测和卸载没有继承安装参数，导致自定义配置目录卸载后残留。修复后 `OperationRequest.Parameters` 会传入 `CheckStatusAsync` 和 `UninstallAsync`，安装后验证与卸载后复检也会继续使用同一组参数；Linux 脚本通过 `env` 注入非明文密码且符合 Bash 变量名规范的参数，命令日志会对密码、Token、Key 等敏感值脱敏，复测确认残留已清理。

#### Mosquitto 1.6.10

CentOS 7 Mosquitto 安装链路已进入真实远端安装阶段，当前阻塞原因是离线资源不完整：`RemoteInstaller/Scripts/Mosquitto/mosquitto-centos7` 目录只有主 RPM，目标机和离线目录均缺少 `libwebsockets.so.13()(64bit)` 对应依赖包。

已完成修复：`install_linux.sh` 在执行 `yum --disablerepo='*' -y localinstall` 前会预检 CentOS 7 RPM 的无版本依赖能力；RPM 不可读会明确报错，离线目录缺失依赖会输出缺失 capability 列表，版本化依赖仍交给 `yum --setopt=tsflags=test` 做事务校验，避免手写版本比较误报。

复测结果：安装前明确报 `libwebsockets.so.13()(64bit)` 缺失，`install=False`、`checkInstalled=False`、`checkRunning=False`，卸载清理返回成功且残留检查为 `CLEAN`。

结论：这是 CentOS 7 Mosquitto 离线资源包缺失，不是操作管线、SSH 上传或脚本调度失败。需要补齐 CentOS 7 Mosquitto 离线依赖后再执行完整安装、检测、卸载验证。

### Ubuntu 24 真实主机验证

目标主机：Ubuntu 24，`192.168.60.154`。使用用户新提供的 root 凭据后，验证已进入 `OperationExecutor -> InstallerService -> SSH/SFTP -> 远端脚本` 完整链路；验证程序通过进程环境变量读取密码，没有把主机密码写入仓库文件、测试文件或验证文档。

#### Traefik 3.6.13

使用本地离线资源 `RemoteInstaller/Scripts/Traefik/traefik-ubuntu/traefik_v3.6.13_linux_amd64.tar.gz`，并使用非默认参数：

- `HTTP_PORT=28080`
- `HTTPS_PORT=28443`
- `DASHBOARD_PORT=28081`
- `INSTALL_DIR=/opt/traefik-validation-ubuntu24`
- `CONFIG_DIR=/etc/traefik-validation-ubuntu24`
- `ENABLE_DASHBOARD=true`

验证结果：

- 安装：`succeeded=True`，任务状态 `Completed`。
- 安装后检测：`installed=True`，`running=True`，版本 `3.6.13`。
- 卸载：`succeeded=True`，任务状态 `Completed`。
- 卸载后检测：`installed=False`，`running=False`，版本 `未知`。
- 远端残留检查：`/etc/traefik-validation-ubuntu24` 与 `/opt/traefik-validation-ubuntu24` 均不存在，结果 `CLEAN`。

## 后续全量冒烟验证

本专项文档记录操作管线性能优化与早期真实主机验证。后续已在同日完成全应用与主要 WPF UI 冒烟测试，覆盖 13 个内置应用在 CentOS 7 与 Ubuntu 24 上的真实安装、检测、卸载和残留检查矩阵，并补充主窗口、常用弹窗、批量操作入口、终端、自定义应用和 JDK 上传等 UI 入口验证。

完整结论、应用矩阵、UI 冒烟矩阵、安全复查和后续建议见 [`full-smoke-validation-2026-05-13.md`](./full-smoke-validation-2026-05-13.md)。

## 后续建议

1. Ubuntu 24 MySQL 卸载残留问题已完成修复并通过全链路复验；继续关注后续全量冒烟报告中的资源阻塞项。
2. Ubuntu 24 MariaDB 已定位为严格离线资源不完整：离线目录缺少 `libconfig-inifiles-perl`、`libdbi-perl`、`lsof`、`rsync` 等必需 DEB 依赖；脚本已补充安装前预检、`nullglob` 包存在性判断和敏感输出脱敏，补齐资源后需重新执行完整安装、检测、卸载和残留验证。
3. 补齐 MariaDB CentOS 7、Mosquitto CentOS 7、JDK 8/11/17 所需离线资源后，重新执行对应阻塞项验证；Nacos 已按用户要求完全删除，不再作为待补资源项。
4. 为关键 WPF 控件补充稳定的 `AutomationProperties.AutomationId`，提升后续 UI 冒烟自动化稳定性。
