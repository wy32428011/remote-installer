#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo "========================================"
echo "      MariaDB 状态检测脚本"
echo "========================================"

get_config_candidates() {
    local -a files=()
    local file

    for file in \
        /etc/mysql/mariadb.conf.d/50-server.cnf \
        /etc/my.cnf \
        /etc/mysql/my.cnf \
        /etc/my.cnf.d/server.cnf; do
        if [ -f "$file" ]; then
            files+=("$file")
        fi
    done

    printf '%s\n' "${files[@]}"
}

get_mariadb_port() {
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

find_mariadb_client_command() {
    command -v mariadb 2>/dev/null || command -v mysql 2>/dev/null || true
}

verify_local_sql_ready() {
    local mariadb_cmd=$1
    timeout 5s "$mariadb_cmd" --connect-timeout=3 -N -s -uroot -e "SELECT 1" >/dev/null 2>&1 || \
    timeout 5s "$mariadb_cmd" --connect-timeout=3 -N -s -uroot --protocol=TCP -h127.0.0.1 -P"$MARIADB_PORT" -e "SELECT 1" >/dev/null 2>&1
}

verify_tcp_sql_ready() {
    local mariadb_cmd=$1
    timeout 5s "$mariadb_cmd" --connect-timeout=3 -N -s -uroot --protocol=TCP -h127.0.0.1 -P"$MARIADB_PORT" -e "SELECT 1" >/dev/null 2>&1
}

MARIADB_PORT=$(get_mariadb_port)
CONFIG_PATH=$(printf '%s,' $(get_config_candidates) 2>/dev/null | sed 's/,$//')
[ -z "$CONFIG_PATH" ] && CONFIG_PATH="unknown"

SERVICE_NAME="unknown"
SERVICE_STATUS="not-found"
READY="false"

echo -e "${YELLOW}1. 检查安装情况:${NC}"
is_installed="false"
version="未知"
if command -v mariadbd >/dev/null 2>&1 || [ -x /usr/sbin/mariadbd ] || [ -x /usr/bin/mariadbd ] || [ -x /usr/libexec/mariadbd ]; then
    is_installed="true"
    v_out=$(mariadbd --version 2>/dev/null || mariadb --version 2>/dev/null)
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    echo -e "MariaDB 已安装: ${GREEN}是 (server binary)${NC}"
elif systemctl list-unit-files 2>/dev/null | grep -Eq '^mariadb\.service|^mysql\.service|^mysqld\.service'; then
    is_installed="true"
    echo -e "MariaDB 已安装: ${GREEN}是 (systemd service)${NC}"
elif dpkg -l 2>/dev/null | grep -Ei '^ii[[:space:]]+mariadb-server([[:space:]:-]|$)' | grep -q .; then
    is_installed="true"
    echo -e "MariaDB 已安装: ${GREEN}是 (Debian/Ubuntu server package)${NC}"
elif rpm -qa 2>/dev/null | grep -Ei '^(MariaDB-server|mariadb-server)(-|$)' | grep -q .; then
    is_installed="true"
    echo -e "MariaDB 已安装: ${GREEN}是 (RedHat/CentOS server package)${NC}"
else
    echo -e "MariaDB 已安装: ${RED}否${NC}"
fi

echo -e "${YELLOW}2. 检查运行进程:${NC}"
is_running="false"
mariadb_pid=$(pgrep -x mariadbd 2>/dev/null)
if [ -n "$mariadb_pid" ]; then
    is_running="true"
    echo -e "MariaDB 运行状态: ${GREEN}运行中 (PID: $mariadb_pid)${NC}"
else
    echo -e "MariaDB 运行状态: ${RED}未运行${NC}"
fi

echo -e "${YELLOW}3. 检查端口监听 ($MARIADB_PORT):${NC}"
if ss -tlnp 2>/dev/null | grep -E ":${MARIADB_PORT}[[:space:]]|:${MARIADB_PORT}>" | grep -qiE 'mariadb|mariadbd|mysqld|mysql' || \
   netstat -tlnp 2>/dev/null | grep -E ":${MARIADB_PORT}[[:space:]]|:${MARIADB_PORT}>" | grep -qiE 'mariadb|mariadbd|mysqld|mysql'; then
    is_running="true"
    echo -e "MariaDB 端口监听: ${GREEN}是${NC}"
else
    echo -e "MariaDB 端口监听: ${RED}否${NC}"
fi

echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl >/dev/null 2>&1; then
    for svc in mariadb mysql mysqld; do
        if systemctl is-active --quiet "$svc" 2>/dev/null; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS="active"
            is_running="true"
            echo -e "MariaDB 服务 ($svc): ${GREEN}active (running)${NC}"
            break
        elif systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\\.service"; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS=$(systemctl is-active "$svc" 2>/dev/null || echo "inactive")
            echo -e "MariaDB 服务 ($svc): ${YELLOW}${SERVICE_STATUS}${NC}"
            break
        fi
    done

    if [ "$SERVICE_NAME" = "unknown" ]; then
        echo -e "MariaDB 服务: ${RED}未发现 systemd 服务${NC}"
    fi
fi

echo -e "${YELLOW}5. 可用性检测:${NC}"
MARIADB_CLIENT_CMD=$(find_mariadb_client_command)
if [ -n "$MARIADB_CLIENT_CMD" ] && verify_local_sql_ready "$MARIADB_CLIENT_CMD"; then
    READY="true"
    echo -e "MariaDB 可用性: ${GREEN}ready (local sql)${NC}"
elif [ -n "$MARIADB_CLIENT_CMD" ] && verify_tcp_sql_ready "$MARIADB_CLIENT_CMD"; then
    READY="true"
    echo -e "MariaDB 可用性: ${GREEN}ready (tcp sql)${NC}"
else
    echo -e "MariaDB 可用性: ${RED}not-ready${NC}"
fi

echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: $MARIADB_PORT"
echo "SERVICE_NAME: ${SERVICE_NAME:-unknown}"
echo "SERVICE_STATUS: ${SERVICE_STATUS:-unknown}"
echo "CONFIG_PATH: ${CONFIG_PATH:-unknown}"
echo "READY: ${READY:-false}"
echo "------------------------"
