#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "========================================"
echo "      MySQL 状态检测脚本"
echo "========================================"

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
        echo -e "$name 端口监听: ${GREEN}是${NC}"
        echo "$result"
        return 0
    else
        echo -e "$name 端口监听: ${RED}否 (端口未开放)${NC}"
        return 1
    fi
}

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"
mysql_path=$(which mysqld 2>/dev/null || find /usr/sbin /usr/bin /usr/local/mysql/bin -name mysqld 2>/dev/null | head -n 1)
is_installed="false"
version="未知"

if [ -n "$mysql_path" ] || command -v mysql &> /dev/null; then
    is_installed="true"
    echo -e "MySQL 已安装: ${GREEN}是${NC}"
    if [ -n "$mysql_path" ]; then 
        v_out=$($mysql_path --version 2>&1)
    else 
        v_out=$(mysql --version 2>&1)
    fi
    echo "$v_out"
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
else
    echo -e "MySQL 已安装: ${RED}否${NC}"
fi

# 2. 检查运行进程
echo -e "${YELLOW}2. 检查运行进程:${NC}"
mysql_pid=$(pgrep -x mysqld 2>/dev/null || pgrep -x mariadbd 2>/dev/null)
is_running="false"
if [ -n "$mysql_pid" ]; then
    is_running="true"
    echo -e "MySQL 运行状态: ${GREEN}运行中 (PID: $mysql_pid)${NC}"
else
    echo -e "MySQL 运行状态: ${RED}未运行${NC}"
fi

# 3. 检查端口监听
check_port 3306 "MySQL" "mysql|mysqld|mariadbd"

# 4. systemd 服务状态
echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet mysql 2>/dev/null || systemctl is-active --quiet mysqld 2>/dev/null || systemctl is-active --quiet mariadb 2>/dev/null; then
        echo -e "MySQL 服务状态: ${GREEN}active (running)${NC}"
        is_running="true"
    else
        echo -e "MySQL 服务状态: ${RED}inactive/not found${NC}"
    fi
fi

# 输出机器可读的状态信息 (供 InstallerService 解析)
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: 3306"
echo "------------------------"
