# 安装、检测、卸载架构重构设计

日期：2026-05-08

## 背景

当前远程安装链路已经能支撑 MySQL、Redis、Elasticsearch、RabbitMQ、Nacos、Nginx 等应用，但安装、检测、卸载的核心逻辑集中在 `InstallerService` 中。该服务同时承担远程命令执行、脚本定位、参数转义、脚本输出解析、状态归一化、日志收集、安装验证、卸载验证和应用特例处理。

这导致几个反复出现的问题：

- 应用脚本存在，但配置或 UI 列表未覆盖时，全量状态刷新不会检查该应用。
- 脚本退出码和真实运行状态没有统一仲裁，可能出现应用已安装但任务显示失败。
- 检测脚本各自判断二进制、包、服务、进程、端口和残留状态，规则难以保持一致。
- ViewModel 混入离线资源选择、JDK 聚合和应用特例，UI 层承担了过多部署细节。
- 测试大量依赖源码字符串断言，说明核心行为还不够可注入、可隔离。

## 目标

重构目标是保留现有脚本资产，先把 C# 编排层边界拆清楚，降低后续新增应用和修复误判的成本。

重构后应满足：

- 安装、检测、卸载统一通过结构化命令结果和结构化脚本事件判断。
- 最终任务结果由脚本结果、状态探测结果和策略共同决定。
- 应用定义尽量收敛到一个事实来源，避免配置、UI、硬编码兜底多处漂移。
- 应用特例有明确扩展点，不再散落在通用服务和 ViewModel 中。
- 状态检测能表达“已安装”“运行中”“已停止”“仅配置残留”“仅服务残留”“部分卸载”等更细状态。
- 关键链路可以用 fake remote runner 做行为测试，减少源码字符串断言。

## 非目标

本轮不重写所有 Bash/PowerShell 脚本，不改变用户现有安装包目录布局，也不一次性移除所有硬编码兜底。脚本协议会逐步升级，先兼容现有 `PROGRESS:`、`INSTALLED:`、`RUNNING:` 文本输出。

## 推荐架构

采用中等重构，把现有 `InstallerService` 拆成若干小服务：

```text
UI / ViewModel
  -> ApplicationOperationService
    -> OperationPlanner
    -> PackageResolver
    -> ScriptResolver
    -> RemoteCommandRunner
    -> ScriptProtocolParser
    -> StatusProbeEngine
    -> IAppHandler per app
```

### ApplicationOperationService

负责安装、检测、卸载的高层编排。它不直接拼远程命令，不直接读脚本文件，也不直接解析脚本输出。

主要职责：

- 创建任务和日志上下文。
- 调用 planner 生成操作计划。
- 执行脚本和状态探测。
- 根据策略决定任务最终结果。
- 把结果保存到数据库并上报给 UI。

### RemoteCommandRunner

统一远程命令执行结果，替代“返回字符串或直接 throw”的模式。

返回模型：

```csharp
public sealed class RemoteCommandResult
{
    public string Command { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; }
    public string Stderr { get; init; }
    public bool TimedOut { get; init; }
    public TimeSpan Duration { get; init; }
}
```

异常只表示连接断开、认证失败、取消、无法创建命令等基础设施失败。脚本自身非 0 退出应作为 `ExitCode` 返回，由上层策略判断。

### ScriptProtocolParser

统一解析脚本事件。第一阶段兼容现有文本协议：

- `PROGRESS:Stage:Percent`
- `INSTALLED:true`
- `RUNNING:true`
- `VERSION:...`
- `PORT:...`
- `STAGE:SUCCESS`
- `SERVICE_ONLY_STALE:true`

第二阶段建议升级为 JSONL：

```json
{"type":"progress","stage":"Installing","percent":40}
{"type":"status","installed":true,"running":true,"version":"7.2.3","port":"6379"}
{"type":"result","stage":"success"}
```

解析器输出统一事件模型，供日志、进度、状态和结果仲裁复用。

### StatusProbeEngine

状态探测不应只输出两个布尔值，而应输出证据模型：

```csharp
public sealed class ApplicationStatusEvidence
{
    public bool BinaryFound { get; init; }
    public bool PackageFound { get; init; }
    public bool ServiceFound { get; init; }
    public bool ServiceActive { get; init; }
    public bool ProcessFound { get; init; }
    public bool PortListening { get; init; }
    public bool ConfigOnlyResidue { get; init; }
    public bool ServiceOnlyResidue { get; init; }
}
```

平台层用统一规则归一化状态：

- 进程、端口或 active 服务存在时，视为已安装并运行中。
- 二进制或包存在但未运行时，视为已安装但未运行。
- 仅配置残留或仅服务残留，不视为已安装。
- 卸载后若仍有运行证据，视为失败或部分卸载。

脚本仍可提供应用专属证据，但状态归一化规则应集中在 C# 层。

### PackageResolver

负责离线资源选择和校验，从 ViewModel 中移出 Redis、RabbitMQ、MariaDB、Nginx、Elasticsearch 等目录规则。

输出模型：

```csharp
public sealed class PackageResolution
{
    public bool Found { get; init; }
    public string Path { get; init; }
    public string Version { get; init; }
    public string Hint { get; init; }
    public IReadOnlyList<string> MissingDependencies { get; init; }
}
```

ViewModel 只展示结果，不理解每个应用的目录结构。

### ScriptResolver

负责根据应用、版本、操作系统和操作类型解析脚本路径。它应支持：

- 配置引用，例如 `Scripts/Nacos/check_status_linux.sh`。
- 本地调试路径。
- 发布输出路径。
- 脚本文本命令兼容。

脚本解析失败时返回结构化错误，不在上层隐式退回成普通 shell 命令。

### IAppHandler

每个应用的特例放在 handler 中，而不是通用服务里散落 `if app.Id == ...`。

接口示意：

```csharp
public interface IAppHandler
{
    string AppId { get; }
    Task BeforeInstallAsync(OperationContext context);
    Task AfterInstallAsync(OperationContext context, ApplicationStatus status);
    Task BeforeUninstallAsync(OperationContext context);
    Task<PackageResolution?> ResolvePackageAsync(OperationContext context);
    ApplicationStatus NormalizeStatus(ApplicationStatus status, ApplicationStatusEvidence evidence);
}
```

默认 handler 覆盖大多数应用。Mosquitto 密钥文件、JDK 版本聚合、RabbitMQ 离线依赖、MariaDB 强制离线等特例由对应 handler 实现。

## 数据流

安装流程：

```text
Install request
  -> resolve app manifest
  -> resolve package
  -> resolve scripts
  -> before install hook
  -> upload package/script
  -> run install script
  -> parse script events
  -> probe status
  -> decide final result
  -> after install hook
  -> save task/logs
  -> refresh one app status
```

检测流程：

```text
Check request
  -> resolve status script
  -> run status script or generic probe
  -> parse evidence and status
  -> normalize status
  -> return snapshot
```

卸载流程：

```text
Uninstall request
  -> resolve uninstall script
  -> before uninstall hook
  -> run uninstall script
  -> parse script result
  -> probe status
  -> decide uninstalled / partial / failed
  -> save task/logs
  -> refresh one app status
```

## 结果仲裁策略

安装结果：

- 状态探测确认已安装：任务完成。
- 脚本成功但状态探测未安装：任务失败，提示安装验证失败。
- 脚本非 0 但状态探测已安装：任务完成，记录脚本异常警告。
- 基础设施异常：任务失败。

卸载结果：

- 状态探测确认未安装且无运行证据：任务完成。
- 仅配置残留：任务完成，记录残留提示。
- 仅服务残留：任务部分完成，提示可执行残留清理。
- 仍有进程、端口或 active 服务：任务失败。
- 基础设施异常：任务失败。

## 配置来源

目标状态是 `app-configuration.json` 成为主要事实来源。硬编码兜底保留，但应由同一 manifest 转换或生成，避免手工维护两份应用清单。

建议新增 manifest 校验：

- 有 `RemoteInstaller/Scripts/<App>/check_status_linux.sh` 的应用必须出现在配置中。
- 配置中声明的脚本必须能被 `ScriptResolver` 找到。
- 每个应用至少有一个检测方式。
- 安装、检测、卸载脚本输出协议符合兼容规则。

## 测试策略

新增行为测试优先级：

- `RemoteCommandRunner`：非 0 退出返回 `RemoteCommandResult`，不直接 throw。
- `ScriptProtocolParser`：文本协议和 JSONL 协议都能解析。
- `ApplicationOperationService`：脚本失败但状态已安装时任务完成。
- `ApplicationOperationService`：卸载脚本成功但状态仍运行时任务失败。
- `StatusProbeEngine`：进程、端口、服务、残留状态组合归一化正确。
- `PackageResolver`：RabbitMQ、Redis、MariaDB 等离线资源选择使用临时目录隔离。
- Manifest 校验：脚本目录和配置不漂移。

源码字符串断言可以保留少量用于脚本协议守护，但核心判断应逐步迁移为行为测试。

## 迁移计划

第一阶段：命令结果结构化

- 引入 `RemoteCommandResult`。
- 新增 `IRemoteCommandRunner` 适配现有 `SshService`。
- 安装和卸载路径不再把脚本非 0 退出当作基础设施异常。

第二阶段：脚本协议解析器

- 抽出 `ScriptProtocolParser`。
- `LogCollector`、安装验证、状态检测共用解析器。
- 兼容现有文本协议。

第三阶段：状态探测与归一化

- 引入 `ApplicationStatusEvidence`。
- 将 `NormalizeApplicationStatus` 扩展为统一策略。
- 把残留服务和配置残留从脚本自由约定收拢为平台状态。

第四阶段：应用 handler

- 先迁移 Mosquitto、JDK、RabbitMQ、MariaDB。
- 再迁移 Redis、Nginx、Elasticsearch、Nacos、Consul、Traefik。
- 移除 ViewModel 中的应用部署特例。

第五阶段：配置收敛和协议升级

- `app-configuration.json` 成为主事实来源。
- 硬编码兜底改为 manifest 转换或最小备用清单。
- 新脚本优先输出 JSONL，旧脚本继续兼容。

## 风险与缓解

- 风险：一次性拆分范围大。缓解：按阶段迁移，每阶段保持全量测试通过。
- 风险：脚本协议升级影响已有脚本。缓解：解析器长期兼容旧文本协议。
- 风险：handler 过度抽象。缓解：默认 handler 覆盖通用逻辑，只为真实特例建专用 handler。
- 风险：配置来源收敛影响旧发布包。缓解：保留硬编码兜底一段时间，并加 manifest 校验。

## 验收标准

- `InstallerService` 不再承担所有职责，安装、检测、卸载编排可以单独测试。
- 新增应用只需要添加 manifest、脚本和可选 handler，不需要改多个 UI/服务硬编码位置。
- 安装成功但脚本退出异常、卸载脚本成功但状态仍运行、仅服务残留等场景都有明确结果。
- 全量测试覆盖关键仲裁策略和状态归一化。
- `dotnet build RemoteInstaller.sln --no-restore` 和 `dotnet test RemoteInstaller.sln --no-restore` 通过。
