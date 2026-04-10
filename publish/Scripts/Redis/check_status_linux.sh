
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
CONF_FILES=("/etc/redis/redis.conf" "/etc/redis.conf" "/etc/redis-server/redis.conf" "/usr/local/etc/redis.conf")
for conf in "${CONF_FILES[@]}"; do
    if [ -f "$conf" ]; then
        EXTRACTED_PORT=$(grep -E '^[[:space:]]*port[[:space:]]+[0-9]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
        if [ -n "$EXTRACTED_PORT" ]; then
            REDIS_PORT=$EXTRACTED_PORT
        fi
        # 检查是否配置了密码
        REDIS_PASSWORD=$(grep -E '^[[:space:]]*requirepass[[:space:]]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n"'"'" )
        break
    fi
done

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"
redis_path=$(which redis-server 2>/dev/null || find /usr/bin /usr/sbin /usr/local/bin /opt/redis/bin /snap/bin -name redis-server 2>/dev/null | head -n 1)
is_installed="false"
version="未知"

if [ -n "$redis_path" ]; then
    is_installed="true"
    echo -e "Redis 已安装: ${GREEN}是${NC}"
    echo "位置: $redis_path"
    v_out=$($redis_path --version 2>&1)
    echo "$v_out"
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
else
    # 二进制文件未找到，但也检查 systemd 服务是否存在（包管理器可能安装到非标准路径）
    if systemctl list-unit-files 2>/dev/null | grep -qE "redis|redis-server"; then
        is_installed="true"
        echo -e "Redis 已安装: ${GREEN}是 (通过 systemd 服务发现)${NC}"
    else
        echo -e "Redis 已安装: ${RED}否${NC}"
    fi
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
check_port "$REDIS_PORT" "Redis" "redis"

# 4. systemd 服务状态
echo -e "${YELLOW}4. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    SERVICE_FOUND=false
    for svc in redis redis-server; do
        if systemctl is-active --quiet "$svc" 2>/dev/null; then
            echo -e "Redis 服务 ($svc): ${GREEN}active (running)${NC}"
            is_running="true"
            SERVICE_FOUND=true
            break
        elif systemctl list-unit-files 2>/dev/null | grep -q "${svc}.service"; then
            STATUS=$(systemctl is-active "$svc" 2>/dev/null || echo "unknown")
            echo -e "Redis 服务 ($svc): ${RED}$STATUS${NC}"
            SERVICE_FOUND=true
            break
        fi
    done
    if [ "$SERVICE_FOUND" = false ]; then
        echo -e "Redis 服务: ${RED}未发现 systemd 服务${NC}"
    fi
fi

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
echo "------------------------"