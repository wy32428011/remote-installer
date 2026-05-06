# Scripts Directory

This directory contains installation, uninstallation and detection scripts for different applications.

## Structure

```
Scripts/
├── MySQL/
│   ├── install_linux.sh
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
├── Nacos/
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
- 可额外放置其他离线依赖包，脚本会优先匹配与目标 CPU 架构一致的 Mosquitto 主包。
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
- 若使用 zip，推荐命名示例：`mosquitto-2.0.21-windows-x64.zip`

### Check Script Output

Mosquitto 状态脚本输出单一 MQTT 端口，不再包含 WebSocket 端口：

```
INSTALLED:true
VERSION:2.0.21 或对应离线资源版本
RUNNING:true
PORT:1883
```

## Notes

- JDK Windows 安装脚本会在 zip 解压后自动展开首层目录。
- JDK Windows 安装脚本对 msi / exe 使用静默安装参数，并在安装后通过 `java.exe` 实际位置回查安装目录。
- JDK Linux 安装脚本会将 `JAVA_HOME` 写入 `/etc/profile.d/`，并在 `Set As Default=true` 时更新 `/usr/local/bin/java` 与 `/usr/local/bin/javac` 软链。
