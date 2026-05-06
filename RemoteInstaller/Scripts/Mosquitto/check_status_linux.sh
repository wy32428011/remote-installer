#!/bin/bash
set -euo pipefail

SERVICE_NAME="mosquitto"
MAIN_CONFIG_FILE="/etc/mosquitto/mosquitto.conf"
REMOTE_CONFIG_FILE="/etc/mosquitto/conf.d/remote-installer.conf"
MQTT_PORT="1883"
INSTALLED="false"
RUNNING="false"
VERSION="未知"

command_exists() {
    command -v "$1" >/dev/null 2>&1
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
        if command_exists dpkg-query && dpkg-query -W -f='${Status}' mosquitto 2>/dev/null | grep -q 'installed'; then
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

if command_exists mosquitto || [ -x /usr/sbin/mosquitto ] || [ -x /usr/bin/mosquitto ]; then
    INSTALLED="true"
fi

if command_exists dpkg-query && dpkg-query -W -f='${Status}' mosquitto 2>/dev/null | grep -q 'installed'; then
    INSTALLED="true"
fi

if command_exists rpm && rpm -q mosquitto >/dev/null 2>&1; then
    INSTALLED="true"
fi

if [ -f "$MAIN_CONFIG_FILE" ] || [ -f "$REMOTE_CONFIG_FILE" ]; then
    INSTALLED="true"
fi

if [ "$INSTALLED" = "true" ]; then
    resolve_version
fi

if command_exists systemctl && systemctl is-active --quiet "$SERVICE_NAME"; then
    RUNNING="true"
fi

if pgrep -x mosquitto >/dev/null 2>&1; then
    RUNNING="true"
fi

if is_port_listening "$MQTT_PORT"; then
    RUNNING="true"
fi

echo "安装状态：$INSTALLED"
echo "版本：$VERSION"
echo "服务状态：$(if command_exists systemctl && systemctl is-active --quiet "$SERVICE_NAME"; then echo active; else echo inactive; fi)"
echo "进程状态：$(if pgrep -x mosquitto >/dev/null 2>&1; then echo true; else echo false; fi)"
echo "MQTT 端口：$(if is_port_listening "$MQTT_PORT"; then echo true; else echo false; fi) ($MQTT_PORT)"

echo "INSTALLED:$INSTALLED"
echo "VERSION:$VERSION"
echo "RUNNING:$RUNNING"
echo "PORT:$MQTT_PORT"
