# Scripts Directory

This directory contains installation, uninstallation and detection scripts for different applications.

## Structure

```
Scripts/
├── MySQL/
│   ├── install_ubuntu.sh
│   ├── install_centos.sh
│   ├── install_linux.sh
│   ├── install_common.sh
│   ├── install_windows.ps1
│   ├── uninstall_linux.sh
│   ├── uninstall_windows.ps1
│   ├── check_linux.sh
│   └── check_windows.ps1
├── Redis/
│   └── ...
├── Elasticsearch/
│   └── ...
├── RabbitMQ/
│   └── ...
├── MariaDB/
│   └── ...
├── JDK8/
│   ├── install_linux.sh
│   ├── install_windows.ps1
│   ├── uninstall_linux.sh
│   ├── uninstall_windows.ps1
│   ├── check_status_linux.sh
│   ├── check_status_windows.ps1
│   ├── windows/
│   ├── ubuntu/
│   └── centos/
├── JDK11/
│   └── ...
├── JDK17/
│   └── ...
├── Nginx/
│   └── ...
└── Mosquitto/
    ├── install_linux.sh
    ├── check_status_linux.sh
    ├── uninstall_linux.sh
    ├── install_windows.ps1
    ├── check_status_windows.ps1
    ├── uninstall_windows.ps1
    ├── windows/
    ├── mosquitto-ubuntu/
    │   ├── 22/
    │   └── 24/
    └── mosquitto-centos7/
```

## Linux Install Script Selection

Linux 安装脚本按目标主机系统优先选择专属入口，旧的通用脚本名继续保留为兼容入口：

1. CentOS / RHEL 系主机优先匹配 `install_centos.sh`，找不到时回退 `install_linux.sh`。
2. Ubuntu 主机优先匹配 `install_ubuntu.sh`，找不到时回退 `install_linux.sh`。
3. Windows 主机仍匹配 `install_windows.ps1`。
4. 版本目录内的脚本优先级高于应用根目录，例如 `Scripts/MySQL/8.0.35/install_ubuntu.sh` 会优先于 `Scripts/MySQL/install_ubuntu.sh`。

已经拆分系统入口的应用包括：

- `Consul`
- `Elasticsearch`
- `MariaDB`
- `Mosquitto`
- `MySQL`
- `Nginx`
- `RabbitMQ`
- `Redis`

这些目录中的 `install_ubuntu.sh` 与 `install_centos.sh` 负责系统校验和入口分流，`install_common.sh` 保存原有公共安装执行体，`install_linux.sh` 只作为历史配置和旧版本调用的兼容分发入口。安装服务上传 Linux 安装脚本时会同时上传同目录的 `.sh` 文件，保证系统专属入口可以调用同目录公共脚本。

### Smoke Mode

系统专属入口支持轻量冒烟模式，不会执行实际安装：

```bash
REMOTE_INSTALLER_SMOKE_TEST=true bash install_ubuntu.sh
REMOTE_INSTALLER_SMOKE_TEST=true bash install_centos.sh
```

冒烟模式会校验当前系统类型、检查 `install_common.sh` 是否存在，并执行 `bash -n install_common.sh` 验证公共执行体语法。

## Script Format

### Install Script Output
Scripts should output progress in the following format:
```
PROGRESS:StageName:Percentage
```

Example:
```
PROGRESS:Extracting:30
PROGRESS:Configuring:60
PROGRESS:Installing:90
```

### Check Script Output
Check scripts should output:
```
INSTALLED:true|false
VERSION:x.y.z
RUNNING:true|false
PORT:port_number
```

`PORT` 只表示脚本识别到的配置端口或默认端口，不能用来推断应用正在运行。
如果脚本需要单独表达端口监听结果，请额外输出：
```
PORT_LISTENING:true|false
```
状态归一化会优先信任 `INSTALLED`、`RUNNING`、进程、active 服务和 `PORT_LISTENING` 这类事实证据，避免仅因保留端口配置而误判为已安装或运行中。

RabbitMQ 需要额外注意：Erlang VM、默认端口 `5672/15672`、`rabbitmqctl` 命令或 inactive 服务定义都可能在卸载后残留，不能单独作为 RabbitMQ 安装/运行证据。RabbitMQ 脚本应输出 `PACKAGE_INSTALLED`、`BINARY_FOUND`、`PROCESS_FOUND`、`SERVICE_ACTIVE`、`PORT_LISTENING` 和残留标记，且 `PORT_LISTENING` 只能在端口归属到 RabbitMQ 进程时为 `true`。

JDK status scripts may additionally output:
```
JAVA_HOME:path
```

## JDK Offline Package Layout

JDK 8、JDK 11、JDK 17 均按独立应用维护离线资源目录，推荐放置在以下路径：

- `RemoteInstaller/Scripts/JDK8/windows`
- `RemoteInstaller/Scripts/JDK8/ubuntu`
- `RemoteInstaller/Scripts/JDK8/centos`
- `RemoteInstaller/Scripts/JDK11/windows`
- `RemoteInstaller/Scripts/JDK11/ubuntu`
- `RemoteInstaller/Scripts/JDK11/centos`
- `RemoteInstaller/Scripts/JDK17/windows`
- `RemoteInstaller/Scripts/JDK17/ubuntu`
- `RemoteInstaller/Scripts/JDK17/centos`

### Recommended Naming

当前 JDK 离线资源按 Temurin 风格命名，自动识别优先按文件名中的版本号匹配，其次按文件最后修改时间兜底。

#### Windows

支持以下格式：
- `.zip`
- `.msi`
- `.exe`

推荐命名示例：
- `OpenJDK8U-jdk_x64_windows_hotspot_8u452b09.zip`
- `OpenJDK11U-jdk_x64_windows_hotspot_11.0.27_6.msi`
- `OpenJDK17U-jdk_x64_windows_hotspot_17.0.15_6.exe`

#### Ubuntu / CentOS

当前 Linux 侧统一按压缩包安装，支持以下格式：
- `.tar.gz`
- `.tar.xz`
- `.tgz`

推荐命名示例：
- `OpenJDK8U-jdk_x64_linux_hotspot_8u452b09.tar.gz`
- `OpenJDK11U-jdk_x64_linux_hotspot_11.0.27_6.tar.xz`
- `OpenJDK17U-jdk_x64_linux_hotspot_17.0.15_6.tgz`

## Mosquitto Offline Package Layout

Mosquitto 按独立应用维护离线资源目录，推荐放置在以下路径：

- `RemoteInstaller/Scripts/Mosquitto/windows`
- `RemoteInstaller/Scripts/Mosquitto/mosquitto-ubuntu/22`
- `RemoteInstaller/Scripts/Mosquitto/mosquitto-ubuntu/24`
- `RemoteInstaller/Scripts/Mosquitto/mosquitto-centos7`

### Linux (Ubuntu 22 / Ubuntu 24)

- 仅支持目录型离线资源。
- 根目录至少包含：`mosquitto_*.deb` 或 `mosquitto-*.deb`
- 根目录必须同时包含与目标 CPU 架构匹配的关键依赖包：`libmosquitto1`、`libcjson1`。
- Ubuntu 22 还必须包含：`libmicrohttpd12`。
- Ubuntu 24 还必须包含：`libmicrohttpd12t64`。
- 若启用用户名密码认证，根目录还需要包含 `mosquitto-clients`，用于提供 `mosquitto_passwd`。
- 可额外放置其他离线依赖包，脚本会优先匹配与目标 CPU 架构一致的 Mosquitto 主包，并在安装前提示缺失的关键依赖。
- 当前仓库示例版本：Ubuntu 22 使用 `2.0.22`，Ubuntu 24 使用 `2.1.2`。

### Linux (CentOS 7)

- 仅支持目录型离线资源。
- 根目录至少包含：`mosquitto-*.rpm`
- 可额外放置其他离线依赖包，脚本会优先匹配与目标 CPU 架构一致的 Mosquitto 主包。
- 当前仓库示例版本：`1.6.10`。

### Windows

- 支持以下格式：
  - `.zip`
  - 已解压目录
- 离线资源中至少应包含：
  - `mosquitto.exe`
  - `mosquitto_passwd.exe`
  - `mosquitto.conf`
- 当前仓库使用的是官方安装器静默展开后的目录：`windows/extracted-2.0.21`
- zip 包文件名应匹配：`mosquitto-*.zip`
- 若使用 zip，推荐命名示例：`mosquitto-2.0.21-windows-x64.zip`

### Check Script Output

Mosquitto 状态脚本输出单一 MQTT 端口，不再包含 WebSocket 端口：

```
INSTALLED:true
VERSION:2.0.21 或对应离线资源版本
RUNNING:true
PORT:1883
PORT_LISTENING:true
```

Ubuntu 状态检测会精确要求 dpkg 状态为 `install ok installed`；如果 `dpkg -i` 因缺依赖留下 `unpacked`、`half-configured` 等半安装状态，脚本会按未完整安装处理，避免安装失败后被误判为成功。

## Notes

- JDK Windows 安装脚本会在 zip 解压后自动展开首层目录。
- JDK Windows 安装脚本对 msi / exe 使用静默安装参数，并在安装后通过 `java.exe` 实际位置回查安装目录。
- JDK Linux 安装脚本会将 `JAVA_HOME` 写入 `/etc/profile.d/`，并在 `Set As Default=true` 时更新 `/usr/local/bin/java` 与 `/usr/local/bin/javac` 软链。
