#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo "========================================"
echo "      MySQL 状态检测脚本"
echo "========================================"

get_config_candidates() {
    local -a files=()
    local file

    for file in \
        /etc/my.cnf \
        /etc/mysql/my.cnf \
        /etc/mysql/mysql.conf.d/mysqld.cnf \
        /etc/my.cnf.d/mysql-server.cnf; do
        if [ -f "$file" ]; then
            files+=("$file")
        fi
    done

    printf '%s\n' "${files[@]}"
}

get_mysql_port() {
    local file
    local port

    while IFS= read -r file; do
        port=$(grep -E '^[[:space:]]*port([[:space:]]*=|[[:space:]]+)[0-9]+' "$file" 2>/dev/null | head -n 1 | grep -oE '[0-9]+' | head -n 1)
        if [ -n "$port" ]; then
            echo "$port"
            return 0
        fi
    done < <(get_config_candidates)

    echo "3306"
}

check_port() {
    local port=$1
    local name=$2
    local process_pattern=$3
    local result=""

    if command -v ss >/dev/null 2>&1; then
        if [ -n "$process_pattern" ]; then
            result=$(ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -iE "$process_pattern" | grep -v grep)
        else
            result=$(ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -v grep)
        fi
    elif command -v netstat >/dev/null 2>&1; then
        if [ -n "$process_pattern" ]; then
            result=$(netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -iE "$process_pattern" | grep -v grep)
        else
            result=$(netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" | grep -v grep)
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

MYSQL_PORT=$(get_mysql_port)
CONFIG_PATH=$(printf '%s,' $(get_config_candidates) 2>/dev/null | sed 's/,$//')
[ -z "$CONFIG_PATH" ] && CONFIG_PATH="unknown"

SERVICE_NAME="unknown"
SERVICE_STATUS="not-found"
READY="false"
BIND_ADDRESS="unknown"

first_config=$(get_config_candidates | head -n 1)
if [ -n "$first_config" ]; then
    bind_address=$(grep -E '^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)' "$first_config" 2>/dev/null | head -n 1 | sed -E 's/^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)//' | tr -d '\r')
    if [ -n "$bind_address" ]; then
        BIND_ADDRESS="$bind_address"
    fi
fi

echo -e "${YELLOW}1. 检查安装情况:${NC}"
mysql_path=$(command -v mysqld 2>/dev/null || command -v mysql 2>/dev/null || find /usr/sbin /usr/bin /usr/local/mysql/bin -name mysqld 2>/dev/null | head -n 1)
is_installed="false"
version="未知"

if [ -n "$mysql_path" ]; then
    is_installed="true"
    echo -e "MySQL 已安装: ${GREEN}是${NC}"
    echo "位置: $mysql_path"
    v_out=$($mysql_path --version 2>&1)
    echo "$v_out"
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
else
    if dpkg -l 2>/dev/null | grep -Ei 'mysql-server|mysql-community-server|mariadb-server' | grep -q '^ii'; then
        is_installed="true"
        echo -e "MySQL 已安装: ${GREEN}是 (Debian/Ubuntu)${NC}"
    elif rpm -qa 2>/dev/null | grep -Ei 'mysql-community-server|mysql-server|mariadb-server' | grep -q .; then
        is_installed="true"
        echo -e "MySQL 已安装: ${GREEN}是 (RedHat/CentOS)${NC}"
    else
        echo -e "MySQL 已安装: ${RED}否${NC}"
    fi
fi

echo -e "${YELLOW}2. 检查运行进程:${NC}"
mysql_pid=$(pgrep -x mysqld 2>/dev/null || pgrep -x mariadbd 2>/dev/null)
is_running="false"
if [ -n "$mysql_pid" ]; then
    is_running="true"
    echo -e "MySQL 运行状态: ${GREEN}运行中 (PID: $mysql_pid)${NC}"
    ps -p "$mysql_pid" -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "MySQL 运行状态: ${RED}未运行${NC}"
fi

if check_port "$MYSQL_PORT" "MySQL" 'mysql|mysqld|mariadbd'; then
    is_running="true"
fi

echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl >/dev/null 2>&1; then
    for svc in mysqld mysql mariadb; do
        if systemctl is-active --quiet "$svc" 2>/dev/null; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS="active"
            is_running="true"
            echo -e "MySQL 服务 ($svc): ${GREEN}active (running)${NC}"
            break
        elif systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\.service"; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS=$(systemctl is-active "$svc" 2>/dev/null || echo "inactive")
            echo -e "MySQL 服务 ($svc): ${YELLOW}${SERVICE_STATUS}${NC}"
            break
        fi
    done

    if [ "$SERVICE_NAME" = "unknown" ]; then
        echo -e "MySQL 服务: ${RED}未发现 systemd 服务${NC}"
    fi
fi

echo -e "${YELLOW}5. 可用性检测:${NC}"
mysqladmin_path=$(command -v mysqladmin 2>/dev/null || echo "")
if [ -n "$mysqladmin_path" ] && timeout 5s "$mysqladmin_path" --connect-timeout=3 -uroot -P"$MYSQL_PORT" -h127.0.0.1 ping >/dev/null 2>&1; then
    READY="true"
    is_running="true"
    echo -e "MySQL 可用性: ${GREEN}ready${NC}"
else
    echo -e "MySQL 可用性: ${RED}not-ready${NC}"
fi

echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: $MYSQL_PORT"
echo "SERVICE_NAME: ${SERVICE_NAME:-unknown}"
echo "SERVICE_STATUS: ${SERVICE_STATUS:-unknown}"
echo "CONFIG_PATH: ${CONFIG_PATH:-unknown}"
echo "BIND_ADDRESS: ${BIND_ADDRESS:-unknown}"
echo "READY: ${READY:-false}"
echo "------------------------"
