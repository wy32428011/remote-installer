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
    if command_exists dpkg-query; then
        local deb_packages
        deb_packages="$(dpkg-query -W -f='${Status} ${Package}\n' mosquitto mosquitto-clients 'libmosquitto*' 2>/dev/null | awk '($1 == "install" || $1 == "deinstall") && ($4 ~ /^(mosquitto|mosquitto-clients|libmosquitto)/) {print $4}' || true)"
        if [ -n "$deb_packages" ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y --purge $deb_packages >/dev/null 2>&1 || true
            dpkg -P $deb_packages >/dev/null 2>&1 || true
        fi
    fi

    if command_exists rpm; then
        local rpm_packages
        rpm_packages="$(rpm -qa 2>/dev/null | grep -Ei '^(mosquitto|libmosquitto)(-|$)' || true)"
        if [ -n "$rpm_packages" ]; then
            yum remove -y $rpm_packages >/dev/null 2>&1 || rpm -e $rpm_packages >/dev/null 2>&1 || true
        fi
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
rm -rf /etc/mosquitto /var/lib/mosquitto /var/log/mosquitto 2>/dev/null || true

SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/mosquitto.service"
    "/run/systemd/generator*/mosquitto.service"
)

for service_file in \
    "/etc/systemd/system/${SERVICE_NAME}.service" \
    "/lib/systemd/system/${SERVICE_NAME}.service" \
    "/usr/lib/systemd/system/${SERVICE_NAME}.service"; do
    rm -f "$service_file" 2>/dev/null || true
done

for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for service_file in $pattern; do
        if [ -e "$service_file" ] || [ -L "$service_file" ]; then
            rm -f "$service_file" 2>/dev/null || true
        fi
    done
done

INIT_SCRIPTS=(
    "/etc/init.d/mosquitto"
)

for init_script in "${INIT_SCRIPTS[@]}"; do
    service_name=$(basename "$init_script")
    if command_exists update-rc.d; then
        update-rc.d -f "$service_name" remove 2>/dev/null || true
    fi
    if command_exists chkconfig; then
        chkconfig --del "$service_name" 2>/dev/null || true
    fi
    rm -f "$init_script" 2>/dev/null || true
done

if command_exists systemctl; then
    systemctl daemon-reload >/dev/null 2>&1 || true
    systemctl reset-failed "$SERVICE_NAME" >/dev/null 2>&1 || true
fi

write_progress "CleaningFirewall" 85
remove_firewall_rules

write_progress "Complete" 100
echo "INSTALLED:false"
echo "VERSION:未知"
echo "RUNNING:false"
echo "PORT:${MQTT_PORT}"
