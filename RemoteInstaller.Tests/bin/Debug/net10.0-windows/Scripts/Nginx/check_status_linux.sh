#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo "========================================"
echo "      Nginx 状态检测脚本"
echo "========================================"

is_installed="false"
is_running="false"
version="未知"
SERVICE_NAME="nginx"
SERVICE_STATUS="not-found"
CONFIG_PATH="unknown"

get_config_candidates() {
    local -a files=()
    local file

    for file in \
        /etc/nginx/sites-available/default \
        /etc/nginx/sites-enabled/default \
        /etc/nginx/conf.d/default.conf \
        /etc/nginx/conf.d/*.conf \
        /etc/nginx/nginx.conf \
        /usr/local/nginx/conf/nginx.conf; do
        if [ -f "$file" ]; then
            files+=("$file")
        fi
    done

    printf '%s\n' "${files[@]}"
}

get_listen_ports() {
    local ports
    ports=$(for file in $(get_config_candidates); do
        grep -Eho 'listen[[:space:]]+([^;]|\[[^]]+\])+' "$file" 2>/dev/null || true
    done | grep -Eo '([0-9]{2,5})' | sort -u)

    if [ -n "$ports" ]; then
        echo "$ports"
    else
        printf '80\n443\n'
    fi
}

check_port() {
    local port=$1
    local name=$2
    local process_pattern=$3
    local result=""

    if command -v ss &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -v grep)
        fi
    elif command -v netstat &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -v grep)
        fi
    fi

    echo -e "${YELLOW}3. 检查端口监听 ($port):${NC}"
    if [ -n "$result" ]; then
        echo -e "$name 端口监听：${GREEN}是${NC}"
        echo "$result"
        return 0
    else
        echo -e "$name 端口监听：${RED}否 (端口未开放)${NC}"
        return 1
    fi
}

echo -e "${YELLOW}1. 检查安装情况:${NC}"
nginx_path=$(which nginx 2>/dev/null || find /usr/sbin /usr/local/nginx/sbin -name nginx 2>/dev/null | head -n 1)

if [ -n "$nginx_path" ]; then
    is_installed="true"
    echo -e "Nginx 已安装：${GREEN}是${NC}"
    echo "位置：$nginx_path"
    v_out=$($nginx_path -v 2>&1)
    echo "$v_out"
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
else
    if dpkg -l 2>/dev/null | grep -q '^ii.*nginx'; then
        is_installed="true"
        echo -e "Nginx 已安装：${GREEN}是 (Debian/Ubuntu)${NC}"
        version=$(dpkg -l | grep '^ii.*nginx' | awk '{print $3}' | head -n 1)
    elif rpm -qa 2>/dev/null | grep -q '^nginx'; then
        is_installed="true"
        echo -e "Nginx 已安装：${GREEN}是 (RedHat/CentOS)${NC}"
        version=$(rpm -qa | grep '^nginx' | head -n 1 | sed 's/nginx-//' | sed 's/\.el[0-9].*//')
    else
        echo -e "Nginx 已安装：${RED}否${NC}"
    fi
fi

echo -e "${YELLOW}2. 检查运行进程:${NC}"
nginx_pid=$(pgrep -x nginx 2>/dev/null | head -n 1)
if [ -n "$nginx_pid" ]; then
    is_running="true"
    echo -e "Nginx 运行状态：${GREEN}运行中 (PID: $nginx_pid)${NC}"
    ps -C nginx -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "Nginx 运行状态：${RED}未运行 (进程检测)${NC}"
fi

mapfile -t CONFIG_FILES < <(get_config_candidates)
if [ ${#CONFIG_FILES[@]} -gt 0 ]; then
    CONFIG_PATH=$(printf '%s,' "${CONFIG_FILES[@]}" | sed 's/,$//')
fi

mapfile -t PORTS < <(get_listen_ports)
PORT_OUTPUT=$(printf '%s,' "${PORTS[@]}" | sed 's/,$//')

for port in "${PORTS[@]}"; do
    if check_port "$port" "Nginx" "nginx"; then
        is_running="true"
    fi
done

echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nginx 2>/dev/null; then
        SERVICE_STATUS="active"
        echo -e "Nginx 服务状态：${GREEN}active (running)${NC}"
        is_running="true"
        is_installed="true"
    elif systemctl list-unit-files 2>/dev/null | grep -q '^nginx\.service'; then
        SERVICE_STATUS=$(systemctl is-active nginx 2>/dev/null || echo "inactive")
        echo -e "Nginx 服务状态：${YELLOW}${SERVICE_STATUS}${NC}"
    else
        echo -e "Nginx 服务状态：${RED}未找到服务${NC}"
    fi
else
    echo -e "${YELLOW}systemctl 不可用，跳过服务状态检查${NC}"
fi

if [ "$is_installed" = "false" ] && [ ${#CONFIG_FILES[@]} -gt 0 ]; then
    echo -e "${YELLOW}注意：检测到配置文件残留，但不单独判定为已安装${NC}"
fi

HTTP_READY="false"
if command -v curl >/dev/null 2>&1; then
    for port in "${PORTS[@]}"; do
        if curl -I -s --max-time 3 "http://127.0.0.1:${port}/" >/dev/null 2>&1; then
            HTTP_READY="true"
            break
        fi
    done
fi

echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: ${PORT_OUTPUT:-80,443}"
echo "SERVICE_NAME: ${SERVICE_NAME:-nginx}"
echo "SERVICE_STATUS: ${SERVICE_STATUS:-unknown}"
echo "CONFIG_PATH: ${CONFIG_PATH:-unknown}"
echo "HTTP_READY: ${HTTP_READY:-false}"
echo "------------------------"

if [ "$is_installed" = "true" ]; then
    if [ "$is_running" = "true" ]; then
        echo -e "${GREEN}最终状态：已安装且运行中 (v${version:-未知})${NC}"
    else
        echo -e "${YELLOW}最终状态：已安装但未运行${NC}"
    fi
else
    echo -e "${RED}最终状态：未安装${NC}"
fi
