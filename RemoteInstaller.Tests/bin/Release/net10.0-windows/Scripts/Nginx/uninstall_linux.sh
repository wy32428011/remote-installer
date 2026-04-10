#!/bin/bash
set -e

# 参数定义
KEEP_DATA=false
if [ "$1" == "--keep-data" ] || [ "${KEEP_DATA:-false}" == "true" ]; then
    KEEP_DATA=true
fi

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "PROGRESS:Initializing:5"
echo -e "${YELLOW}========================================${NC}"
echo -e "${YELLOW}      Nginx 完全卸载脚本${NC}"
echo -e "${YELLOW}========================================${NC}"
echo "保留数据模式：$KEEP_DATA"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}错误：请使用 root 权限运行此脚本${NC}"
    exit 1
fi

echo "PROGRESS:Stopping:15"
# 1. 停止服务
echo -e "${YELLOW}1. 停止 Nginx 服务...${NC}"

# 尝试多种方式停止 Nginx
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nginx 2>/dev/null; then
        echo "通过 systemctl 停止 nginx..."
        systemctl stop nginx || true
        systemctl disable nginx || true
    fi
fi

# 使用 nginx -s stop 停止
if command -v nginx &> /dev/null; then
    echo "通过 nginx 命令停止..."
    nginx -s stop 2>/dev/null || true
    # 如果配置文件在自定义位置，尝试停止
    nginx -c /etc/nginx/nginx.conf -s stop 2>/dev/null || true
    nginx -c /usr/local/nginx/conf/nginx.conf -s stop 2>/dev/null || true
fi

# 确保进程已关闭
echo "检查并清理残留进程..."
for i in 1 2 3; do
    if pgrep -x nginx > /dev/null 2>&1; then
        echo "发现 Nginx 进程，尝试优雅关闭 (尝试 $i/3)..."
        pkill nginx || true
        sleep 2
    else
        break
    fi
done

# 强制终止任何残留进程
if pgrep -x nginx > /dev/null 2>&1; then
    echo "强制终止残留 Nginx 进程..."
    pkill -9 nginx || true
    sleep 1
fi

# 再次确认
if pgrep -x nginx > /dev/null 2>&1; then
    echo -e "${YELLOW}警告：仍有 Nginx 进程无法终止${NC}"
else
    echo -e "${GREEN}所有 Nginx 进程已停止${NC}"
fi

echo "PROGRESS:Uninstalling:35"
# 2. 卸载软件包 (包管理器)
echo -e "${YELLOW}2. 卸载软件包...${NC}"

OS_TYPE="unknown"
if [ -f /etc/debian_version ]; then
    OS_TYPE="debian"
    echo "检测到 Debian/Ubuntu 系统"
    if [ "$KEEP_DATA" = true ]; then
        echo "执行 remove (保留配置)..."
        DEBIAN_FRONTEND=noninteractive apt-get remove -y nginx nginx-common nginx-full 2>/dev/null || true
    else
        echo "执行 purge (删除配置)..."
        DEBIAN_FRONTEND=noninteractive apt-get purge -y nginx nginx-common nginx-full 2>/dev/null || true
        echo "清理自动安装的依赖..."
        apt-get autoremove -y 2>/dev/null || true
        apt-get autoclean 2>/dev/null || true
    fi
    # 清理 apt 缓存
    rm -rf /var/cache/apt/archives/nginx* 2>/dev/null || true
elif [ -f /etc/redhat-release ]; then
    OS_TYPE="redhat"
    echo "检测到 CentOS/RedHat 系统"
    yum remove -y nginx 2>/dev/null || true
    yum clean all 2>/dev/null || true
elif command -v dnf &> /dev/null && dnf list installed nginx 2>/dev/null | grep -q nginx; then
    echo "检测到 Fedora/RHEL 8+ 系统"
    dnf remove -y nginx 2>/dev/null || true
    dnf clean all 2>/dev/null || true
elif command -v pacman &> /dev/null && pacman -Q nginx &>/dev/null; then
    echo "检测到 Arch Linux 系统"
    pacman -R --noconfirm nginx 2>/dev/null || true
elif command -v zypper &> /dev/null && zypper search --installed-only nginx 2>/dev/null | grep -q '^i'; then
    echo "检测到 openSUSE/SLES 系统"
    zypper remove -y nginx 2>/dev/null || true
    zypper clean 2>/dev/null || true
fi

echo "PROGRESS:Cleaning:60"
# 3. 彻底清理所有残留文件和目录
echo -e "${YELLOW}3. 清理残留文件和目录...${NC}"

# 定义所有可能的 Nginx 路径
declare -a CONFIG_DIRS=(
    "/etc/nginx"
    "/etc/nginx.conf"
    "/usr/local/nginx/conf"
    "/opt/nginx/conf"
)

declare -a LOG_DIRS=(
    "/var/log/nginx"
    "/var/log/nginx.log"
    "/usr/local/nginx/logs"
    "/opt/nginx/logs"
)

declare -a DATA_DIRS=(
    "/var/lib/nginx"
    "/var/cache/nginx"
    "/usr/local/nginx/html"
    "/usr/local/nginx/client_body_temp"
    "/opt/nginx"
)

declare -a BINARY_PATHS=(
    "/usr/sbin/nginx"
    "/usr/local/sbin/nginx"
    "/usr/bin/nginx"
    "/usr/local/bin/nginx"
    "/opt/nginx/sbin/nginx"
)

declare -a SERVICE_PATHS=(
    "/etc/systemd/system/nginx.service"
    "/lib/systemd/system/nginx.service"
    "/etc/init.d/nginx"
    "/etc/rc.d/init.d/nginx"
)

declare -a SSL_CERT_PATHS=(
    "/etc/nginx/ssl"
    "/etc/ssl/nginx"
)

# 清理配置文件
echo "清理配置文件..."
for path in "${CONFIG_DIRS[@]}"; do
    if [ -e "$path" ]; then
        if [ "$KEEP_DATA" = false ] || [[ "$path" != *"conf"* ]]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    fi
done

# 清理日志文件
if [ "$KEEP_DATA" = false ]; then
    echo "清理日志文件..."
    for path in "${LOG_DIRS[@]}"; do
        if [ -e "$path" ]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    done
fi

# 清理数据目录
if [ "$KEEP_DATA" = false ]; then
    echo "清理数据目录..."
    for path in "${DATA_DIRS[@]}"; do
        if [ -e "$path" ]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    done
fi

# 清理二进制文件
echo "清理二进制文件..."
for path in "${BINARY_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -f "$path"
    fi
done

# 清理服务单元
echo "清理服务单元..."
for path in "${SERVICE_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -f "$path"
    fi
done

# 清理 SSL 证书
if [ "$KEEP_DATA" = false ]; then
    for path in "${SSL_CERT_PATHS[@]}"; do
        if [ -e "$path" ]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    done
fi

# 清理用户和组 (如果存在)
echo "清理系统用户..."
if id nginx &>/dev/null; then
    echo "  删除用户 nginx"
    userdel -r nginx 2>/dev/null || userdel nginx 2>/dev/null || true
fi
if getent group nginx &>/dev/null; then
    echo "  删除组 nginx"
    groupdel nginx 2>/dev/null || true
fi

# 清理 nginx 临时文件配置
rm -f /etc/tmpfiles.d/nginx.conf 2>/dev/null || true

echo "PROGRESS:Finalizing:90"
# 4. 刷新系统服务
echo -e "${YELLOW}4. 刷新系统服务...${NC}"
if command -v systemctl &> /dev/null; then
    systemctl daemon-reload || true
    systemctl reset-failed || true
fi

# 重新加载防火墙配置 (如果配置了 nginx 规则)
if command -v firewall-cmd &> /dev/null; then
    firewall-cmd --permanent --remove-service=http 2>/dev/null || true
    firewall-cmd --permanent --remove-service=https 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw &> /dev/null; then
    ufw delete allow 'Nginx Full' 2>/dev/null || true
    ufw delete allow 'Nginx HTTP' 2>/dev/null || true
    ufw delete allow 'Nginx HTTPS' 2>/dev/null || true
fi

echo "PROGRESS:Complete:100"
echo -e "${YELLOW}========================================${NC}"
echo -e "${GREEN}      Nginx 完全卸载完成！${NC}"
echo -e "${YELLOW}========================================${NC}"

# 最终验证
echo ""
echo -e "${YELLOW}最终验证:${NC}"

FAILED=0

# 验证进程（最多重试3次）
for retry in 1 2 3; do
    if ! pgrep -x nginx > /dev/null 2>&1; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 Nginx 进程残留，强制终止后重试 ($retry/3)..."
        pkill -9 nginx 2>/dev/null || true
        sleep 2
    fi
done

if pgrep -x nginx > /dev/null 2>&1; then
    echo -e "${RED}警告：仍有 Nginx 进程运行${NC}"
    FAILED=1
else
    echo -e "${GREEN}Nginx 进程：已停止${NC}"
fi

# 验证 nginx 命令
if command -v nginx &> /dev/null; then
    echo -e "${RED}警告：nginx 命令仍存在${NC}"
    FAILED=1
else
    echo -e "${GREEN}nginx 命令已清理${NC}"
fi

# 验证配置目录
if [ -d /etc/nginx ]; then
    echo -e "${RED}警告：/etc/nginx 目录仍存在${NC}"
    FAILED=1
else
    echo -e "${GREEN}/etc/nginx 目录已清理${NC}"
fi

# 验证端口 80
if command -v ss &> /dev/null; then
    if ss -tuln 2>/dev/null | grep -q ':80[[:space:]]'; then
        echo -e "${RED}警告：端口 80 仍在监听${NC}"
        FAILED=1
    else
        echo -e "${GREEN}端口 80：已释放${NC}"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln 2>/dev/null | grep -q ':80[[:space:]]'; then
        echo -e "${RED}警告：端口 80 仍在监听${NC}"
        FAILED=1
    else
        echo -e "${GREEN}端口 80：已释放${NC}"
    fi
fi

# 输出机器可读的状态信息
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
