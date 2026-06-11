
#!/bin/bash
set -e

# 解析命令行参数
KEEP_DATA=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --keep-data)
            KEEP_DATA=true
            shift
            ;;
        --no-keep-data)
            KEEP_DATA=false
            shift
            ;;
        *)
            shift
            ;;
    esac
done

# 日志设置 - 在远程执行模式下，建议直接输出到 stdout/stderr
# 由调用者（如 InstallerService）负责捕获日志
# LOG_FILE="uninstall.log"
# exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "Redis 卸载脚本开始..."
echo "保留数据模式: $KEEP_DATA"
echo "当前工作目录: $(pwd)"

# 0. 检查 Root 权限
if [ "$(id -u)" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 检查 OS
if [ -f /etc/debian_version ]; then
    OS="Debian"
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    OS="Unknown"
fi
echo "检测到操作系统: $OS"

# 读取 Redis 实际端口
REDIS_PORT=6379
CONF_FILES=("/etc/redis/redis.conf" "/etc/redis.conf" "/etc/redis-server/redis.conf" "/usr/local/etc/redis.conf")
for conf in "${CONF_FILES[@]}"; do
    if [ -f "$conf" ]; then
        EXTRACTED_PORT=$(grep -E '^[[:space:]]*port[[:space:]]+[0-9]+' "$conf" 2>/dev/null | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
        if [ -n "$EXTRACTED_PORT" ]; then
            REDIS_PORT=$EXTRACTED_PORT
        fi
        break
    fi
done

echo "PROGRESS:Stopping:20"
# 1. 停止服务
echo "正在停止 Redis 服务..."
for service in redis redis-server redis_6379 redis-sentinel; do
    if systemctl list-unit-files 2>/dev/null | grep -q "${service}.service"; then
        echo "  停止并禁用服务: $service"
        systemctl stop "$service" 2>/dev/null || true
        systemctl disable "$service" 2>/dev/null || true
    fi
done

# 确保进程已关闭
echo "检查残留进程..."
if pgrep -x redis-server > /dev/null 2>&1; then
    REDIS_PIDS=$(pgrep -x redis-server)
    echo "发现残留进程 (PID: $REDIS_PIDS)，正在强制停止..."
    pkill -9 -x redis-server 2>/dev/null || true
    sleep 2
fi

# 再次检查
if pgrep -x redis-server > /dev/null 2>&1; then
    echo "警告：仍有 Redis 进程运行，逐个终止..."
    for pid in $(pgrep -x redis-server); do
        kill -9 "$pid" 2>/dev/null || true
    done
    sleep 1
fi

# 检查 Sentinel 进程
if pgrep -x redis-sentinel > /dev/null 2>&1; then
    echo "发现 Redis Sentinel 进程，正在停止..."
    pkill -9 -x redis-sentinel 2>/dev/null || true
fi

echo "PROGRESS:Uninstalling:40"
# 2. 卸载软件包
echo "正在通过包管理器卸载 Redis..."
if [ "$OS" = "Debian" ]; then
    INSTALLED_PKGS=$(dpkg -l 2>/dev/null | grep -iE 'redis' | awk '{print $2}' || true)
    if [ -n "$INSTALLED_PKGS" ]; then
        echo "发现已安装的包: $INSTALLED_PKGS"
        if [ "$KEEP_DATA" = true ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y $INSTALLED_PKGS || dpkg --remove --force-all $INSTALLED_PKGS || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y $INSTALLED_PKGS || dpkg --purge --force-all $INSTALLED_PKGS || true
        fi
    else
        echo "未发现通过 apt 安装的 Redis 包"
    fi
elif [ "$OS" = "RedHat" ]; then
    INSTALLED_PKGS=$(rpm -qa 2>/dev/null | grep -iE 'redis' || true)
    if [ -n "$INSTALLED_PKGS" ]; then
        echo "发现已安装的包: $INSTALLED_PKGS"
        yum remove -y $INSTALLED_PKGS || true
    else
        echo "未发现通过 yum 安装的 Redis 包"
    fi
fi

echo "PROGRESS:Cleaning:60"
# 3. 清理二进制文件（涵盖源码安装路径）
echo "正在清理残留二进制文件..."
BINARY_FILES=(
    "/usr/local/bin/redis-server"
    "/usr/local/bin/redis-cli"
    "/usr/local/bin/redis-benchmark"
    "/usr/local/bin/redis-check-aof"
    "/usr/local/bin/redis-check-rdb"
    "/usr/local/bin/redis-sentinel"
    "/usr/bin/redis-server"
    "/usr/bin/redis-cli"
    "/usr/bin/redis-benchmark"
    "/usr/bin/redis-check-aof"
    "/usr/bin/redis-check-rdb"
    "/usr/bin/redis-sentinel"
)
for file in "${BINARY_FILES[@]}"; do
    if [ -f "$file" ]; then
        echo "  删除: $file"
        rm -f "$file"
    fi
done

# 4. 清理 systemd 服务单元
echo "清理 systemd 服务文件..."
SERVICE_FILES=(
    "/etc/systemd/system/redis.service"
    "/etc/systemd/system/redis-server.service"
    "/etc/systemd/system/redis-sentinel.service"
    "/lib/systemd/system/redis.service"
    "/lib/systemd/system/redis-server.service"
    "/usr/lib/systemd/system/redis.service"
    "/usr/lib/systemd/system/redis-server.service"
)
for file in "${SERVICE_FILES[@]}"; do
    if [ -f "$file" ]; then
        echo "  删除: $file"
        rm -f "$file"
    fi
done

SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/redis.service"
    "/etc/systemd/system/*.wants/redis-server.service"
    "/etc/systemd/system/*.wants/redis-sentinel.service"
    "/run/systemd/generator*/redis.service"
    "/run/systemd/generator*/redis-server.service"
    "/run/systemd/generator*/redis-sentinel.service"
)
for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for file in $pattern; do
        if [ -e "$file" ] || [ -L "$file" ]; then
            echo "  删除: $file"
            rm -f "$file"
        fi
    done
done

# Debian/Ubuntu 可能通过 SysV init 脚本生成 redis-server.service
echo "清理 SysV init 脚本..."
INIT_SCRIPTS=(
    "/etc/init.d/redis"
    "/etc/init.d/redis-server"
    "/etc/init.d/redis-sentinel"
)
for init_script in "${INIT_SCRIPTS[@]}"; do
    service_name=$(basename "$init_script")
    if command -v update-rc.d >/dev/null 2>&1; then
        update-rc.d -f "$service_name" remove 2>/dev/null || true
    fi
    if command -v chkconfig >/dev/null 2>&1; then
        chkconfig --del "$service_name" 2>/dev/null || true
    fi
    if [ -e "$init_script" ] || [ -L "$init_script" ]; then
        echo "  删除: $init_script"
        rm -f "$init_script"
    fi
done

echo "PROGRESS:Cleaning:80"

# 5. 清理配置、日志、数据
if [ "$KEEP_DATA" = false ]; then
    echo "清理配置文件..."
    for path in /etc/redis /etc/redis.conf /usr/local/etc/redis /usr/local/etc/redis.conf; do
        if [ -e "$path" ]; then
            echo "  删除: $path"
            rm -rf "$path"
        fi
    done
    
    echo "清理数据目录..."
    for path in /var/lib/redis /usr/local/var/db/redis; do
        if [ -d "$path" ]; then
            echo "  删除: $path"
            rm -rf "$path"
        fi
    done
else
    echo "保留数据模式：跳过配置和数据目录清理"
fi

echo "清理日志文件..."
for path in /var/log/redis /var/log/redis.log /var/log/redis-server.log; do
    if [ -e "$path" ]; then
        echo "  删除: $path"
        rm -rf "$path"
    fi
done

echo "清理运行时目录..."
rm -rf /var/run/redis 2>/dev/null || true
rm -f /var/run/redis.pid /var/run/redis-server.pid 2>/dev/null || true
rm -rf /run/redis 2>/dev/null || true

# 6. 清理 redis 用户（仅非保留数据模式）
if [ "$KEEP_DATA" = false ]; then
    if id -u redis >/dev/null 2>&1; then
        if ! pgrep -u redis > /dev/null 2>&1; then
            echo "删除 redis 系统用户..."
            userdel redis 2>/dev/null || true
            groupdel redis 2>/dev/null || true
        else
            echo "redis 用户仍有进程运行，跳过用户删除"
        fi
    fi
fi

# 7. 刷新系统服务
echo "刷新 systemd 配置..."
systemctl daemon-reload
systemctl reset-failed 2>/dev/null || true

echo "PROGRESS:Complete:100"
echo "Redis 卸载完成！"

# 最终验证
echo ""
echo "最终验证..."

FAILED=0

# 验证进程（最多重试3次）
for retry in 1 2 3; do
    if ! pgrep -x redis-server > /dev/null 2>&1 && ! pgrep -x redis-sentinel > /dev/null 2>&1; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 Redis 进程残留，强制终止后重试 ($retry/3)..."
        pkill -9 redis-server 2>/dev/null || true
        pkill -9 redis-sentinel 2>/dev/null || true
        sleep 2
    fi
done

if pgrep -x redis-server > /dev/null 2>&1 || pgrep -x redis-sentinel > /dev/null 2>&1; then
    echo "警告：仍有 Redis 进程运行"
    FAILED=1
else
    echo "Redis 进程：已停止"
fi

# 验证实际端口
if command -v ss &> /dev/null; then
    if ss -tuln 2>/dev/null | grep -q ":${REDIS_PORT}[[:space:]]"; then
        echo "警告：端口 ${REDIS_PORT} 仍在监听"
        FAILED=1
    else
        echo "端口 ${REDIS_PORT}：已释放"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln 2>/dev/null | grep -q ":${REDIS_PORT}[[:space:]]"; then
        echo "警告：端口 ${REDIS_PORT} 仍在监听"
        FAILED=1
    else
        echo "端口 ${REDIS_PORT}：已释放"
    fi
fi

# 验证 systemd 服务文件
if systemctl list-unit-files 2>/dev/null | grep -qE '^(redis|redis-server|redis-sentinel)\.service'; then
    echo "警告：Redis systemd 服务定义仍存在"
    FAILED=1
else
    echo "Redis 服务定义：已清理"
fi

# 验证关键目录
if [ "$KEEP_DATA" = false ]; then
    for path in /etc/redis /etc/redis.conf /usr/local/etc/redis /usr/local/etc/redis.conf /var/lib/redis /var/log/redis; do
        if [ -e "$path" ]; then
            echo "警告：目录或文件仍存在: $path"
            FAILED=1
        fi
    done
fi

# 验证 redis 命令
if command -v redis-server &> /dev/null; then
    echo "警告：redis-server 命令仍存在"
    FAILED=1
else
    echo "redis-server 命令：已清理"
fi

# 输出机器可读的状态信息 (供 InstallerService 解析)
echo ""
echo "--- MACHINE READABLE ---"
if [ "$FAILED" = 0 ]; then
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "STAGE:SUCCESS"
else
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "STAGE:PARTIAL"
fi
echo "------------------------"
