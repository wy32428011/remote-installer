#!/bin/bash
set -e

LOG_FILE="consul_install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

HTTP_PORT="${HTTP_PORT:-8500}"
DNS_PORT="${DNS_PORT:-8600}"
BIND_ADDR="${BIND_ADDR:-0.0.0.0}"
DATA_DIR="${DATA_DIR:-/var/lib/consul}"
NODE_NAME="${NODE_NAME:-consul-node}"
UI_ENABLED="${UI_ENABLED:-true}"
PACKAGE_PATH="${PACKAGE_PATH:-}"
INSTALL_DIR="/opt/consul"
CONFIG_DIR="/etc/consul.d"
SERVICE_NAME="consul"
PRIMARY_ADDR="$(hostname -I 2>/dev/null | awk '{print $1}')"
if [ -z "$PRIMARY_ADDR" ]; then
    PRIMARY_ADDR="127.0.0.1"
fi

AGENT_BIND_ADDR="$BIND_ADDR"
if [ "$AGENT_BIND_ADDR" = "0.0.0.0" ]; then
    AGENT_BIND_ADDR="$PRIMARY_ADDR"
fi

write_progress() {
    echo "PROGRESS:$1:$2"
}

is_port_listening() {
    local port=$1
    if command -v ss >/dev/null 2>&1; then
        ss -tuln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    elif command -v netstat >/dev/null 2>&1; then
        netstat -tuln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    else
        return 1
    fi
}

find_consul_package() {
    if [ -f "$PACKAGE_PATH" ]; then
        printf '%s' "$PACKAGE_PATH"
        return 0
    fi

    if [ -d "$PACKAGE_PATH" ]; then
        find "$PACKAGE_PATH" -maxdepth 1 -type f -name 'consul_*_linux_amd64.zip' | sort | tail -n 1
        return 0
    fi

    return 1
}

write_progress "Initializing" 5
echo "Consul 安装脚本开始..."
echo "PACKAGE_PATH=${PACKAGE_PATH:-<未指定>}"

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

PACKAGE_FILE="$(find_consul_package || true)"
if [ -z "$PACKAGE_FILE" ] || [ ! -f "$PACKAGE_FILE" ]; then
    echo "错误：未找到 Consul 离线安装包，请确认 PACKAGE_PATH 中包含 consul_*_linux_amd64.zip"
    exit 1
fi

echo "使用离线安装包：$PACKAGE_FILE"

write_progress "Preparing" 15
if command -v unzip >/dev/null 2>&1; then
    :
elif [ -f /etc/debian_version ]; then
    DEBIAN_FRONTEND=noninteractive apt-get update -qq || true
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq unzip curl
elif [ -f /etc/redhat-release ]; then
    yum install -y unzip curl >/dev/null 2>&1 || true
fi

mkdir -p "$INSTALL_DIR" "$CONFIG_DIR" "$DATA_DIR"
TMP_DIR="/tmp/consul_extract_$(date +%s)"
mkdir -p "$TMP_DIR"
unzip -oq "$PACKAGE_FILE" -d "$TMP_DIR"
install -m 0755 "$TMP_DIR/consul" /usr/local/bin/consul
rm -rf "$TMP_DIR"

if ! id consul >/dev/null 2>&1; then
    useradd --system --home "$DATA_DIR" --shell /sbin/nologin consul 2>/dev/null || useradd --system --home "$DATA_DIR" --shell /usr/sbin/nologin consul 2>/dev/null || true
fi

chown -R consul:consul "$DATA_DIR" "$CONFIG_DIR"

write_progress "Configuring" 45
cat > "$CONFIG_DIR/consul.hcl" <<EOF
server = true
datacenter = "dc1"
node_name = "$NODE_NAME"
data_dir = "$DATA_DIR"
bind_addr = "$AGENT_BIND_ADDR"
advertise_addr = "$AGENT_BIND_ADDR"
client_addr = "$BIND_ADDR"
ports {
  http = $HTTP_PORT
  dns = $DNS_PORT
}
ui_config {
  enabled = ${UI_ENABLED}
}
EOF

cat > /etc/systemd/system/${SERVICE_NAME}.service <<EOF
[Unit]
Description=Consul Agent
After=network-online.target
Wants=network-online.target

[Service]
User=consul
Group=consul
ExecStart=/usr/local/bin/consul agent -config-dir=$CONFIG_DIR
ExecReload=/bin/kill -HUP $MAINPID
Restart=on-failure
RestartSec=5
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --add-port=${HTTP_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --add-port=${DNS_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw >/dev/null 2>&1; then
    ufw allow ${HTTP_PORT}/tcp 2>/dev/null || true
    ufw allow ${DNS_PORT}/tcp 2>/dev/null || true
fi

if command -v iptables >/dev/null 2>&1; then
    iptables -C INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || true
    iptables -C INPUT -p tcp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p tcp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || true
    iptables -C INPUT -p udp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p udp --dport ${DNS_PORT} -j ACCEPT 2>/dev/null || true
fi

write_progress "Starting" 75
systemctl daemon-reload
systemctl enable ${SERVICE_NAME}
systemctl restart ${SERVICE_NAME}

RUNNING=false
for i in $(seq 1 15); do
    sleep 2
    if systemctl is-active --quiet ${SERVICE_NAME} && is_port_listening "$HTTP_PORT"; then
        RUNNING=true
        break
    fi
    echo "等待 Consul 服务就绪 ($i/15)..."
done

VERSION="$(consul version 2>/dev/null | head -n 1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo '未知')"

write_progress "Complete" 100
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: ${RUNNING}"
echo "PORT: ${HTTP_PORT},${DNS_PORT}"
echo "------------------------"

if [ "$RUNNING" != "true" ]; then
    echo "错误：Consul 服务启动失败"
    journalctl -u ${SERVICE_NAME} -n 50 --no-pager 2>/dev/null || true
    exit 1
fi

echo "Consul 安装完成"
