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

echo "PROGRESS:Initializing:5"
echo "Consul 卸载脚本开始..."

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

echo "PROGRESS:Stopping:15"
systemctl stop ${SERVICE_NAME} 2>/dev/null || true
systemctl disable ${SERVICE_NAME} 2>/dev/null || true
pkill -x consul 2>/dev/null || true
sleep 1
pkill -9 -x consul 2>/dev/null || true

echo "PROGRESS:Cleaning:45"
rm -f /etc/systemd/system/${SERVICE_NAME}.service /lib/systemd/system/${SERVICE_NAME}.service /usr/lib/systemd/system/${SERVICE_NAME}.service 2>/dev/null || true
SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/consul.service"
    "/run/systemd/generator*/consul.service"
)
for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for service_file in $pattern; do
        if [ -e "$service_file" ] || [ -L "$service_file" ]; then
            rm -f "$service_file" 2>/dev/null || true
        fi
    done
done
rm -rf /etc/consul.d /opt/consul /var/log/consul "$DATA_DIR" 2>/dev/null || true
rm -f /usr/local/bin/consul /usr/bin/consul 2>/dev/null || true

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --remove-port=${HTTP_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=${DNS_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw >/dev/null 2>&1; then
    ufw delete allow ${HTTP_PORT}/tcp 2>/dev/null || true
    ufw delete allow ${DNS_PORT}/tcp 2>/dev/null || true
fi

if command -v iptables >/dev/null 2>&1; then
    iptables -D INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p tcp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p udp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || true
fi

if id consul >/dev/null 2>&1; then
    userdel -r consul 2>/dev/null || userdel consul 2>/dev/null || true
fi
if getent group consul >/dev/null 2>&1; then
    groupdel consul 2>/dev/null || true
fi

systemctl daemon-reload 2>/dev/null || true
systemctl reset-failed ${SERVICE_NAME} 2>/dev/null || true
systemctl reset-failed 2>/dev/null || true

FAILED=0
echo "PROGRESS:Finalizing:90"
if pgrep -x consul >/dev/null 2>&1; then
    echo "警告：仍有 Consul 进程运行"
    FAILED=1
fi
if command -v consul >/dev/null 2>&1; then
    echo "警告：consul 命令仍存在"
    FAILED=1
fi
if systemctl list-unit-files 2>/dev/null | grep -q '^consul\.service'; then
    echo "警告：consul systemd 服务定义仍存在"
    FAILED=1
fi
for port in "$HTTP_PORT" "$DNS_PORT"; do
    PORT_OUTPUT="$(get_port_process_output "$port")"
    if [ -n "$PORT_OUTPUT" ] && echo "$PORT_OUTPUT" | grep -qi 'consul'; then
        echo "警告：端口 $port 仍由 Consul 监听"
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
echo "PORT: ${HTTP_PORT},${DNS_PORT}"
echo "------------------------"
