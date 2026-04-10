#!/bin/bash
set -e

HTTP_PORT="${HTTP_PORT:-8500}"
DNS_PORT="${DNS_PORT:-8600}"
DATA_DIR="${DATA_DIR:-/var/lib/consul}"
SERVICE_NAME="consul"

get_port_process_output() {
    local port=$1
    if command -v ss >/dev/null 2>&1; then
        ss -tulnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true
    elif command -v netstat >/dev/null 2>&1; then
        netstat -tulnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true
    fi
}

IS_INSTALLED=false
IS_RUNNING=false
VERSION="未知"

if command -v consul >/dev/null 2>&1; then
    IS_INSTALLED=true
    VERSION="$(consul version 2>/dev/null | head -n 1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo '未知')"
fi

if [ -f /etc/systemd/system/${SERVICE_NAME}.service ] || [ -f /lib/systemd/system/${SERVICE_NAME}.service ] || [ -f /usr/lib/systemd/system/${SERVICE_NAME}.service ]; then
    IS_INSTALLED=true
fi

if [ -d /etc/consul.d ] || [ -d "$DATA_DIR" ]; then
    IS_INSTALLED=true
fi

if systemctl is-active --quiet ${SERVICE_NAME} 2>/dev/null; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi

if pgrep -x consul >/dev/null 2>&1; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi

HTTP_OUTPUT="$(get_port_process_output "$HTTP_PORT")"
DNS_OUTPUT="$(get_port_process_output "$DNS_PORT")"
if echo "$HTTP_OUTPUT" | grep -qi 'consul'; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi
if echo "$DNS_OUTPUT" | grep -qi 'consul'; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi

echo "--- MACHINE READABLE ---"
echo "INSTALLED: ${IS_INSTALLED}"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: ${IS_RUNNING}"
echo "PORT: ${HTTP_PORT},${DNS_PORT}"
echo "------------------------"
