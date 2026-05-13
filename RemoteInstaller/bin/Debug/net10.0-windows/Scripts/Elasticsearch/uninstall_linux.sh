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
ES_PACKAGE_DIR="/usr/share/elasticsearch"
ES_DATA_DIR="/var/lib/elasticsearch"
ES_LOG_DIR="/var/log/elasticsearch"
ES_CONF_DIR="/etc/elasticsearch"
ES_RUN_DIR="/var/run/elasticsearch"
ES_SERVICE_NAME="elasticsearch"
ES_SERVICE_FILES=(
    "/etc/systemd/system/${ES_SERVICE_NAME}.service"
    "/usr/lib/systemd/system/${ES_SERVICE_NAME}.service"
    "/lib/systemd/system/${ES_SERVICE_NAME}.service"
)
ES_EXTRA_PATHS=(
    "/etc/systemd/system/${ES_SERVICE_NAME}.service"
    "/etc/systemd/system/multi-user.target.wants/${ES_SERVICE_NAME}.service"
    "/etc/systemd/system/graphical.target.wants/${ES_SERVICE_NAME}.service"
    "/etc/init.d/${ES_SERVICE_NAME}"
    "/etc/default/${ES_SERVICE_NAME}"
    "/etc/security/limits.d/${ES_SERVICE_NAME}.conf"
    "/etc/sysctl.d/${ES_SERVICE_NAME}.conf"
    "/etc/logrotate.d/${ES_SERVICE_NAME}"
    "/etc/profile.d/${ES_SERVICE_NAME}.sh"
    "/etc/tmpfiles.d/${ES_SERVICE_NAME}.conf"
    "/usr/bin/${ES_SERVICE_NAME}"
    "/usr/local/bin/${ES_SERVICE_NAME}"
)
SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/elasticsearch.service"
    "/run/systemd/generator*/elasticsearch.service"
)
INIT_SCRIPTS=(
    "/etc/init.d/elasticsearch"
)

#=============================================================================
# 1. 停止 Elasticsearch 服务
#=============================================================================
echo ""
echo "[1/6] 停止 Elasticsearch 服务..."

service_exists=false
for service_file in "${ES_SERVICE_FILES[@]}"; do
    if [ -f "$service_file" ]; then
        service_exists=true
        break
    fi
done

if command -v systemctl >/dev/null 2>&1 && [ "$service_exists" = true ]; then
    echo "  通过 systemd 停止 ES 服务..."
    systemctl stop "$ES_SERVICE_NAME" 2>/dev/null || true
    systemctl disable "$ES_SERVICE_NAME" 2>/dev/null || true
    systemctl reset-failed "$ES_SERVICE_NAME" 2>/dev/null || true

    for i in 1 2 3 4 5; do
        service_state=$(systemctl is-active "$ES_SERVICE_NAME" 2>/dev/null || true)
        if [ "$service_state" = "inactive" ] || [ "$service_state" = "failed" ] || [ "$service_state" = "unknown" ] || [ -z "$service_state" ]; then
            break
        fi
        echo "  等待服务停止... ($i/5)"
        sleep 2
    done
fi

# 尝试 service 命令（备选）
service "$ES_SERVICE_NAME" stop 2>/dev/null || true
sleep 2

# 查找并终止 ES 相关进程
echo "  查找 ES 进程..."

find_es_procs() {
    local pids=""
    local pid
    local cmdline

    for pid in $(pgrep -f "elasticsearch|org\.elasticsearch\.bootstrap\.Elasticsearch|/usr/share/elasticsearch|/opt/elasticsearch" 2>/dev/null || true); do
        if [ "$pid" = "$SCRIPT_PID" ] || [ "$pid" = "$$" ]; then
            continue
        fi

        if [ -r "/proc/$pid/cmdline" ]; then
            cmdline=$(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null)
            if echo "$cmdline" | grep -Eqi "(/usr/share/elasticsearch|/opt/elasticsearch|org\.elasticsearch\.bootstrap\.Elasticsearch|[[:space:]/-]elasticsearch([[:space:]]|$))"; then
                case " $pids " in
                    *" $pid "*) ;;
                    *) pids="$pids $pid" ;;
                esac
            fi
        fi
    done

    echo "$pids"
}

ES_PIDS=$(find_es_procs)
if [ -n "$ES_PIDS" ]; then
    echo "  发现 ES 进程:$ES_PIDS"
    for pid in $ES_PIDS; do
        echo "  发送 SIGTERM 到 PID $pid"
        kill -15 "$pid" 2>/dev/null || true
    done

    for i in 1 2 3 4 5; do
        ES_PIDS=$(find_es_procs)
        if [ -z "$ES_PIDS" ]; then
            break
        fi
        echo "  等待进程退出... ($i/5)"
        sleep 2
    done
fi

ES_PIDS=$(find_es_procs)
if [ -n "$ES_PIDS" ]; then
    echo "  强制终止进程:$ES_PIDS"
    for pid in $ES_PIDS; do
        kill -9 "$pid" 2>/dev/null || true
    done

    for i in 1 2 3; do
        ES_PIDS=$(find_es_procs)
        if [ -z "$ES_PIDS" ]; then
            break
        fi
        echo "  等待强制终止生效... ($i/3)"
        sleep 1
    done
fi

REMAINING_PIDS=$(find_es_procs)
if [ -z "$REMAINING_PIDS" ]; then
    echo "  [OK] Elasticsearch 已停止"
else
    echo "  [警告] 仍有 ES 进程残留:$REMAINING_PIDS"
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
            DEBIAN_FRONTEND=noninteractive apt-get remove -y -qq "$pkg" 2>/dev/null || dpkg -r "$pkg" 2>/dev/null || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y -qq "$pkg" 2>/dev/null || dpkg -P "$pkg" 2>/dev/null || true
        fi
    done
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

# 清理 systemd 与额外残留
for path in "${ES_EXTRA_PATHS[@]}"; do
    rm -rf "$path" 2>/dev/null || true
done

for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for path in $pattern; do
        if [ -e "$path" ] || [ -L "$path" ]; then
            rm -f "$path" 2>/dev/null || true
        fi
    done
done

for init_script in "${INIT_SCRIPTS[@]}"; do
    service_name=$(basename "$init_script")
    if command -v update-rc.d >/dev/null 2>&1; then
        update-rc.d -f "$service_name" remove 2>/dev/null || true
    fi
    if command -v chkconfig >/dev/null 2>&1; then
        chkconfig --del "$service_name" 2>/dev/null || true
    fi
    rm -f "$init_script" 2>/dev/null || true
done

systemctl daemon-reload 2>/dev/null || true
systemctl reset-failed "$ES_SERVICE_NAME" 2>/dev/null || true
systemctl reset-failed 2>/dev/null || true

echo "服务配置已清理"

#=============================================================================
# 4. 清理安装目录
#=============================================================================
echo ""
echo "[4/6] 清理安装目录..."

if [ "$KEEP_DATA" = true ]; then
    echo "  保留数据目录 (--keep-data)"
    for dir in "$ES_INSTALL_DIR" "$ES_PACKAGE_DIR" "$ES_CONF_DIR" "$ES_RUN_DIR"; do
        if [ -e "$dir" ]; then
            echo "  删除: $dir"
            rm -rf "$dir" 2>/dev/null || true
        fi
    done
else
    for dir in "$ES_INSTALL_DIR" "$ES_PACKAGE_DIR" "$ES_DATA_DIR" "$ES_LOG_DIR" "$ES_CONF_DIR" "$ES_RUN_DIR"; do
        if [ -e "$dir" ]; then
            echo "  删除: $dir"
            rm -rf "$dir" 2>/dev/null || true
        fi
    done
fi

echo "安装目录已清理"

#=============================================================================
# 5. 清理用户和组
#=============================================================================
echo ""
echo "[5/6] 清理用户和组..."

# 终止 elasticsearch 用户的所有进程
if id -u elasticsearch >/dev/null 2>&1; then
    USER_PIDS=$(pgrep -u elasticsearch 2>/dev/null || true)
    if [ -n "$USER_PIDS" ]; then
        echo "  终止 elasticsearch 用户残留进程:$USER_PIDS"
        for pid in $USER_PIDS; do
            kill -9 "$pid" 2>/dev/null || true
        done
        sleep 1
    fi
fi

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

echo "用户和系统配置已清理"

#=============================================================================
# 6. 最终验证
#=============================================================================
echo ""
echo "[6/6] 最终验证..."

FAILED=0

# 检查进程（排除自身）
ES_PIDS=$(find_es_procs)
if [ -n "$ES_PIDS" ]; then
    echo "  [警告] 仍有 ES 进程"
    echo "$ES_PIDS" | xargs -r ps -p 2>/dev/null | tail -n +2 || true
    FAILED=1
else
    echo "  [OK] 进程已停止"
fi

# 检查服务
if command -v systemctl >/dev/null 2>&1; then
    SERVICE_STATE=$(systemctl is-active "$ES_SERVICE_NAME" 2>/dev/null || true)
    if [ "$SERVICE_STATE" = "active" ] || [ "$SERVICE_STATE" = "activating" ] || [ "$SERVICE_STATE" = "reloading" ]; then
        echo "  [警告] 服务仍处于活动状态: $SERVICE_STATE"
        FAILED=1
    else
        echo "  [OK] 服务未运行"
    fi

    if systemctl list-unit-files 2>/dev/null | grep -q "^${ES_SERVICE_NAME}\.service"; then
        echo "  [警告] systemd 服务定义仍存在"
        FAILED=1
    else
        echo "  [OK] systemd 服务定义已清理"
    fi
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
INSTALL_REMAINING=false
for dir in "$ES_INSTALL_DIR" "$ES_PACKAGE_DIR"; do
    if [ -e "$dir" ]; then
        INSTALL_REMAINING=true
        break
    fi
done

if [ "$INSTALL_REMAINING" = true ]; then
    echo "  [警告] 安装目录仍存在"
    FAILED=1
else
    echo "  [OK] 安装目录已清理"
fi

if [ "$KEEP_DATA" != true ]; then
    for dir in "$ES_CONF_DIR" "$ES_DATA_DIR" "$ES_LOG_DIR"; do
        if [ -e "$dir" ]; then
            echo "  [警告] 目录仍存在: $dir"
            FAILED=1
        fi
    done
fi

# 检查用户组
if id -u elasticsearch >/dev/null 2>&1; then
    echo "  [警告] 用户 elasticsearch 仍存在"
    FAILED=1
else
    echo "  [OK] 用户已清理"
fi

if getent group elasticsearch >/dev/null 2>&1; then
    echo "  [警告] 组 elasticsearch 仍存在"
    FAILED=1
else
    echo "  [OK] 组已清理"
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
