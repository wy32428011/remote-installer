#!/bin/bash
set -euo pipefail

SERVICE_NAME="mosquitto"
MAIN_CONFIG_FILE="/etc/mosquitto/mosquitto.conf"
REMOTE_CONFIG_FILE="/etc/mosquitto/conf.d/remote-installer.conf"
MQTT_PORT="1883"
INSTALLED="false"
RUNNING="false"
VERSION="未知"
service_only_stale="false"
CONFIG_ONLY_RESIDUE="false"
SERVICE_FOUND="false"
CONFIG_FOUND="false"
PORT_LISTENING="false"

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

get_deb_package_status() {
    local package_name="$1"

    command_exists dpkg-query || return 1
    dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null || true
}

is_deb_package_installed() {
    local package_name="$1"
    local package_status

    package_status="$(get_deb_package_status "$package_name" || true)"
    [ "$package_status" = "install ok installed" ]
}

has_mosquitto_binary() {
    command_exists mosquitto || [ -x /usr/sbin/mosquitto ] || [ -x /usr/bin/mosquitto ]
}

is_port_listening() {
    local port="$1"
    if command_exists ss; then
        ss -tln 2>/dev/null | grep -Eq "[\:\.]${port}[[:space:]]"
    elif command_exists netstat; then
        netstat -tln 2>/dev/null | grep -Eq "[\:\.]${port}[[:space:]]"
    else
        return 1
    fi
}

load_port() {
    local config_file
    for config_file in "$REMOTE_CONFIG_FILE" "$MAIN_CONFIG_FILE"; do
        if [ -f "$config_file" ]; then
            local extracted_port
            extracted_port="$(grep -E '^[[:space:]]*listener[[:space:]]+[0-9]+' "$config_file" | head -n 1 | awk '{print $2}' | tr -d ';\r\n')"
            if [ -n "$extracted_port" ]; then
                MQTT_PORT="$extracted_port"
                break
            fi
        fi
    done
}

resolve_version() {
    if command_exists mosquitto; then
        VERSION="$(mosquitto -h 2>&1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
    fi

    if [ -z "$VERSION" ] || [ "$VERSION" = "未知" ]; then
        if is_deb_package_installed "mosquitto"; then
            VERSION="$(dpkg-query -W -f='${Version}' mosquitto 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
        elif command_exists rpm && rpm -q mosquitto >/dev/null 2>&1; then
            VERSION="$(rpm -q --queryformat '%{VERSION}' mosquitto 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
        fi
    fi

    VERSION="${VERSION:-未知}"
}

echo "========================================"
echo "      Mosquitto 状态检测"
echo "========================================"

load_port

DEB_PACKAGE_STATUS="$(get_deb_package_status mosquitto || true)"

if [ "$DEB_PACKAGE_STATUS" = "install ok installed" ]; then
    INSTALLED="true"
elif [ -n "$DEB_PACKAGE_STATUS" ]; then
    echo "Mosquitto dpkg 状态不是完整安装：$DEB_PACKAGE_STATUS"
elif has_mosquitto_binary; then
    INSTALLED="true"
fi

if command_exists rpm && rpm -q mosquitto >/dev/null 2>&1; then
    INSTALLED="true"
fi

if [ -f "$MAIN_CONFIG_FILE" ] || [ -f "$REMOTE_CONFIG_FILE" ]; then
    CONFIG_FOUND="true"
fi

if [ "$INSTALLED" = "true" ]; then
    resolve_version
fi

if command_exists systemctl && systemctl is-active --quiet "$SERVICE_NAME"; then
    RUNNING="true"
    INSTALLED="true"
elif [ -f "/etc/systemd/system/${SERVICE_NAME}.service" ] || [ -f "/lib/systemd/system/${SERVICE_NAME}.service" ] || [ -f "/usr/lib/systemd/system/${SERVICE_NAME}.service" ]; then
    SERVICE_FOUND="true"
elif command_exists systemctl && systemctl list-unit-files 2>/dev/null | grep -q "^${SERVICE_NAME}\\.service"; then
    SERVICE_FOUND="true"
fi

if pgrep -x mosquitto >/dev/null 2>&1; then
    RUNNING="true"
    INSTALLED="true"
fi

if is_port_listening "$MQTT_PORT"; then
    PORT_LISTENING="true"
fi

if [ "$SERVICE_FOUND" = "true" ] && [ "$INSTALLED" != "true" ] && [ "$RUNNING" != "true" ]; then
    service_only_stale="true"
    echo "Mosquitto 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理"
fi

if [ "$CONFIG_FOUND" = "true" ] && [ "$INSTALLED" != "true" ] && [ "$RUNNING" != "true" ]; then
    CONFIG_ONLY_RESIDUE="true"
    echo "Mosquitto 配置文件存在，但未发现完整安装或 Mosquitto 运行进程，按残留配置处理"
fi

echo "安装状态：$INSTALLED"
echo "版本：$VERSION"
echo "服务状态：$(if command_exists systemctl && systemctl is-active --quiet "$SERVICE_NAME"; then echo active; else echo inactive; fi)"
echo "进程状态：$(if pgrep -x mosquitto >/dev/null 2>&1; then echo true; else echo false; fi)"
echo "MQTT 端口：$PORT_LISTENING ($MQTT_PORT)"

echo "INSTALLED:$INSTALLED"
echo "VERSION:$VERSION"
echo "RUNNING:$RUNNING"
echo "PORT:$MQTT_PORT"
if [ "$RUNNING" = "true" ]; then
    echo "PORT_LISTENING:$PORT_LISTENING"
else
    echo "PORT_LISTENING:false"
fi
echo "SERVICE_ONLY_STALE: ${service_only_stale:-false}"
echo "CONFIG_ONLY_RESIDUE:$CONFIG_ONLY_RESIDUE"
