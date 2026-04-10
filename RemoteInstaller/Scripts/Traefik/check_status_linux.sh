#!/bin/bash
set -e

HTTP_PORT="${HTTP_PORT:-80}"
HTTPS_PORT="${HTTPS_PORT:-443}"
DASHBOARD_PORT="${DASHBOARD_PORT:-8080}"
INSTALL_DIR="${INSTALL_DIR:-/opt/traefik}"
CONFIG_DIR="${CONFIG_DIR:-/etc/traefik}"
SERVICE_NAME="traefik"

get_port_process_output() {
    local port=$1
    if command -v ss >/dev/null 2>&1; then
        ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true
    elif command -v netstat >/dev/null 2>&1; then
        netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true
    fi
}

IS_INSTALLED=false
IS_RUNNING=false
VERSION="未知"

if command -v traefik >/dev/null 2>&1; then
    IS_INSTALLED=true
    VERSION="$(traefik version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo '未知')"
fi

if [ -f /etc/systemd/system/${SERVICE_NAME}.service ] || [ -f /lib/systemd/system/${SERVICE_NAME}.service ] || [ -f /usr/lib/systemd/system/${SERVICE_NAME}.service ]; then
    IS_INSTALLED=true
fi

if [ -d "$INSTALL_DIR" ] || [ -d "$CONFIG_DIR" ]; then
    IS_INSTALLED=true
fi

if systemctl is-active --quiet ${SERVICE_NAME} 2>/dev/null; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi

if pgrep -x traefik >/dev/null 2>&1; then
    IS_RUNNING=true
    IS_INSTALLED=true
fi

for port in "$HTTP_PORT" "$HTTPS_PORT" "$DASHBOARD_PORT"; do
    PORT_OUTPUT="$(get_port_process_output "$port")"
    if [ -n "$PORT_OUTPUT" ] && echo "$PORT_OUTPUT" | grep -qi 'traefik'; then
        IS_RUNNING=true
        IS_INSTALLED=true
    fi
done

echo "--- MACHINE READABLE ---"
echo "INSTALLED: ${IS_INSTALLED}"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: ${IS_RUNNING}"
echo "PORT: ${HTTP_PORT},${HTTPS_PORT},${DASHBOARD_PORT}"
echo "------------------------"
