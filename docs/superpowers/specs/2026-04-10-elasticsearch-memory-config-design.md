# Elasticsearch 内存配置编辑设计

## 目标
在不破坏现有 UI 结构的前提下，为 Elasticsearch 补齐“可修改内存”的能力：安装前继续使用简单的 `MEMORY_LIMIT` 单字段，安装后在现有“配置”编辑器内支持修改 JVM 堆内存，并将变更同步到 `jvm.options` 与 systemd service 中的 `ES_JAVA_OPTS`。

## 背景与现状
当前项目已经具备 Elasticsearch 安装时的内存参数能力：

- `RemoteInstaller/ViewModels/MainViewModel.cs` 中 Elasticsearch 安装参数已包含 `MEMORY_LIMIT`
- `RemoteInstaller/Scripts/Elasticsearch/install_linux.sh` 会将 `MEMORY_LIMIT` 写入：
  - `jvm.options` 中的 `-Xms/-Xmx`
  - tar 安装的 systemd service 中的 `ES_JAVA_OPTS`

但安装后的“配置”入口目前只围绕主配置文件使用，缺少对 Elasticsearch JVM 内存配置的可视化编辑能力，因此用户无法在现有配置入口内方便地修改内存。

## 设计原则
1. **遵循现有 UI 模式**：不新增独立窗口、不新增新的主界面按钮，继续使用 `ConfigEditorDialog`。
2. **安装前简单，安装后细化**：安装面板保持一个 `MEMORY_LIMIT` 单字段；安装后的配置编辑器再提供高级模式。
3. **默认安全**：默认将 `Xms` 与 `Xmx` 保持一致。
4. **最小侵入**：优先复用 Traefik 已有的文件切换能力与现有配置编辑器结构。
5. **多来源一致性**：保存内存配置时同时同步 `jvm.options` 与 service 中的 `ES_JAVA_OPTS`，避免来源不一致。

## 用户体验设计

### 1. 安装参数区
保持现状，不调整 Elasticsearch 安装面板结构。

保留以下参数：
- `HTTP_PORT`
- `CLUSTER_NAME`
- `NODE_NAME`
- `MEMORY_LIMIT`

其中：
- `MEMORY_LIMIT` 继续作为安装前唯一的内存输入项
- 不在安装面板加入高级模式或拆分 `Xms/Xmx`

### 2. 安装后的“配置”入口
点击 Elasticsearch 的“配置”后，继续进入 `ConfigEditorDialog`，但为 Elasticsearch 增加文件切换能力。

建议支持的可切换文件：
- `主配置 (elasticsearch.yml)`
- `JVM 配置 (jvm.options)`
- `服务配置 (elasticsearch.service)`，仅在远端文件存在时显示

候选路径应覆盖包安装与 tar 安装场景：
- `/etc/elasticsearch/elasticsearch.yml`
- `/etc/elasticsearch/jvm.options`
- `/etc/systemd/system/elasticsearch.service`
- `/usr/lib/systemd/system/elasticsearch.service`
- `/lib/systemd/system/elasticsearch.service`
- `/opt/elasticsearch/config/elasticsearch.yml`
- `/opt/elasticsearch/config/jvm.options`

默认打开：
- `elasticsearch.yml`

### 3. JVM 配置页面的内存编辑体验
当用户切换到 `jvm.options` 时，在保持现有编辑器布局不变的前提下，额外显示 Elasticsearch 专用的内存编辑区。

#### 默认模式
显示一个逻辑字段：
- `内存限制`

取值规则：
- 若 `-Xms` 与 `-Xmx` 相同，则显示该值，例如 `2g`
- 若二者不一致，则 `内存限制` 显示为空或提示“值不一致”

#### 高级模式
用户可手动展开“高级模式”，显示：
- `Xms`
- `Xmx`

行为规则：
- 默认模式下修改“内存限制”时，同时写入 `Xms` 与 `Xmx`
- 高级模式下允许分别修改 `Xms`、`Xmx`
- 当 `Xms != Xmx` 时，界面展示轻量提示：通常建议二者保持一致

## 配置同步规则

### 1. 以 `jvm.options` 为主来源
当读取到 `jvm.options` 与 systemd service 中的 `ES_JAVA_OPTS` 值不一致时：
- UI 以 `jvm.options` 中解析出的值作为默认显示值
- 状态栏提示：检测到 service 与 `jvm.options` 内存配置不一致，保存时将自动同步

选择 `jvm.options` 作为主来源的原因：
- 它是 Elasticsearch 的标准 JVM 配置文件
- service 中的 `ES_JAVA_OPTS` 更像启动覆盖项
- 更符合用户对“JVM 配置”页面的直觉

### 2. 保存时的写入目标
当用户在 `jvm.options` 页面点击保存或保存并重启时：

#### 写入 `jvm.options`
更新或追加：
- `-Xms{value}`
- `-Xmx{value}`

#### 同步写入 systemd service
查找并更新：
- `Environment="ES_JAVA_OPTS=-Xms... -Xmx..."`

若 service 文件存在但没有该行：
- 补写该行

若当前主机不存在 service 文件：
- 不视为失败
- 仅保存 `jvm.options`
- 在状态消息中说明未找到 service 文件，因此跳过同步

## 与现有 UI 的一致性约束
为了符合原有 UI 设计，本次设计明确约束：

1. 不新增独立“内存配置”对话框
2. 不在 Elasticsearch 主卡片上新增第二个专用按钮
3. 不替换 `ConfigEditorDialog` 为专用 Elasticsearch 编辑器
4. 继续保留当前配置编辑器结构：
   - 顶部标题区
   - 文件路径与文件切换区
   - 主编辑区
   - 状态栏
   - 保存 / 保存并重启按钮
5. Elasticsearch 专用扩展仅在以下条件下显示：
   - 软件为 Elasticsearch
   - 当前选中文件为 `jvm.options`

## 代码落点

### `RemoteInstaller/Services/ConfigurationService.cs`
需要新增 Elasticsearch 的可切换配置文件定义与路径发现逻辑，复用 Traefik 当前模式：
- 默认配置路径仍返回 `elasticsearch.yml`
- 额外支持 `GetSwitchableConfigFilesAsync("Elasticsearch")`
- 仅返回远端存在的文件

### `RemoteInstaller/ViewModels/MainViewModel.cs`
在打开 Elasticsearch 配置时，将 switchable files 传递给 `ConfigEditorViewModel`，方式与 Traefik 保持一致。

### `RemoteInstaller/ViewModels/ConfigEditorViewModel.cs`
新增仅针对 Elasticsearch + `jvm.options` 的逻辑：
- 解析 `-Xms`
- 解析 `-Xmx`
- 合成逻辑字段 `内存限制`
- 提供高级模式状态
- 保存时同步更新 `jvm.options` 与 service 文件中的 `ES_JAVA_OPTS`

其它应用和其它文件类型的行为不应改变。

### `RemoteInstaller/Views/Dialogs/ConfigEditorDialog.xaml`
在不改变整体布局的前提下，为 Elasticsearch `jvm.options` 视图增加条件显示的内存编辑区域：
- 一个“内存限制”输入框
- 一个“高级模式”切换开关
- 高级模式展开后的 `Xms` / `Xmx` 输入项

控件风格应继续沿用当前 MaterialDesignInXaml 的输入组件、间距与卡片样式。

## 验证方案

### 代码级验证
1. 测试 Elasticsearch 支持切换以下文件：
   - `elasticsearch.yml`
   - `jvm.options`
   - `elasticsearch.service`
2. 测试安装参数区仍保留 `MEMORY_LIMIT`
3. 测试 `jvm.options` 内存编辑逻辑会处理：
   - `-Xms`
   - `-Xmx`
4. 测试保存逻辑会同步 service 中的 `ES_JAVA_OPTS`
5. 测试在 service 文件缺失时不会导致保存失败

### 功能级验证
1. 新安装 Elasticsearch 时，安装面板仍只显示单字段 `内存限制`
2. 点击“配置”后默认进入 `elasticsearch.yml`
3. 可切换到 `jvm.options`
4. 在 `jvm.options` 页面：
   - 默认可修改“内存限制”
   - 开启高级模式后可分别修改 `Xms/Xmx`
5. 保存后：
   - `jvm.options` 正确更新
   - service 文件中的 `ES_JAVA_OPTS` 被同步
6. 点击“保存并重启”后，Elasticsearch 能按新内存配置重启

## 非目标
本次不做以下事项：
- 不重构整个配置编辑器架构
- 不新增 Elasticsearch 专用主界面入口
- 不修改安装参数面板为双字段或高级模式
- 不处理 Windows 下 Elasticsearch 内存配置编辑
- 不扩展到 Nacos、RabbitMQ 等其它 Java 应用的 JVM 配置统一入口

## 结论
本设计采用“安装前保持简单、安装后在现有配置编辑器中增强”的路线：
- 安装参数区继续使用 `MEMORY_LIMIT`
- 安装后通过多文件切换进入 `jvm.options`
- 默认提供 `内存限制`，高级模式下支持 `Xms/Xmx`
- 保存时同步 `jvm.options` 与 service 的 `ES_JAVA_OPTS`

该方案与现有 Traefik 文件切换模式一致，UI 侵入最小，且能补齐 Elasticsearch 当前缺失的内存配置编辑能力。