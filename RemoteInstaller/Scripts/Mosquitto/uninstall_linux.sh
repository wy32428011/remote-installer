#!/bin/bash
set -euo pipefail

SERVICE_NAME="mosquitto"
MAIN_CONFIG_FILE="/etc/mosquitto/mosquitto.conf"
REMOTE_CONFIG_FILE="/etc/mosquitto/conf.d/remote-installer.conf"
PASSWORD_FILE="/etc/mosquitto/passwd"
MQTT_PORT="1883"

write_progress() {
    echo "PROGRESS:$1:$2"
}

fail() {
    echo "错误：$1"
    exit 1
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

require_root() {
    if [ "$EUID" -ne 0 ]; then
        fail "请使用 root 权限运行此脚本"
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

remove_firewall_rules() {
    if command_exists firewall-cmd; then
        firewall-cmd --permanent --remove-port=${MQTT_PORT}/tcp >/dev/null 2>&1 || true
        firewall-cmd --reload >/dev/null 2>&1 || true
    fi

    if command_exists ufw; then
        ufw delete allow ${MQTT_PORT}/tcp >/dev/null 2>&1 || true
    fi

    if command_exists iptables; then
        iptables -C INPUT -p tcp --dport ${MQTT_PORT} -j ACCEPT >/dev/null 2>&1 && iptables -D INPUT -p tcp --dport ${MQTT_PORT} -j ACCEPT >/dev/null 2>&1 || true
    fi
}

remove_packages() {
    if command_exists dpkg-query && dpkg-query -W -f='${Status}' mosquitto 2>/dev/null | grep -q 'installed'; then
        DEBIAN_FRONTEND=noninteractive apt-get remove -y --purge mosquitto >/dev/null 2>&1 || true
        dpkg -P mosquitto >/dev/null 2>&1 || true
    fi

    if command_exists rpm && rpm -q mosquitto >/dev/null 2>&1; then
        yum remove -y mosquitto >/dev/null 2>&1 || rpm -e mosquitto >/dev/null 2>&1 || true
    fi
}

write_progress "Initializing" 5
echo "Mosquitto Linux 卸载开始..."

require_root
load_port

write_progress "StoppingService" 20
if command_exists systemctl; then
    systemctl stop "$SERVICE_NAME" >/dev/null 2>&1 || true
    systemctl disable "$SERVICE_NAME" >/dev/null 2>&1 || true
fi
pkill -x mosquitto >/dev/null 2>&1 || true

write_progress "RemovingPackages" 45
remove_packages

write_progress "CleaningFiles" 70
rm -f "$REMOTE_CONFIG_FILE" "$PASSWORD_FILE"

write_progress "CleaningFirewall" 85
remove_firewall_rules

write_progress "Complete" 100
echo "INSTALLED:false"
echo "VERSION:未知"
echo "RUNNING:false"
echo "PORT:${MQTT_PORT}"
