#!/bin/bash

#=============================================================================
# Elasticsearch 卸载脚本 (Ubuntu/Debian 优化版)

# 禁用 errexit 防止命令失败导致脚本提前退出
set +e

# 保存脚本自身 PID，避免误杀
SCRIPT_PID=$$
export SCRIPT_PID
# 用法: sudo bash uninstall_linux.sh [--keep-data]
#
# 支持卸载方式:
#   - tar.gz archive 解压安装 (/opt/elasticsearch)
#   - DEB/RPM 包管理器安装
#=============================================================================

# 解析命令行参数
KEEP_DATA=false
for arg in "$@"; do
    case "$arg" in
        --keep-data) KEEP_DATA=true ;;
        --no-keep-data) KEEP_DATA=false ;;
    esac
done

echo "========================================"
echo "      Elasticsearch 卸载脚本"
echo "========================================"
echo "保留数据: $KEEP_DATA"

# 检查 root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误: 请使用 root 权限运行此脚本"
    echo "INSTALLED: true"
    echo "RUNNING: false"
    exit 1
fi

# 安装目录变量 (与安装脚本保持一致)
ES_INSTALL_DIR="/opt/elasticsearch"
ES_DATA_DIR="/var/lib/elasticsearch"
ES_LOG_DIR="/var/log/elasticsearch"
ES_CONF_DIR="/etc/elasticsearch"
ES_RUN_DIR="/var/run/elasticsearch"

#=============================================================================
# 1. 停止 Elasticsearch 服务
#=============================================================================
echo ""
echo "[1/6] 停止 Elasticsearch 服务..."

# 先尝试通过 systemd 停止（如果存在服务）
if systemctl list-units --all --type=service 2>/dev/null | grep -qi "elasticsearch"; then
    echo "  通过 systemd 停止 ES 服务..."
    systemctl stop elasticsearch 2>/dev/null || true
    systemctl disable elasticsearch 2>/dev/null || true
    systemctl daemon-reload 2>/dev/null || true
    sleep 2
fi

# 尝试 service 命令（备选）
service elasticsearch stop 2>/dev/null || true

# 尝试通过 API 优雅关闭（带超时保护，不卡住）
echo "  尝试 API 关闭..."
timeout 15 curl -s --connect-timeout 2 --max-time 5 -X POST "http://localhost:9200/_shutdown?ignore_unavailable=true" 2>/dev/null || true
sleep 2

# 查找并终止 ES 相关进程
echo "  查找 ES 进程..."

# 查找包含 elasticsearch 关键词的进程，排除自身和 grep 进程
find_es_procs() {
    local pids=""
    # 方法1: pgrep 查找
    for pid in $(pgrep -f "elasticsearch" 2>/dev/null || true); do
        if [ "$pid" != "$SCRIPT_PID" ] && [ "$pid" != "$$" ]; then
            pids="$pids $pid"
        fi
    done
    # 方法2: 查找 java 进程中包含 elasticsearch 的
    for pid in $(ps aux 2>/dev/null | grep -E "[j]ava" | grep -v grep | awk '{print $2}'); do
        if [ "$pid" != "$SCRIPT_PID" ] && [ "$pid" != "$$" ]; then
            if [ -d "/proc/$pid" ]; then
                cmdline=$(cat /proc/$pid/cmdline 2>/dev/null | tr '\0' ' ')
                if echo "$cmdline" | grep -qi "elastic\|elasticsearch"; then
                    pids="$pids $pid"
                fi
            fi
        fi
    done
    echo "$pids"
}

# 温柔终止
ES_PIDS=$(find_es_procs)
if [ -n "$ES_PIDS" ]; then
    echo "  发现 ES 进程:$ES_PIDS"
    for pid in $ES_PIDS; do
        echo "  发送 SIGTERM 到 PID $pid"
        kill -15 "$pid" 2>/dev/null || true
    done
    echo "  等待进程退出..."
    sleep 5
fi

# 强制终止仍在运行的进程
ES_PIDS=$(find_es_procs)
if [ -n "$ES_PIDS" ]; then
    echo "  强制终止进程:$ES_PIDS"
    for pid in $ES_PIDS; do
        kill -9 "$pid" 2>/dev/null || true
    done
    sleep 2
fi

# 确认进程已停止
REMAINING=$(find_es_procs | wc -w || true)
if [ -z "$REMAINING" ] || [ "$REMAINING" -eq 0 ]; then
    echo "  [OK] Elasticsearch 已停止"
else
    echo "  [警告] 仍有 ES 进程残留"
fi

#=============================================================================
# 2. 卸载软件包
#=============================================================================
echo ""
echo "[2/6] 卸载软件包..."

# Debian/Ubuntu 系统 - DEB 包
ES_PKGS=$(dpkg -l 2>/dev/null | grep -E "^ii.*elasticsearch" | awk '{print $2}' | cut -d':' -f1 || true)

if [ -n "$ES_PKGS" ]; then
    echo "  发现 DEB 包: $ES_PKGS"
    for pkg in $ES_PKGS; do
        echo "  卸载包: $pkg"
        if [ "$KEEP_DATA" = true ]; then
            apt-get remove -y "$pkg" 2>/dev/null || dpkg -r "$pkg" 2>/dev/null || true
        else
            apt-get purge -y "$pkg" 2>/dev/null || dpkg -P "$pkg" 2>/dev/null || true
        fi
    done
    apt-get autoremove -y 2>/dev/null || true
else
    echo "  未发现 DEB 包"
fi

# RHEL/CentOS 系统 - RPM 包
if command -v rpm &>/dev/null; then
    ES_RPM_PKGS=$(rpm -qa 2>/dev/null | grep -i elasticsearch || true)
    if [ -n "$ES_RPM_PKGS" ]; then
        echo "  发现 RPM 包: $ES_RPM_PKGS"
        for pkg in $ES_RPM_PKGS; do
            echo "  卸载 RPM 包: $pkg"
            if [ "$KEEP_DATA" = true ]; then
                rpm -e --nodeps "$pkg" 2>/dev/null || yum remove -y "$pkg" 2>/dev/null || true
            else
                rpm -e --allmatches --nodeps "$pkg" 2>/dev/null || yum remove -y "$pkg" 2>/dev/null || true
            fi
        done
    else
        echo "  未发现 RPM 包"
    fi
fi

echo "软件包已处理"

#=============================================================================
# 3. 清理服务配置
#=============================================================================
echo ""
echo "[3/6] 清理服务配置..."

# 清理 systemd
systemctl daemon-reload 2>/dev/null || true
systemctl reset-failed 2>/dev/null || true
rm -f /etc/systemd/system/elasticsearch.service
rm -f /etc/systemd/system/multi-user.target.wants/elasticsearch.service
rm -f /etc/systemd/system/graphical.target.wants/elasticsearch.service

# 清理 init.d
rm -f /etc/init.d/elasticsearch
rm -f /etc/default/elasticsearch

echo "服务配置已清理"

#=============================================================================
# 4. 清理安装目录
#=============================================================================
echo ""
echo "[4/6] 清理安装目录..."

if [ "$KEEP_DATA" = true ]; then
    echo "  保留数据目录 (--keep-data)"
    rm -rf "$ES_INSTALL_DIR" 2>/dev/null || true
    rm -rf "$ES_CONF_DIR" 2>/dev/null || true
    rm -rf "$ES_RUN_DIR" 2>/dev/null || true
else
    for dir in "$ES_INSTALL_DIR" "$ES_DATA_DIR" "$ES_LOG_DIR" "$ES_CONF_DIR" "$ES_RUN_DIR"; do
        if [ -e "$dir" ]; then
            echo "  删除: $dir"
            rm -rf "$dir" 2>/dev/null || true
        fi
    done
fi

# 清理二进制
rm -f /usr/bin/elasticsearch 2>/dev/null || true
rm -f /usr/local/bin/elasticsearch 2>/dev/null || true

echo "安装目录已清理"

#=============================================================================
# 5. 清理用户和组
#=============================================================================
echo ""
echo "[5/6] 清理用户和组..."

# 终止 elasticsearch 用户的所有进程
for pid in $(ps -u elasticsearch 2>/dev/null | awk 'NR>1 {print $1}'); do
    kill -9 "$pid" 2>/dev/null || true
done

# 删除用户
if id -u elasticsearch &>/dev/null 2>&1; then
    userdel -r elasticsearch 2>/dev/null || userdel elasticsearch 2>/dev/null || true
    echo "  用户 elasticsearch 已删除"
fi

# 删除组
if getent group elasticsearch &>/dev/null 2>&1; then
    groupdel elasticsearch 2>/dev/null || true
    echo "  组 elasticsearch 已删除"
fi

# 清理系统配置
rm -f /etc/security/limits.d/elasticsearch.conf 2>/dev/null || true
rm -f /etc/sysctl.d/elasticsearch.conf 2>/dev/null || true
rm -f /etc/logrotate.d/elasticsearch 2>/dev/null || true
rm -f /etc/profile.d/elasticsearch.sh 2>/dev/null || true
rm -f /etc/tmpfiles.d/elasticsearch.conf 2>/dev/null || true

echo "用户和系统配置已清理"

#=============================================================================
# 6. 最终验证
#=============================================================================
echo ""
echo "[6/6] 最终验证..."

FAILED=0

# 检查进程（排除自身）
ES_PIDS=$(pgrep -f "elasticsearch" 2>/dev/null | grep -v "$SCRIPT_PID" || true)
if [ -n "$ES_PIDS" ]; then
    echo "  [警告] 仍有 ES 进程"
    echo "$ES_PIDS" | xargs -r ps -p 2>/dev/null | tail -n +2 || true
    FAILED=1
else
    echo "  [OK] 进程已停止"
fi

# 检查端口 (使用多种方法确保兼容性)
PORT_CHECK=$(ss -tuln 2>/dev/null | grep -E ":(9200|9201|9300)[[:space:]]" || true)
if [ -z "$PORT_CHECK" ] && command -v netstat &>/dev/null; then
    PORT_CHECK=$(netstat -tuln 2>/dev/null | grep -E ":(9200|9201|9300)[[:space:]]" || true)
fi
if [ -z "$PORT_CHECK" ] && command -v lsof &>/dev/null; then
    PORT_CHECK=$(lsof -i :9200 2>/dev/null || true)
fi

if [ -n "$PORT_CHECK" ]; then
    echo "  [警告] ES 端口仍在监听:"
    echo "$PORT_CHECK" | head -5
    FAILED=1
else
    echo "  [OK] ES 端口已释放"
fi

# 检查目录
if [ "$KEEP_DATA" != true ]; then
    if [ -d "$ES_INSTALL_DIR" ] || [ -d "/usr/share/elasticsearch" ]; then
        echo "  [警告] 安装目录仍存在"
        FAILED=1
    else
        echo "  [OK] 安装目录已清理"
    fi
fi

# 输出结果
echo ""
echo "========================================"
if [ "$FAILED" = 0 ]; then
    echo "  Elasticsearch 卸载成功!"
else
    echo "  Elasticsearch 卸载完成 (部分清理)"
fi
echo "========================================"

# 输出机器可读状态
echo ""
echo "INSTALLED: false"
echo "RUNNING: false"
echo "STATUS: $([ "$FAILED" = 0 ] && echo "SUCCESS" || echo "PARTIAL")"

exit 0
