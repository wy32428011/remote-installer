#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo "========================================"
echo "      RabbitMQ 状态检测脚本"
echo "========================================"

is_installed="false"
is_running="false"
version="未知"
package_installed="false"
binary_found="false"
process_found="false"
service_found="false"
service_active="false"
service_only_stale="false"
config_only_residue="false"
management_plugin_enabled="false"
management_http_ready="false"
remote_access_available="false"
amqp_open="false"
management_open="false"
amqp_bind_all="false"
mgmt_bind_all="false"
port_listening="false"
SERVICE_NAME="unknown"
SERVICE_STATUS="not-found"
RABBITMQ_PID=""
RABBITMQ_CMD=""

AMQP_PORT=5672
MANAGEMENT_PORT=15672

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

is_rabbitmq_command_line() {
    local command_line="$1"
    printf '%s' "$command_line" | grep -Eiq 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot'
}

find_rabbitmq_process() {
    local pid
    local command_line

    command_exists pgrep || return 1

    while IFS= read -r pid; do
        [ -n "$pid" ] || continue
        command_line="$(ps -p "$pid" -o args= 2>/dev/null || true)"
        if [ -n "$command_line" ] && is_rabbitmq_command_line "$command_line"; then
            RABBITMQ_PID="$pid"
            RABBITMQ_CMD="$command_line"
            return 0
        fi
    done < <(pgrep -f '[b]eam\.smp|[e]rl' 2>/dev/null || true)

    return 1
}

get_listener_result() {
    local port="$1"

    if command_exists ss; then
        ss -tlnp 2>/dev/null | grep -E "[:.]${port}[[:space:]]" || true
    elif command_exists netstat; then
        netstat -tlnp 2>/dev/null | grep -E "[:.]${port}[[:space:]]" || true
    fi
}

listener_is_bound_all() {
    local listener_result="$1"
    local port="$2"

    printf '%s\n' "$listener_result" | grep -Eq "0\.0\.0\.0:${port}|\\*:${port}|\\[::\\]:${port}|:::${port}"
}

listener_is_rabbitmq_owned() {
    local listener_result="$1"

    [ -n "$listener_result" ] || return 1

    if printf '%s\n' "$listener_result" | grep -Eiq 'rabbitmq|rabbit'; then
        return 0
    fi

    if [ -n "$RABBITMQ_PID" ] && printf '%s\n' "$listener_result" | grep -Eq "(pid=${RABBITMQ_PID},|${RABBITMQ_PID}/)"; then
        return 0
    fi

    return 1
}

detect_service_state() {
    local candidate
    local detected_status

    command_exists systemctl || return 0

    for candidate in rabbitmq-server rabbitmq; do
        if systemctl is-active --quiet "$candidate" 2>/dev/null; then
            SERVICE_NAME="$candidate"
            SERVICE_STATUS="active"
            service_found="true"
            service_active="true"
            return 0
        fi
    done

    for candidate in rabbitmq-server rabbitmq; do
        if systemctl list-unit-files 2>/dev/null | grep -q "^${candidate}\\.service" || \
           systemctl list-units --all --type=service 2>/dev/null | grep -Eq "^\\s*${candidate}\\.service"; then
            SERVICE_NAME="$candidate"
            detected_status="$(systemctl is-active "$candidate" 2>/dev/null || true)"
            SERVICE_STATUS="${detected_status:-inactive}"
            service_found="true"
            return 0
        fi
    done

    if [ -e /etc/init.d/rabbitmq-server ] || [ -e /etc/init.d/rabbitmq ]; then
        SERVICE_NAME="rabbitmq"
        SERVICE_STATUS="inactive"
        service_found="true"
    fi
}

detect_package_state() {
    if command_exists dpkg-query; then
        if [ "$(dpkg-query -W -f='${Status}' rabbitmq-server 2>/dev/null || true)" = "install ok installed" ]; then
            package_installed="true"
        fi
    elif dpkg -l 2>/dev/null | awk '$1 == "ii" && $2 == "rabbitmq-server" { found=1 } END { exit found ? 0 : 1 }'; then
        package_installed="true"
    fi

    if command_exists rpm && rpm -q rabbitmq-server >/dev/null 2>&1; then
        package_installed="true"
    fi
}

detect_binary_state() {
    if command_exists rabbitmq-server || \
       [ -x /usr/sbin/rabbitmq-server ] || \
       [ -x /usr/lib/rabbitmq/bin/rabbitmq-server ] || \
       [ -x /opt/rabbitmq/sbin/rabbitmq-server ] || \
       [ -x /usr/local/bin/rabbitmq-server ]; then
        binary_found="true"
    fi
}

echo -e "${YELLOW}1. 检查安装证据:${NC}"
detect_package_state
detect_binary_state
detect_service_state

if [ "$package_installed" = "true" ]; then
    echo -e "RabbitMQ 包状态：${GREEN}rabbitmq-server 已完整安装${NC}"
else
    echo -e "RabbitMQ 包状态：${RED}未发现完整安装的 rabbitmq-server 包${NC}"
fi

if [ "$binary_found" = "true" ]; then
    echo -e "RabbitMQ 服务端二进制：${GREEN}存在${NC}"
else
    echo -e "RabbitMQ 服务端二进制：${RED}不存在${NC}"
fi

echo -e "${YELLOW}2. 检查运行进程:${NC}"
if find_rabbitmq_process; then
    process_found="true"
    echo -e "RabbitMQ 进程：${GREEN}运行中 (PID: $RABBITMQ_PID)${NC}"
    echo "$RABBITMQ_CMD"
else
    echo -e "RabbitMQ 进程：${RED}未发现 RabbitMQ 专属 Erlang 进程${NC}"
fi

echo -e "${YELLOW}3. 检查 rabbitmqctl:${NC}"
rabbitmqctl_running="false"
if command_exists rabbitmqctl; then
    if rabbitmqctl status >/dev/null 2>&1; then
        rabbitmqctl_running="true"
        echo -e "rabbitmqctl status：${GREEN}正常${NC}"
    else
        echo -e "rabbitmqctl status：${RED}失败或节点未运行${NC}"
    fi
else
    echo -e "${YELLOW}rabbitmqctl 不可用${NC}"
fi

echo -e "${YELLOW}4. 检查端口监听:${NC}"
amqp_result="$(get_listener_result "$AMQP_PORT")"
management_result="$(get_listener_result "$MANAGEMENT_PORT")"

if [ -n "$amqp_result" ]; then
    amqp_open="true"
    echo -e "AMQP 端口 ($AMQP_PORT)：${GREEN}监听中${NC}"
    echo "$amqp_result"
    if listener_is_bound_all "$amqp_result" "$AMQP_PORT"; then
        amqp_bind_all="true"
    fi
else
    echo -e "AMQP 端口 ($AMQP_PORT)：${RED}未监听${NC}"
fi

if [ -n "$management_result" ]; then
    management_open="true"
    echo -e "Management 端口 ($MANAGEMENT_PORT)：${GREEN}监听中${NC}"
    echo "$management_result"
    if listener_is_bound_all "$management_result" "$MANAGEMENT_PORT"; then
        mgmt_bind_all="true"
    fi
else
    echo -e "Management 端口 ($MANAGEMENT_PORT)：${YELLOW}未监听${NC}"
fi

if listener_is_rabbitmq_owned "$amqp_result" || listener_is_rabbitmq_owned "$management_result"; then
    port_listening="true"
    echo -e "RabbitMQ 端口归属：${GREEN}端口由 RabbitMQ 进程监听${NC}"
elif [ "$amqp_open" = "true" ] || [ "$management_open" = "true" ]; then
    echo -e "${YELLOW}RabbitMQ 端口归属：端口被其他进程占用，不作为 RabbitMQ 运行证据${NC}"
fi

if [ "$service_active" = "true" ] || [ "$process_found" = "true" ] || [ "$rabbitmqctl_running" = "true" ]; then
    is_running="true"
fi

if [ "$package_installed" = "true" ] || [ "$binary_found" = "true" ] || [ "$service_active" = "true" ] || [ "$process_found" = "true" ] || [ "$rabbitmqctl_running" = "true" ]; then
    is_installed="true"
fi

echo -e "${YELLOW}5. 检查版本与管理能力:${NC}"
if [ "$is_installed" = "true" ]; then
    v_out="$(rabbitmqctl version 2>/dev/null || rabbitmq-server --version 2>/dev/null || true)"
    version="$(printf '%s\n' "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)"
    version="${version:-未知}"

    if command_exists rabbitmq-plugins; then
        if rabbitmq-plugins list -E -m 2>/dev/null | grep -qx "rabbitmq_management"; then
            management_plugin_enabled="true"
        fi
    elif [ -f /etc/rabbitmq/enabled_plugins ] && grep -q "rabbitmq_management" /etc/rabbitmq/enabled_plugins; then
        management_plugin_enabled="true"
    fi

    if [ "$management_open" = "true" ] && [ "$port_listening" = "true" ] && command_exists curl; then
        management_http_status="$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:${MANAGEMENT_PORT}/api/overview" || true)"
        case "$management_http_status" in
            200|301|302|401|403)
                management_http_ready="true"
                ;;
        esac
    fi
fi

if [ "$management_plugin_enabled" = "true" ] && [ "$management_open" = "true" ] && [ "$mgmt_bind_all" = "true" ] && [ "$amqp_bind_all" = "true" ]; then
    remote_access_available="true"
fi

if [ "$service_found" = "true" ] && [ "$is_installed" != "true" ] && [ "$is_running" != "true" ]; then
    service_only_stale="true"
    echo -e "${YELLOW}RabbitMQ 服务定义存在，但未发现服务端二进制、完整包或 RabbitMQ 运行进程，按残留服务处理${NC}"
    echo -e "${YELLOW}RabbitMQ 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理${NC}"
fi

if [ -e /etc/rabbitmq ] && [ "$is_installed" != "true" ] && [ "$is_running" != "true" ]; then
    config_only_residue="true"
    echo -e "${YELLOW}RabbitMQ 配置目录存在，但未发现服务端二进制、完整包或 RabbitMQ 运行进程，按残留配置处理${NC}"
fi

echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED:$is_installed"
echo "VERSION:${version:-未知}"
echo "RUNNING:$is_running"
echo "PORT:$AMQP_PORT,$MANAGEMENT_PORT"
echo "PACKAGE_INSTALLED:$package_installed"
echo "BINARY_FOUND:$binary_found"
echo "PROCESS_FOUND:$process_found"
echo "SERVICE_FOUND:$service_found"
echo "SERVICE_ACTIVE:$service_active"
echo "SERVICE_NAME:$SERVICE_NAME"
echo "SERVICE_STATUS:$SERVICE_STATUS"
echo "PORT_LISTENING:$port_listening"
echo "REMOTE_ACCESS_AVAILABLE:$remote_access_available"
echo "MANAGEMENT_PLUGIN_ENABLED:$management_plugin_enabled"
echo "MANAGEMENT_HTTP_READY:$management_http_ready"
echo "AMQP_BIND_ALL:$amqp_bind_all"
echo "MGMT_BIND_ALL:$mgmt_bind_all"
echo "MANAGEMENT_OPEN:$management_open"
echo "SERVICE_ONLY_STALE:$service_only_stale"
echo "SERVICE_ONLY_STALE: ${service_only_stale:-false}"
echo "CONFIG_ONLY_RESIDUE:$config_only_residue"
echo "------------------------"

if [ "$is_installed" = "true" ]; then
    if [ "$is_running" = "true" ]; then
        echo -e "${GREEN}最终状态：已安装且运行中 (v${version:-未知})${NC}"
    else
        echo -e "${YELLOW}最终状态：已安装但未运行${NC}"
    fi
else
    echo -e "${RED}最终状态：未安装${NC}"
fi
