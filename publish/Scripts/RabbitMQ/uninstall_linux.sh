#!/bin/bash
set -e

# 参数
KEEP_DATA="${KEEP_DATA:-false}"

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo "========================================"
echo "      RabbitMQ 完全卸载脚本"
echo "========================================"
echo "保留数据模式：$KEEP_DATA"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

echo ""
echo "PROGRESS:Initializing:5"

# 1. 停止 RabbitMQ 服务
echo -e "${YELLOW}1. 停止 RabbitMQ 服务:${NC}"
echo "PROGRESS:StoppingService:10"

if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet rabbitmq-server 2>/dev/null; then
        echo "停止 rabbitmq-server 服务..."
        systemctl stop rabbitmq-server 2>/dev/null || true
        systemctl disable rabbitmq-server 2>/dev/null || true
        echo "服务已停止"
    else
        echo "服务未运行"
    fi
fi

# 使用 init.d 停止
if [ -f /etc/init.d/rabbitmq-server ]; then
    /etc/init.d/rabbitmq-server stop 2>/dev/null || true
fi

# 强制停止进程
echo "检查并停止 RabbitMQ 进程..."
for i in {1..3}; do
    beam_pids=$(pgrep -f "beam\.sm[p]" 2>/dev/null || true)
    if [ -z "$beam_pids" ]; then
        break
    fi
    echo "终止进程 (尝试 $i/3): $beam_pids"
    kill -9 $beam_pids 2>/dev/null || true
    sleep 2
done

# 最终确认
beam_pids=$(pgrep -f "beam\.sm[p]" 2>/dev/null || true)
if [ -n "$beam_pids" ]; then
    echo "警告：仍有残留进程无法终止"
else
    echo "所有 RabbitMQ 进程已终止"
fi

echo ""
echo "PROGRESS:RemovingPackages:20"

# 2. 卸载软件包
echo -e "${YELLOW}2. 卸载软件包:${NC}"

# Debian/Ubuntu
if command -v dpkg &> /dev/null && dpkg -l | grep -q '^ii.*rabbitmq'; then
    echo "检测到 Debian/Ubuntu 包..."
    DEBIAN_FRONTEND=noninteractive apt-get remove -y --purge rabbitmq-server 2>/dev/null || true
    DEBIAN_FRONTEND=noninteractive apt-get autoremove -y 2>/dev/null || true
    echo "RabbitMQ 包已卸载"
fi

# RedHat/CentOS
if command -v rpm &> /dev/null && rpm -qa | grep -q 'rabbitmq'; then
    echo "检测到 RedHat/CentOS 包..."
    rpm -e --nodeps rabbitmq-server 2>/dev/null || true
    yum remove -y rabbitmq-server 2>/dev/null || true
    echo "RabbitMQ 包已卸载"
fi

echo ""
echo "PROGRESS:CleaningFiles:40"

# 3. 删除安装目录和数据目录
echo -e "${YELLOW}3. 删除安装目录和数据目录:${NC}"

# 主要安装目录
INSTALL_DIRS=(
    "/opt/rabbitmq"
    "/usr/lib/rabbitmq"
    "/usr/lib64/rabbitmq"
    "/usr/local/rabbitmq"
)

for dir in "${INSTALL_DIRS[@]}"; do
    if [ -d "$dir" ]; then
        if [ "$KEEP_DATA" = "true" ]; then
            echo "保留数据模式：跳过 $dir"
        else
            rm -rf "$dir"
            echo "已删除：$dir"
        fi
    fi
done

# 数据目录
DATA_DIRS=(
    "/var/lib/rabbitmq"
    "/var/lib/rabbitmq/mnesia"
    "/var/lib/rabbitmq/.erlang.cookie"
)

for dir in "${DATA_DIRS[@]}"; do
    if [ -e "$dir" ]; then
        if [ "$KEEP_DATA" = "true" ]; then
            echo "保留数据模式：跳过 $dir"
        else
            rm -rf "$dir"
            echo "已删除：$dir"
        fi
    fi
done

# 日志目录
LOG_DIRS=(
    "/var/log/rabbitmq"
)

for dir in "${LOG_DIRS[@]}"; do
    if [ -d "$dir" ]; then
        rm -rf "$dir"
        echo "已删除日志目录：$dir"
    fi
done

# 配置文件目录
CONF_DIRS=(
    "/etc/rabbitmq"
    "/etc/rabbitmq/enabled_plugins"
)

for dir in "${CONF_DIRS[@]}"; do
    if [ -e "$dir" ]; then
        rm -rf "$dir"
        echo "已删除配置文件：$dir"
    fi
done

# Erlang cookie
if [ -f "/var/lib/rabbitmq/.erlang.cookie" ]; then
    rm -f "/var/lib/rabbitmq/.erlang.cookie"
    echo "已删除 Erlang cookie"
fi

# 全局 Erlang cookie
if [ -f "/etc/rabbitmq/.erlang.cookie" ]; then
    rm -f "/etc/rabbitmq/.erlang.cookie"
    echo "已删除全局 Erlang cookie"
fi

echo ""
echo "PROGRESS:CleaningUsers:50"

# 4. 删除用户和组
echo -e "${YELLOW}4. 删除用户和组:${NC}"

if id -u rabbitmq >/dev/null 2>&1; then
    userdel -r rabbitmq 2>/dev/null || true
    echo "已删除 rabbitmq 用户"
fi

if getent group rabbitmq >/dev/null 2>&1; then
    groupdel rabbitmq 2>/dev/null || true
    echo "已删除 rabbitmq 组"
fi

echo ""
echo "PROGRESS:CleaningFirewall:60"

# 5. 清理防火墙规则
echo -e "${YELLOW}5. 清理防火墙规则:${NC}"

# ufw
if command -v ufw &> /dev/null; then
    ufw delete allow 5672/tcp 2>/dev/null || true
    ufw delete allow 15672/tcp 2>/dev/null || true
    ufw delete allow 25672/tcp 2>/dev/null || true
    echo "已清理 ufw 防火墙规则"
fi

# firewalld
if command -v firewall-cmd &> /dev/null; then
    firewall-cmd --permanent --remove-port=5672/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=15672/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=25672/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
    echo "已清理 firewalld 防火墙规则"
fi

# iptables
if command -v iptables &> /dev/null; then
    iptables -D INPUT -p tcp --dport 5672 -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p tcp --dport 15672 -j ACCEPT 2>/dev/null || true
    iptables -D INPUT -p tcp --dport 25672 -j ACCEPT 2>/dev/null || true
    if command -v iptables-save &> /dev/null && [ -f /etc/iptables.rules ]; then
        iptables-save > /etc/iptables.rules 2>/dev/null || true
    fi
    echo "已清理 iptables 防火墙规则"
fi

echo ""
echo "PROGRESS:CleaningService:70"

# 6. 清理 systemd 服务
echo -e "${YELLOW}6. 清理 systemd 服务:${NC}"

if command -v systemctl &> /dev/null; then
    # 删除服务文件
    SERVICE_FILES=(
        "/etc/systemd/system/rabbitmq-server.service"
        "/etc/systemd/system/rabbitmq.service"
        "/usr/lib/systemd/system/rabbitmq-server.service"
        "/usr/lib/systemd/system/rabbitmq.service"
        "/lib/systemd/system/rabbitmq-server.service"
        "/lib/systemd/system/rabbitmq.service"
    )

    for service_file in "${SERVICE_FILES[@]}"; do
        if [ -f "$service_file" ]; then
            rm -f "$service_file"
            echo "已删除服务文件：$service_file"
        fi
    done

    systemctl daemon-reload 2>/dev/null || true
    echo "systemd 服务已清理"
fi

# 清理 init.d 服务
if [ -f /etc/init.d/rabbitmq-server ]; then
    rm -f /etc/init.d/rabbitmq-server
    echo "已删除 init.d 服务"
fi

if [ -f /etc/init.d/rabbitmq ]; then
    rm -f /etc/init.d/rabbitmq
    echo "已删除 init.d 服务"
fi

# 清理 rc 链接
find /etc/rc*.d -name "S*rabbitmq*" -delete 2>/dev/null || true
find /etc/rc*.d -name "K*rabbitmq*" -delete 2>/dev/null || true
echo "已清理 rc 链接"

echo ""
echo "PROGRESS:CleaningEnv:80"

# 7. 清理环境变量
echo -e "${YELLOW}7. 清理环境变量:${NC}"

# 从 profile 文件中清理
PROFILE_FILES=(
    "/etc/profile.d/rabbitmq.sh"
    "/etc/profile"
    "/etc/bash.bashrc"
    "/etc/bashrc"
)

for profile in "${PROFILE_FILES[@]}"; do
    if [ -f "$profile" ]; then
        if grep -q "rabbitmq" "$profile" 2>/dev/null; then
            sed -i '/rabbitmq/d' "$profile" 2>/dev/null || true
            echo "已清理：$profile"
        fi
    fi
done

# 清理 PATH
if [ -f /etc/environment ]; then
    sed -i '/rabbitmq/d' /etc/environment 2>/dev/null || true
fi

echo ""
echo "PROGRESS:CleaningPackages:90"

# 8. 清理 Erlang（可选，如果单独安装的）
echo -e "${YELLOW}8. 清理 Erlang 依赖:${NC}"
echo "提示：Erlang 可能与其他应用共享，如需完全卸载请手动执行:"
echo "  Debian/Ubuntu: apt-get remove --purge erlang-nox erlang-base"
echo "  RedHat/CentOS: yum remove erlang"

echo ""
echo "PROGRESS:CleaningPackages:95"

# 9. 清理残留的 GPG 密钥和仓库配置
echo -e "${YELLOW}9. 清理仓库配置:${NC}"

# 删除仓库配置
REPO_FILES=(
    "/etc/apt/sources.list.d/erlang.list"
    "/etc/apt/sources.list.d/rabbitmq.list"
    "/etc/yum.repos.d/rabbitmq.repo"
    "/etc/zypp/repos.d/rabbitmq.repo"
)

for repo in "${REPO_FILES[@]}"; do
    if [ -f "$repo" ]; then
        rm -f "$repo"
        echo "已删除仓库配置：$repo"
    fi
done

# 删除 GPG 密钥
GPG_KEYS=(
    "/usr/share/keyrings/erlang-archive-keyring.gpg"
    "/usr/share/keyrings/rabbitmq-archive-keyring.gpg"
    "/etc/pki/rpm-gpg/rabbitmq-gpg-key"
)

for key in "${GPG_KEYS[@]}"; do
    if [ -f "$key" ]; then
        rm -f "$key"
        echo "已删除 GPG 密钥：$key"
    fi
done

# 最终验证
echo ""
echo "========================================"
echo "      RabbitMQ 完全卸载完成！"
echo "========================================"

echo ""
echo -e "${YELLOW}最终验证:${NC}"

FAILED=0

# 检查进程（最多重试3次）
for retry in 1 2 3; do
    beam_pids=$(pgrep -f "beam\.sm[p]" 2>/dev/null || true)
    if [ -z "$beam_pids" ]; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 RabbitMQ 进程残留，强制终止后重试 ($retry/3)..."
        kill -9 $beam_pids 2>/dev/null || true
        sleep 2
    fi
done

beam_pids=$(pgrep -f "beam\.sm[p]" 2>/dev/null || true)
if [ -n "$beam_pids" ]; then
    echo -e "${RED}警告：仍有 RabbitMQ 进程运行${NC}"
    FAILED=1
else
    echo -e "${GREEN}RabbitMQ 进程：已清理${NC}"
fi

# 检查服务
if command -v systemctl &> /dev/null; then
    if systemctl list-units --type=service 2>/dev/null | grep -qi rabbitmq; then
        echo -e "${YELLOW}警告：仍有 RabbitMQ 服务配置${NC}"
        FAILED=1
    else
        echo -e "${GREEN}RabbitMQ 服务：已清理${NC}"
    fi
fi

# 检查包
if command -v dpkg &> /dev/null && dpkg -l 2>/dev/null | grep -q '^ii.*rabbitmq'; then
    echo -e "${RED}警告：仍有 RabbitMQ 包未完全卸载${NC}"
    FAILED=1
elif command -v rpm &> /dev/null && rpm -qa 2>/dev/null | grep -q 'rabbitmq'; then
    echo -e "${RED}警告：仍有 RabbitMQ 包未完全卸载${NC}"
    FAILED=1
else
    echo -e "${GREEN}RabbitMQ 包：已卸载${NC}"
fi

# 检查端口 5672 和 15672
if command -v ss &> /dev/null; then
    if ss -tuln 2>/dev/null | grep -E ':(5672|15672)[[:space:]]' | grep -q .; then
        echo -e "${RED}警告：RabbitMQ 端口（5672/15672）仍在监听${NC}"
        FAILED=1
    else
        echo -e "${GREEN}RabbitMQ 端口（5672/15672）：已释放${NC}"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln 2>/dev/null | grep -E ':(5672|15672)[[:space:]]' | grep -q .; then
        echo -e "${RED}警告：RabbitMQ 端口（5672/15672）仍在监听${NC}"
        FAILED=1
    else
        echo -e "${GREEN}RabbitMQ 端口（5672/15672）：已释放${NC}"
    fi
fi

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: false"
echo "VERSION: 已卸载"
echo "RUNNING: false"
echo "PORT: 5672,15672"
if [ "$FAILED" = 0 ]; then
    echo "STAGE:SUCCESS"
else
    echo "STAGE:PARTIAL"
fi
echo "------------------------"
