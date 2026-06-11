
#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "========================================"
echo "      Redis 状态检测脚本"
echo "========================================"

# 检测端口监听
check_port() {
    local port=$1
    local name=$2
    local process_pattern=$3
    local result=""
    
    if command -v ss &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(ss -tlnp | grep ":$port " | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(ss -tlnp | grep ":$port " | grep -v grep)
        fi
    elif command -v netstat &> /dev/null; then
        if [ -n "$process_pattern" ]; then
            result=$(netstat -tlnp | grep ":$port " | grep -i "$process_pattern" | grep -v grep)
        else
            result=$(netstat -tlnp | grep ":$port " | grep -v grep)
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

# 0. 从配置文件中获取实际端口（而非硬编码 6379）
REDIS_PORT=6379
REDIS_PASSWORD=""
BIND_ADDRESS="127.0.0.1"
PROTECTED_MODE="yes"
ALLOW_REMOTE_EFFECTIVE="false"
CONF_FILES=("/etc/redis/redis.conf" "/etc/redis.conf" "/etc/redis-server/redis.conf" "/usr/local/etc/redis.conf")
for conf in "${CONF_FILES[@]}"; do
    if [ -f "$conf" ]; then
        EXTRACTED_PORT=$(grep -E '^[[:space:]]*port[[:space:]]+[0-9]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
        if [ -n "$EXTRACTED_PORT" ]; then
            REDIS_PORT=$EXTRACTED_PORT
        fi
        REDIS_PASSWORD=$(grep -E '^[[:space:]]*requirepass[[:space:]]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n"'"'" )
        EXTRACTED_BIND=$(grep -E '^[[:space:]]*bind[[:space:]]+' "$conf" 2>/dev/null | head -n 1 | sed -E 's/^[[:space:]]*bind[[:space:]]+//' | tr -d '\r')
        if [ -n "$EXTRACTED_BIND" ]; then
            BIND_ADDRESS="$EXTRACTED_BIND"
        fi
        EXTRACTED_PROTECTED=$(grep -E '^[[:space:]]*protected-mode[[:space:]]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
        if [ -n "$EXTRACTED_PROTECTED" ]; then
            PROTECTED_MODE="$EXTRACTED_PROTECTED"
        fi
        break
    fi
done

if echo "$BIND_ADDRESS" | grep -Eq '0\.0\.0\.0|::|\*' && [ "$PROTECTED_MODE" = "no" ]; then
    ALLOW_REMOTE_EFFECTIVE="true"
fi

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"
redis_path=$(which redis-server 2>/dev/null || find /usr/bin /usr/sbin /usr/local/bin /opt/redis/bin /snap/bin -name redis-server 2>/dev/null | head -n 1)
is_installed="false"
version="未知"
package_installed="false"
service_only_stale="false"

if command -v dpkg-query &> /dev/null; then
    if dpkg-query -W -f='${db:Status-Abbrev} ${binary:Package}\n' redis redis-server 2>/dev/null | awk '$1 ~ /^ii/ { found=1 } END { exit found ? 0 : 1 }'; then
        package_installed="true"
    fi
elif command -v rpm &> /dev/null; then
    if rpm -q redis redis-server >/dev/null 2>&1; then
        package_installed="true"
    fi
fi

if [ -n "$redis_path" ]; then
    is_installed="true"
    echo -e "Redis 已安装: ${GREEN}是${NC}"
    echo "位置: $redis_path"
    v_out=$($redis_path --version 2>&1)
    echo "$v_out"
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
elif [ "$package_installed" = "true" ]; then
    is_installed="true"
    echo -e "Redis 已安装: ${GREEN}是 (通过软件包发现)${NC}"
else
    echo -e "Redis 已安装: ${RED}否${NC}"
fi

# 2. 检查运行进程
echo -e "${YELLOW}2. 检查运行进程:${NC}"
redis_pid=$(pgrep -x redis-server 2>/dev/null)
is_running="false"
if [ -n "$redis_pid" ]; then
    is_running="true"
    echo -e "Redis 运行状态: ${GREEN}运行中 (PID: $redis_pid)${NC}"
    ps -p "$redis_pid" -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "Redis 运行状态: ${RED}未运行${NC}"
fi

# 3. 检查端口监听
port_listening="false"
if check_port "$REDIS_PORT" "Redis" "redis"; then
    port_listening="true"
    is_running="true"
fi

# 4. systemd 服务状态
echo -e "${YELLOW}4. systemd 服务状态:${NC}"
SERVICE_NAME=""
SERVICE_STATUS="not-found"
SERVICE_FOUND=false
if command -v systemctl &> /dev/null; then
    for svc in redis redis-server; do
        if systemctl is-active --quiet "$svc" 2>/dev/null; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS="active"
            echo -e "Redis 服务 ($svc): ${GREEN}active (running)${NC}"
            is_running="true"
            is_installed="true"
            SERVICE_FOUND=true
            break
        elif systemctl list-unit-files 2>/dev/null | grep -q "${svc}.service"; then
            SERVICE_NAME="$svc"
            SERVICE_STATUS=$(systemctl is-active "$svc" 2>/dev/null || echo "unknown")
            echo -e "Redis 服务 ($svc): ${RED}$SERVICE_STATUS${NC}"
            SERVICE_FOUND=true
            break
        fi
    done
    if [ "$SERVICE_FOUND" = false ]; then
        echo -e "Redis 服务: ${RED}未发现 systemd 服务${NC}"
    fi
fi

if [ "$SERVICE_FOUND" = true ] && [ "$is_installed" != "true" ] && [ "$is_running" != "true" ] && [ "$port_listening" != "true" ]; then
    service_only_stale="true"
    echo -e "${YELLOW}Redis 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理${NC}"
fi

echo -e "${YELLOW}4.1 远程访问配置:${NC}"
echo "bind 地址: $BIND_ADDRESS"
echo "protected-mode: $PROTECTED_MODE"
echo "远程访问有效: $ALLOW_REMOTE_EFFECTIVE"

# 5. 连接测试
echo -e "${YELLOW}5. 测试 Redis 连接:${NC}"
if command -v redis-cli &> /dev/null; then
    if [ -n "$REDIS_PASSWORD" ]; then
        PING_RESULT=$(redis-cli -p "$REDIS_PORT" -a "$REDIS_PASSWORD" --no-auth-warning ping 2>/dev/null || echo "")
    else
        PING_RESULT=$(redis-cli -p "$REDIS_PORT" ping 2>/dev/null || echo "")
    fi
    
    if [ "$PING_RESULT" = "PONG" ]; then
        echo -e "Redis 连接测试: ${GREEN}成功 (PONG)${NC}"
        is_running="true"
    else
        echo -e "Redis 连接测试: ${RED}失败${NC}"
    fi
else
    echo -e "Redis 连接测试: ${RED}无法测试 (redis-cli 未安装)${NC}"
fi

# 输出机器可读的状态信息 (供 InstallerService 解析)
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: $REDIS_PORT"
echo "PACKAGE_INSTALLED: ${package_installed:-false}"
echo "SERVICE_NAME: ${SERVICE_NAME:-unknown}"
echo "SERVICE_STATUS: ${SERVICE_STATUS:-unknown}"
echo "SERVICE_ONLY_STALE: ${service_only_stale:-false}"
echo "BIND_ADDRESS: ${BIND_ADDRESS:-unknown}"
echo "ALLOW_REMOTE_EFFECTIVE: ${ALLOW_REMOTE_EFFECTIVE:-false}"
echo "------------------------"
