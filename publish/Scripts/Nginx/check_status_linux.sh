#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "========================================"
echo "      Nginx 状态检测脚本"
echo "========================================"

# 初始化状态
is_installed="false"
is_running="false"
version="未知"

# 检测端口监听
check_port() {
    local port=$1
    local name=$2
    local process_pattern=$3
    local result=""

    if command -v ss &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(ss -tlnp | grep ":$port" | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(ss -tlnp | grep ":$port" | grep -v grep)
        fi
    elif command -v netstat &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(netstat -tlnp | grep ":$port" | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(netstat -tlnp | grep ":$port" | grep -v grep)
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

# 1. 检查安装情况 - 检查二进制文件
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
    # 检查包管理器
    if dpkg -l 2>/dev/null | grep -q '^ii.*nginx'; then
        is_installed="true"
        echo -e "Nginx 已安装：${GREEN}是 (Debian/Ubuntu)${NC}"
        version=$(dpkg -l | grep '^ii.*nginx' | awk '{print $3}' | head -n 1)
    elif rpm -qa 2>/dev/null | grep -q 'nginx'; then
        is_installed="true"
        echo -e "Nginx 已安装：${GREEN}是 (RedHat/CentOS)${NC}"
        version=$(rpm -qa | grep 'nginx' | head -n 1 | sed 's/nginx-//' | sed 's/\.el[0-9]*//')
    fi

    if [ "$is_installed" = "false" ]; then
        echo -e "Nginx 已安装：${RED}否${NC}"
    fi
fi

# 2. 检查运行进程
echo -e "${YELLOW}2. 检查运行进程:${NC}"
nginx_pid=$(pgrep -x nginx 2>/dev/null | head -n 1)
if [ -n "$nginx_pid" ]; then
    is_running="true"
    echo -e "Nginx 运行状态：${GREEN}运行中 (PID: $nginx_pid)${NC}"
    ps -C nginx -o pid,cmd --no-headers
else
    echo -e "Nginx 运行状态：${RED}未运行 (进程检测)${NC}"
fi

# 3. 检查端口监听
port_80_open=false
port_443_open=false
if check_port 80 "Nginx" "nginx"; then
    port_80_open=true
    # 如果端口开放但进程未检测到的，可能是权限问题，但仍认为是运行中
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
fi
if check_port 443 "Nginx" "nginx"; then
    port_443_open=true
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
fi

# 4. systemd 服务状态
echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nginx 2>/dev/null; then
        echo -e "Nginx 服务状态：${GREEN}active (running)${NC}"
        is_running="true"
        is_installed="true"  # 服务存在说明已安装
    elif systemctl list-units --all --type=service 2>/dev/null | grep -Eiwq 'nginx'; then
        echo -e "Nginx 服务状态：${YELLOW}已安装但未运行${NC}"
        is_installed="true"  # 服务存在说明已安装
    else
        echo -e "Nginx 服务状态：${RED}未找到服务${NC}"
    fi
else
    echo -e "${YELLOW}systemctl 不可用，跳过服务状态检查${NC}"
fi

# 5. 检查配置文件是否存在 (作为安装的额外证据)
if [ "$is_installed" = "false" ]; then
    if [ -f /etc/nginx/nginx.conf ] || [ -f /etc/nginx/sites-enabled/default ] || [ -f /usr/local/nginx/conf/nginx.conf ]; then
        is_installed="true"
        echo -e "${YELLOW}注意：检测到配置文件，标记为已安装${NC}"
    fi
fi

# 如果已安装且运行中，输出最终状态
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: 80,443"
echo "------------------------"

# 最终状态摘要
if [ "$is_installed" = "true" ]; then
    if [ "$is_running" = "true" ]; then
        echo -e "${GREEN}最终状态：已安装且运行中 (v${version:-未知})${NC}"
    else
        echo -e "${YELLOW}最终状态：已安装但未运行${NC}"
    fi
else
    echo -e "${RED}最终状态：未安装${NC}"
fi
