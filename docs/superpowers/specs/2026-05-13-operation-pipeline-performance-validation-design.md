# 操作管线统一重构与全量验证设计

日期：2026-05-13

## 背景

本轮目标来自两个诉求：

- 降低除单纯界面外各功能使用时的卡顿，尤其是远程操作、状态检测、日志刷新和任务列表刷新。
- 检查安装、检测、卸载是否能在 CentOS 7 与 Ubuntu 24 上正常完成，并覆盖当前应用市场中的 Linux 应用。

当前代码已经具备一部分架构基础：

- `Services/Operations/OperationDecisionPolicy.cs` 已集中安装和卸载结果判定。
- `Services/Operations/ScriptProtocolParser.cs` 已兼容 `PROGRESS:`、`STATUS:` 和 JSON 行协议。
- `MainViewModel` 已有任务列表刷新节流。
- `InstallProgressViewModel` 已有日志刷新节流。
- `MainWindow.xaml` 中服务器列表和任务列表已避免外层 `ScrollViewer` 破坏虚拟化。

但操作生命周期仍分散在 `MainViewModel` 与 `InstallerService` 中。单机安装、批量安装、单机卸载、批量卸载、检测分别创建任务、创建进度回调、创建日志回调、刷新状态和统计结果，导致重复逻辑多，且部分路径仍存在同步 UI 调用和高频数据库写入风险。

用户已确认采用方案 B：重构安装、检测、卸载的共享操作管线，并允许批量安装、批量卸载一并纳入重构，但应保持现有用户可见语义。

## 目标

重构后应满足：

- 安装、检测、卸载通过统一操作请求进入执行管线。
- 单机和批量操作共用任务、进度、日志、结果处理模型。
- UI 层只负责用户交互和状态展示，不直接散落远程操作生命周期细节。
- 高频日志、进度、状态刷新统一节流，减少 UI 主线程卡顿。
- 数据库任务保存从逐条高频写入调整为关键节点写入和节流写入。
- 批量安装保持全局串行语义。
- 批量卸载保持按设置并发主机、单主机内应用顺序执行语义。
- 批量检测增加并发上限，避免 SSH 和 UI 刷新尖峰。
- CentOS 7 与 Ubuntu 24 上逐项验证安装、检测、卸载链路。

## 非目标

本轮不做以下事情：

- 不更换 WPF、MVVM Toolkit、SSH.NET、SQLite 等基础技术栈。
- 不引入新的外部依赖。
- 不重写所有 Bash 或 PowerShell 脚本。
- 不改变现有脚本协议，继续兼容 `PROGRESS:阶段:百分比`、`STATUS:*`、JSON status/progress/result 行。
- 不改变应用市场入口名称和主要操作流程。
- 不把远程主机密码写入日志、文档或测试输出。
- 不做破坏性清理，例如强制删除非本应用创建的数据目录，除非现有脚本已有明确卸载逻辑。

## 推荐架构

采用 Operations 层主导的统一执行管线：

```text
MainViewModel / Dialogs
  -> OperationRequest
  -> OperationExecutor
  -> InstallerService / SshService / ScriptResolver / AppHandler
  -> OperationEvent stream
  -> UiOperationEventBuffer
  -> TaskViewModel / ApplicationCardViewModel / Logs
```

批量操作额外通过 `BatchOperationRunner` 管理队列、并发和统计：

```text
Batch command
  -> BatchOperationRunner
  -> OperationRequest[]
  -> OperationExecutor per item
  -> BatchOperationSummary
```

### MainViewModel

`MainViewModel` 只负责 UI 编排：

- 收集用户选择的主机、应用、参数和安装包路径。
- 弹出安装配置、卸载确认、错误提示。
- 构建 `OperationRequest`。
- 订阅统一操作事件并更新界面。
- 触发状态刷新和任务详情展示。

它不应再在多个入口中重复创建 `InstallerService`、`TaskViewModel`、`Progress<InstallTask>`、日志回调和批量统计逻辑。

### OperationRequest

统一操作请求模型：

```csharp
public sealed class OperationRequest
{
    public required OperationType Type { get; init; }
    public required RemoteHost Host { get; init; }
    public required ApplicationInfo Application { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
    public string? LocalPackagePath { get; init; }
    public bool KeepData { get; init; }
    public bool IsBatch { get; init; }
}
```

`OperationType` 包含：

- `Install`
- `CheckStatus`
- `Uninstall`

### OperationResult

统一操作结果模型：

```csharp
public sealed class OperationResult
{
    public required OperationType Type { get; init; }
    public required RemoteHost Host { get; init; }
    public required ApplicationInfo Application { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public ApplicationStatus? Status { get; init; }
    public Models.TaskStatus TaskStatus { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool HasWarning { get; init; }
}
```

### OperationEvent

统一操作事件模型：

```csharp
public enum OperationEventKind
{
    Progress,
    Log,
    StatusChanged,
    Completed,
    Failed,
    Canceled
}
```

事件中包含任务 ID、应用、主机、进度、日志和状态快照。后台操作只发布事件，UI 层统一节流消费。

### OperationExecutor

`OperationExecutor` 负责完整操作生命周期：

- 创建 `InstallTask`。
- 初始化 `LogCollector`。
- 连接远程主机。
- 执行安装、检测或卸载。
- 解析脚本协议事件。
- 调用 `OperationDecisionPolicy` 判定最终结果。
- 保存任务和日志。
- 发布进度、日志、完成、失败、取消事件。

第一阶段可以复用 `InstallerService.InstallAsync`、`InstallerService.UninstallAsync`、`InstallerService.CheckStatusAsync`，先把 UI 调用收敛到统一入口；第二阶段再逐步把任务保存、日志收集、远程工作目录管理、状态验证等生命周期细节迁入 Operations 层。

### InstallerService

`InstallerService` 在迁移期间保留底层能力：

- SSH 连接。
- SFTP 上传。
- 脚本定位。
- 脚本执行。
- 参数转义。
- 状态检测。

最终目标是减少 `InstallerService` 中“大而全”的高层流程代码，让它更多承担底层远程能力和兼容旧调用的适配职责。

### BatchOperationRunner

`BatchOperationRunner` 负责批量策略：

- 批量安装：全局串行，按主机和应用顺序逐个执行。
- 批量检测：使用并发上限，避免一次性刷新所有主机和应用。
- 批量卸载：按设置并发主机，单主机内应用顺序执行。
- 单个任务失败不阻断后续任务。
- 汇总成功、失败、取消数量，返回 `BatchOperationSummary`。

## 数据流

### 单机安装

```text
InstallApplication command
  -> show install config dialog
  -> build OperationRequest(Install)
  -> OperationExecutor.ExecuteAsync
  -> operation events
  -> UI event buffer
  -> update task/log/app status
```

### 单机检测

```text
CheckApplicationStatus command
  -> build OperationRequest(CheckStatus)
  -> OperationExecutor.ExecuteAsync
  -> final ApplicationStatus
  -> update selected ApplicationCardViewModel
```

### 单机卸载

```text
UninstallApplication command
  -> show confirmation
  -> resolve installed ApplicationInfo
  -> build OperationRequest(Uninstall)
  -> OperationExecutor.ExecuteAsync
  -> refresh mutated application status
```

### 批量安装

```text
BatchInstall command
  -> selected hosts and selected apps
  -> build OperationRequest list
  -> BatchOperationRunner.RunInstallQueueAsync
  -> execute one item at a time
  -> summarize results
  -> refresh selected host status
```

### 批量检测

```text
BatchCheckStatus command
  -> selected hosts
  -> build status refresh requests
  -> BatchOperationRunner.RunStatusQueueAsync
  -> limited concurrency
  -> publish status updates through UI buffer
```

### 批量卸载

```text
BatchUninstall command
  -> confirmation
  -> selected hosts and selected apps
  -> BatchOperationRunner.RunUninstallQueueAsync
  -> host-level concurrency from setting
  -> app-level serial execution per host
  -> summarize results
  -> refresh selected host status
```

## UI 防卡顿设计

### 统一事件缓冲

后台操作不直接频繁更新 `ObservableCollection`。新增或整理 `UiOperationEventBuffer`：

- 接收 `OperationEvent`。
- 合并同一个任务的多次进度更新，只保留最后一次。
- 批量追加日志。
- 批量裁剪超过上限的日志。
- 完成、失败、取消事件优先刷新。
- 通过 `Dispatcher.InvokeAsync` 使用 `DispatcherPriority.Background` 投递 UI 更新。

### 任务列表刷新

保留并强化现有任务刷新节流：

- 日志追加不立即重排任务。
- 进度更新只更新对应任务字段。
- 任务完成、失败、取消时立即刷新一次。
- 普通高频更新按节流周期刷新。

### 安装详情日志刷新

保留 `InstallProgressViewModel` 中已有的增量日志刷新思路：

- 不在每条日志到来时重建整个日志集合。
- 默认只追加新增项和裁剪头部超限项。
- 只有遇到无法增量表达的集合变化时才完整重载。

### 消除同步 UI 调用

远程操作回调中不得使用同步 `Dispatcher.Invoke`。统一改为：

```csharp
dispatcher.InvokeAsync(action, DispatcherPriority.Background);
```

重点迁移卸载进度回调中同步 Dispatcher 调用，使单机卸载和单机安装走同一 `UpdateTaskProgressState` 路径。

### 数据库写入节流

任务保存策略调整为：

- 任务开始立即保存。
- 阶段变化保存。
- 完成、失败、取消立即保存。
- 高频进度只更新内存状态，由节流器周期保存最后状态。
- 任务日志在结束时完整保存，必要时可增加批量落库而不是逐条落库。

## 错误处理

### 用户输入错误

未选择主机、未选择应用、安装参数缺失、本地包路径不存在等，在进入 `OperationExecutor` 前由 UI 层拦截，不创建远程任务。

### 远程连接错误

SSH 连接失败、认证失败、SFTP 不可用统一转换为失败结果。日志记录主机、端口、用户名和失败原因，不记录密码。

### 脚本执行错误

脚本非 0 退出不立即等价于安装或卸载失败：

- 安装后继续做状态验证。
- 卸载后继续做状态验证。
- 最终由 `OperationDecisionPolicy` 结合脚本结果和状态证据判定。

### 状态检测错误

- 检测脚本缺失时回退内置检测。
- 检测脚本输出不可读时记录 warning 并回退。
- 安装或卸载后的验证失败时，结合脚本输出和残留证据决定结果。

### 取消操作

所有操作通过 `CancellationToken` 传递取消。已开始的远程脚本不做强杀式破坏操作，只停止本地等待和后续队列。UI 显示已取消，任务日志保留。

## 实机验证设计

### 验证主机

- CentOS 7：`192.168.60.152`
- Ubuntu 24：`192.168.60.154`
- 用户：`root`

密码不写入设计文档、日志或测试输出。

### 验证范围

以程序运行时加载到应用市场的 Linux 支持应用为准。当前配置至少包括：

- MySQL
- MariaDB
- Redis
- Nginx
- Elasticsearch
- RabbitMQ
- Mosquitto
- Consul

如运行时还加载 JDK、Traefik 或其他 Linux 应用，也纳入逐项验证清单。

### 单应用验证顺序

每个应用、每台主机按固定顺序执行：

```text
前置检测
  -> 安装
  -> 安装后检测
  -> 卸载
  -> 卸载后检测
```

每一步记录：

- 操作类型。
- 主机系统。
- 应用 ID、名称、版本。
- 是否成功。
- 任务 ID。
- 关键日志。
- 最终 `ApplicationStatus`。
- 失败原因。
- 是否存在残留。

### 成功标准

安装成功必须满足：

- 操作结果为 completed。
- 检测状态 `IsInstalled = true`。
- 服务型应用尽量要求 `IsRunning = true`。
- 端口类应用检查默认端口是否监听。
- UI 任务状态显示完成，进度不再卡在中间阶段。
- 日志能正常滚动与保存，不造成明显卡顿。

检测成功必须满足：

- 不抛异常。
- 已安装应用能识别已安装、版本、运行状态。
- 未安装应用能识别未安装。
- 脚本输出机器可读协议时优先使用协议结果。
- 脚本协议缺失时走内置回退检测并输出明确日志。

卸载成功必须满足：

- 操作结果为 completed 或 completed with warning。
- `IsRunning = false`。
- 不存在运行时证据：进程、端口监听、active service。
- 仅配置或 service 残留标为 warning，不误判为失败。
- UI 状态从已安装刷新为未安装或残留提示。

### 失败处理

全量验证采用逐项隔离：

- 单个应用失败后记录失败，继续下一个应用。
- 如果安装失败，仍执行卸载或清理尝试，避免污染后续应用。
- 如果卸载失败，保留远程工作目录与日志，不做破坏性清理。
- 同一主机 SSH 连接异常时，只跳过当前主机剩余项，另一台主机继续。

### 环境影响

全部逐项验证会真实安装和卸载多种中间件，可能影响：

- 默认端口：`3306`、`6379`、`80`、`9200`、`5672`、`15672`、`1883`、`8500` 等。
- 系统包：`rpm`、`yum`、`apt` 安装状态。
- systemd 服务。
- `/opt`、`/etc`、`/var/lib`、`/tmp/remote_install` 等目录。

执行前应做前置状态快照，执行后做卸载后检测与残留记录。

## 测试策略

### 单元和结构测试

新增或调整测试覆盖：

- `OperationExecutor`：安装成功、安装失败、脚本失败但状态成功、取消。
- `OperationExecutor`：卸载成功、卸载残留 warning、卸载后仍运行失败。
- `OperationExecutor`：检测脚本缺失时回退。
- `BatchOperationRunner`：批量安装保持全局串行。
- `BatchOperationRunner`：批量卸载保持主机并发、主机内应用串行。
- `BatchOperationRunner`：批量检测受并发上限控制。
- `BatchOperationRunner`：失败项不阻断后续项。
- UI 防卡顿：远程操作进度回调里不出现 `Dispatcher.Invoke`。
- UI 防卡顿：日志追加走批量刷新，不逐条重建集合。
- UI 防卡顿：任务排序走节流刷新，不每条日志都重排。
- UI 防卡顿：大列表不被外层 `ScrollViewer` 包裹，保留虚拟化。

### 构建与自动测试

执行：

```powershell
dotnet build RemoteInstaller.sln
dotnet test
```

如果测试暴露已有脚本或环境依赖问题，应区分单元测试问题、代码回归和实机环境问题。

### 手工和实机测试

执行：

```text
批量检测基线
  -> 每个应用逐项：检测 -> 安装 -> 检测 -> 卸载 -> 检测
  -> 批量检测收尾
```

验证应通过应用自身路径完成。必要时可以辅以 SSH 命令做外部确认，但外部确认只作为证据，不替代应用内验证。

## 分阶段实施

### 阶段 1：建立统一模型与执行器

- 新增 `OperationRequest`、`OperationResult`、`OperationEvent`。
- 新增 `OperationExecutor`。
- 初期复用 `InstallerService` 的安装、检测、卸载方法。
- 添加基础行为测试。

### 阶段 2：收敛 UI 调用

- 单机安装改走统一请求。
- 单机检测改走统一请求。
- 单机卸载改走统一请求。
- 替换同步 `Dispatcher.Invoke`。
- 统一任务、日志、状态刷新路径。

### 阶段 3：收敛批量操作

- 新增 `BatchOperationRunner`。
- 批量安装通过统一请求执行，并保持全局串行。
- 批量检测通过统一请求执行，并加入并发上限。
- 批量卸载通过统一请求执行，并保持现有并发语义。
- 添加批量语义保护测试。

### 阶段 4：迁移底层生命周期细节

- 将任务保存、日志收集、状态验证、远程工作目录管理逐步收敛到 Operations 层。
- 删除确认已无调用的重复代码。
- 补齐 README 或用户手册中的性能优化与验证说明。

## 完成标准

本轮完成时应满足：

- 构建通过。
- 自动测试通过，或有明确不可运行原因。
- 单机安装、检测、卸载能在 CentOS 7 和 Ubuntu 24 上逐项完成验证。
- 批量检测可完成且 UI 不明显卡顿。
- 批量安装和批量卸载在重构后语义保持一致。
- README 或用户手册记录本次优化与验证结果。
- 个别应用如果受系统源、离线包或资源限制失败，明确记录为环境或资源问题，不静默跳过。
