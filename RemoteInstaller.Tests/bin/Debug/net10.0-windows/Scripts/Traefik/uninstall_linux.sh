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

echo "PROGRESS:Initializing:5"
echo "Traefik 卸载脚本开始..."

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

echo "PROGRESS:Stopping:15"
systemctl stop ${SERVICE_NAME} 2>/dev/null || true
systemctl disable ${SERVICE_NAME} 2>/dev/null || true
pkill -x traefik 2>/dev/null || true
sleep 1
pkill -9 -x traefik 2>/dev/null || true

echo "PROGRESS:Cleaning:45"
rm -f /etc/systemd/system/${SERVICE_NAME}.service /lib/systemd/system/${SERVICE_NAME}.service /usr/lib/systemd/system/${SERVICE_NAME}.service 2>/dev/null || true
rm -rf "$INSTALL_DIR" "$CONFIG_DIR" /var/log/traefik 2>/dev/null || true
rm -f /usr/local/bin/traefik /usr/bin/traefik 2>/dev/null || true

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --remove-port=${HTTP_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=${HTTPS_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=${DASHBOARD_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw >/dev/null 2>&1; then
    ufw delete allow ${HTTP_PORT}/tcp 2>/dev/null || true
    ufw delete allow ${HTTPS_PORT}/tcp 2>/dev/null || true
    ufw delete allow ${DASHBOARD_PORT}/tcp 2>/dev/null || true
fi

if command -v iptables >/dev/null 2>&1; then
    iptables -D INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p tcp --dport ${HTTPS_PORT} -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p tcp --dport ${DASHBOARD_PORT} -j ACCEPT 2>/dev/null || true
fi

if id traefik >/dev/null 2>&1; then
    userdel -r traefik 2>/dev/null || userdel traefik 2>/dev/null || true
fi
if getent group traefik >/dev/null 2>&1; then
    groupdel traefik 2>/dev/null || true
fi

systemctl daemon-reload 2>/dev/null || true
systemctl reset-failed ${SERVICE_NAME} 2>/dev/null || true
systemctl reset-failed 2>/dev/null || true

FAILED=0
echo "PROGRESS:Finalizing:90"
if pgrep -x traefik >/dev/null 2>&1; then
    echo "警告：仍有 Traefik 进程运行"
    FAILED=1
fi
if command -v traefik >/dev/null 2>&1; then
    echo "警告：traefik 命令仍存在"
    FAILED=1
fi
if systemctl list-unit-files 2>/dev/null | grep -q '^traefik\.service'; then
    echo "警告：traefik systemd 服务定义仍存在"
    FAILED=1
fi
for port in "$HTTP_PORT" "$HTTPS_PORT" "$DASHBOARD_PORT"; do
    PORT_OUTPUT="$(get_port_process_output "$port")"
    if [ -n "$PORT_OUTPUT" ] && echo "$PORT_OUTPUT" | grep -qi 'traefik'; then
        echo "警告：端口 $port 仍由 Traefik 监听"
        FAILED=1
    fi
done

echo "PROGRESS:Complete:100"
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: false"
echo "RUNNING: false"
if [ "$FAILED" = 0 ]; then
    echo "STAGE:SUCCESS"
else
    echo "STAGE:PARTIAL"
fi
echo "PORT: ${HTTP_PORT},${HTTPS_PORT},${DASHBOARD_PORT}"
echo "------------------------"
