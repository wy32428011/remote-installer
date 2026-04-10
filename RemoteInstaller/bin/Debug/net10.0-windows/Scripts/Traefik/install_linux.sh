#!/bin/bash
set -e

LOG_FILE="traefik_install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

HTTP_PORT="${HTTP_PORT:-80}"
HTTPS_PORT="${HTTPS_PORT:-443}"
DASHBOARD_PORT="${DASHBOARD_PORT:-8080}"
INSTALL_DIR="${INSTALL_DIR:-/opt/traefik}"
CONFIG_DIR="${CONFIG_DIR:-/etc/traefik}"
ENABLE_DASHBOARD="${ENABLE_DASHBOARD:-true}"
PACKAGE_PATH="${PACKAGE_PATH:-}"
SERVICE_NAME="traefik"

write_progress() {
    echo "PROGRESS:$1:$2"
}

is_port_listening() {
    local port=$1
    if command -v ss >/dev/null 2>&1; then
        ss -tln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    elif command -v netstat >/dev/null 2>&1; then
        netstat -tln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    else
        return 1
    fi
}

find_traefik_package() {
    if [ -f "$PACKAGE_PATH" ]; then
        printf '%s' "$PACKAGE_PATH"
        return 0
    fi

    if [ -d "$PACKAGE_PATH" ]; then
        find "$PACKAGE_PATH" -maxdepth 1 -type f -name 'traefik_v*_linux_amd64.tar.gz' | sort | tail -n 1
        return 0
    fi

    return 1
}

write_progress "Initializing" 5
echo "Traefik 安装脚本开始..."
echo "PACKAGE_PATH=${PACKAGE_PATH:-<未指定>}"

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

PACKAGE_FILE="$(find_traefik_package || true)"
if [ -z "$PACKAGE_FILE" ] || [ ! -f "$PACKAGE_FILE" ]; then
    echo "错误：未找到 Traefik 离线安装包，请确认 PACKAGE_PATH 中包含 traefik_v*_linux_amd64.tar.gz"
    exit 1
fi

echo "使用离线安装包：$PACKAGE_FILE"

write_progress "Preparing" 15
mkdir -p "$INSTALL_DIR" "$CONFIG_DIR"
TMP_DIR="/tmp/traefik_extract_$(date +%s)"
mkdir -p "$TMP_DIR"
tar -xzf "$PACKAGE_FILE" -C "$TMP_DIR"
install -m 0755 "$TMP_DIR/traefik" /usr/local/bin/traefik
rm -rf "$TMP_DIR"

if ! id traefik >/dev/null 2>&1; then
    useradd --system --home "$INSTALL_DIR" --shell /sbin/nologin traefik 2>/dev/null || useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin traefik 2>/dev/null || true
fi

mkdir -p "$INSTALL_DIR/data"
chown -R traefik:traefik "$INSTALL_DIR" "$CONFIG_DIR"

write_progress "Configuring" 45
cat > "$CONFIG_DIR/traefik.toml" <<EOF
[entryPoints]
  [entryPoints.web]
    address = ":${HTTP_PORT}"
  [entryPoints.websecure]
    address = ":${HTTPS_PORT}"
  [entryPoints.traefik]
    address = ":${DASHBOARD_PORT}"

[providers]
  [providers.file]
    filename = "$CONFIG_DIR/dynamic.toml"
    watch = true

[log]
  level = "INFO"
EOF

if [ "$ENABLE_DASHBOARD" = "true" ]; then
cat >> "$CONFIG_DIR/traefik.toml" <<EOF

[api]
  dashboard = true
  insecure = true
EOF
fi

cat > "$CONFIG_DIR/dynamic.toml" <<'EOF'
# reserved for dynamic configuration
EOF

cat > /etc/systemd/system/${SERVICE_NAME}.service <<EOF
[Unit]
Description=Traefik Proxy
After=network-online.target
Wants=network-online.target

[Service]
User=traefik
Group=traefik
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
NoNewPrivileges=true
ExecStart=/usr/local/bin/traefik --configfile=$CONFIG_DIR/traefik.toml
Restart=on-failure
RestartSec=5
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --add-port=${HTTP_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --add-port=${HTTPS_PORT}/tcp 2>/dev/null || true
    firewall-cmd --permanent --add-port=${DASHBOARD_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw >/dev/null 2>&1; then
    ufw allow ${HTTP_PORT}/tcp 2>/dev/null || true
    ufw allow ${HTTPS_PORT}/tcp 2>/dev/null || true
    ufw allow ${DASHBOARD_PORT}/tcp 2>/dev/null || true
fi

if command -v iptables >/dev/null 2>&1; then
    iptables -C INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p tcp --dport ${HTTP_PORT} -j ACCEPT 2>/dev/null || true
    iptables -C INPUT -p tcp --dport ${HTTPS_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p tcp --dport ${HTTPS_PORT} -j ACCEPT 2>/dev/null || true
    iptables -C INPUT -p tcp --dport ${DASHBOARD_PORT} -j ACCEPT 2>/dev/null || iptables -I INPUT -p tcp --dport ${DASHBOARD_PORT} -j ACCEPT 2>/dev/null || true
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
    echo "等待 Traefik 服务就绪 ($i/15)..."
done

VERSION="$(traefik version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo '未知')"

write_progress "Complete" 100
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: ${RUNNING}"
echo "PORT: ${HTTP_PORT},${HTTPS_PORT},${DASHBOARD_PORT}"
echo "------------------------"

if [ "$RUNNING" != "true" ]; then
    echo "错误：Traefik 服务启动失败"
    journalctl -u ${SERVICE_NAME} -n 50 --no-pager 2>/dev/null || true
    exit 1
fi

echo "Traefik 安装完成"
