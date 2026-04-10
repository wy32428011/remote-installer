#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "========================================"
echo "      RabbitMQ 状态检测脚本"
echo "========================================"

# 初始化状态
is_installed="false"
is_running="false"
version="未知"
management_plugin_enabled="false"
management_http_ready="false"

# 默认端口
AMQP_PORT=5672
MANAGEMENT_PORT=15672

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"

# 检查命令是否存在
if command -v rabbitmq-server &> /dev/null || command -v rabbitmqctl &> /dev/null; then
    is_installed="true"
    echo -e "RabbitMQ 已安装：${GREEN}是 (命令存在)${NC}"
elif [ -f /usr/sbin/rabbitmq-server ] || [ -f /usr/lib/rabbitmq/bin/rabbitmq-server ]; then
    is_installed="true"
    echo -e "RabbitMQ 已安装：${GREEN}是 (文件存在)${NC}"
else
    # 检查包管理器
    if dpkg -l 2>/dev/null | grep -q '^ii.*rabbitmq'; then
        is_installed="true"
        echo -e "RabbitMQ 已安装：${GREEN}是 (Debian/Ubuntu)${NC}"
    elif rpm -qa 2>/dev/null | grep -q 'rabbitmq'; then
        is_installed="true"
        echo -e "RabbitMQ 已安装：${GREEN}是 (RedHat/CentOS)${NC}"
    fi
fi

if [ "$is_installed" = "true" ]; then
    # 获取版本
    v_out=$(rabbitmqctl version 2>/dev/null || rabbitmq-server --version 2>/dev/null || echo "")
    if [ -n "$v_out" ]; then
        echo "$v_out"
        version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    fi
    if [ -z "$version" ] || [ "$version" = "未知" ]; then
        # 尝试从 erl 获取版本
        v_out=$(erl -eval 'erlang:display(erlang:system_info(otp_release)), halt().' -noshell 2>/dev/null | head -n 1)
        if [ -n "$v_out" ]; then
            echo "Erlang OTP: $v_out"
        fi
    fi
    echo -e "版本：${GREEN}${version:-未知}${NC}"
else
    echo -e "RabbitMQ 已安装：${RED}否${NC}"
fi

# 如果未安装，直接输出结果
if [ "$is_installed" = "false" ]; then
    echo ""
    echo "--- MACHINE READABLE ---"
    echo "INSTALLED: false"
    echo "VERSION: 未知"
    echo "RUNNING: false"
    echo "PORT: 5672,15672"
    echo "REMOTE_ACCESS_AVAILABLE: false"
    echo "MANAGEMENT_PLUGIN_ENABLED: false"
    echo "AMQP_BIND_ALL: false"
    echo "MGMT_BIND_ALL: false"
    echo "MANAGEMENT_OPEN: false"
    echo "------------------------"
    exit 0
fi

# 2. 检查管理插件状态
echo -e "${YELLOW}2. 检查管理插件状态:${NC}"
if command -v rabbitmq-plugins &> /dev/null; then
    if rabbitmq-plugins list -E -m 2>/dev/null | grep -qx "rabbitmq_management"; then
        management_plugin_enabled="true"
        echo -e "Management 插件：${GREEN}已启用${NC}"
    else
        echo -e "Management 插件：${RED}未启用${NC}"
    fi
elif [ -f /etc/rabbitmq/enabled_plugins ] && grep -q "rabbitmq_management" /etc/rabbitmq/enabled_plugins; then
    management_plugin_enabled="true"
    echo -e "Management 插件：${GREEN}已在 enabled_plugins 中声明${NC}"
else
    echo -e "Management 插件：${YELLOW}无法直接确认${NC}"
fi

# 3. 检查运行进程
echo -e "${YELLOW}3. 检查运行进程:${NC}"
# RabbitMQ 使用 Erlang VM，进程名通常是 beam.smp
beam_pid=$(pgrep -f "beam\.sm[p]" 2>/dev/null | head -n 1)
if [ -n "$beam_pid" ]; then
    is_running="true"
    echo -e "RabbitMQ 运行状态：${GREEN}运行中 (PID: $beam_pid)${NC}"
    ps -p "$beam_pid" -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "RabbitMQ 运行状态：${RED}未运行 (进程检测)${NC}"
fi

# 4. 检查端口监听
echo -e "${YELLOW}4. 检查端口监听:${NC}"
amqp_open=false
management_open=false
amqp_bind_all=false
mgmt_bind_all=false

# 检查 AMQP 端口 (5672)
if command -v ss &> /dev/null; then
    amqp_result=$(ss -tlnp 2>/dev/null | grep ":$AMQP_PORT")
elif command -v netstat &> /dev/null; then
    amqp_result=$(netstat -tlnp 2>/dev/null | grep ":$AMQP_PORT")
fi

if [ -n "$amqp_result" ]; then
    echo -e "AMQP 端口 ($AMQP_PORT): ${GREEN}监听中${NC}"
    echo "$amqp_result"
    amqp_open=true
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
    # 检查是否监听在所有接口 (0.0.0.0 或 *)
    if echo "$amqp_result" | grep -qE "0\.0\.0\.0:$AMQP_PORT|\*:$AMQP_PORT|\[::\]:$AMQP_PORT"; then
        echo -e "AMQP 绑定地址：${GREEN}0.0.0.0 (允许远程访问)${NC}"
        amqp_bind_all=true
    else
        echo -e "AMQP 绑定地址：${RED}127.0.0.1 (仅本地访问)${NC}"
    fi
else
    echo -e "AMQP 端口 ($AMQP_PORT): ${RED}未监听${NC}"
fi

# 检查 Management 端口 (15672)
if command -v ss &> /dev/null; then
    mgmt_result=$(ss -tlnp 2>/dev/null | grep ":$MANAGEMENT_PORT")
elif command -v netstat &> /dev/null; then
    mgmt_result=$(netstat -tlnp 2>/dev/null | grep ":$MANAGEMENT_PORT")
fi

if [ -n "$mgmt_result" ]; then
    echo -e "Management 端口 ($MANAGEMENT_PORT): ${GREEN}监听中${NC}"
    echo "$mgmt_result"
    management_open=true
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
    # 检查是否监听在所有接口 (0.0.0.0 或 *)
    if echo "$mgmt_result" | grep -qE "0\.0\.0\.0:$MANAGEMENT_PORT|\*:$MANAGEMENT_PORT|\[::\]:$MANAGEMENT_PORT"; then
        echo -e "Management 绑定地址：${GREEN}0.0.0.0 (允许远程访问)${NC}"
        mgmt_bind_all=true
    else
        echo -e "Management 绑定地址：${RED}127.0.0.1 (仅本地访问)${NC}"
    fi
else
    echo -e "Management 端口 ($MANAGEMENT_PORT): ${YELLOW}未监听 (可能未启用插件)${NC}"
fi

# 5.1 检查 Management HTTP 接口
if [ "$management_open" = "true" ]; then
    echo -e "${YELLOW}5. 检查 Management HTTP 接口:${NC}"
    if command -v curl &> /dev/null; then
        management_http_status=$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:${MANAGEMENT_PORT}/api/overview" || true)
        case "$management_http_status" in
            200|301|302|401|403)
                management_http_ready="true"
                echo -e "Management HTTP 接口：${GREEN}可访问${NC} (/api/overview -> $management_http_status)"
                ;;
            *)
                echo -e "Management HTTP 接口：${RED}不可访问${NC} (/api/overview -> ${management_http_status:-无响应})"
                ;;
        esac
    else
        echo -e "Management HTTP 接口：${YELLOW}未检查 (系统缺少 curl)${NC}"
    fi
fi

# 远程访问可用性检查
echo -e "${YELLOW}6. 远程访问检查:${NC}"
remote_access_available="false"
if [ "$management_plugin_enabled" != "true" ]; then
    echo -e "远程访问状态：${RED}不可用 (rabbitmq_management 未启用)${NC}"
elif [ "$management_open" != "true" ]; then
    echo -e "远程访问状态：${RED}不可用 (管理端口未监听)${NC}"
elif [ "$mgmt_bind_all" != "true" ]; then
    echo -e "远程访问状态：${YELLOW}管理端口已监听但仅本地绑定${NC}"
elif command -v curl &> /dev/null && [ "$management_http_ready" != "true" ]; then
    echo -e "远程访问状态：${RED}不可用 (Management HTTP 接口未就绪)${NC}"
elif [ "$amqp_bind_all" = "true" ]; then
    remote_access_available="true"
    echo -e "远程访问状态：${GREEN}可用${NC}"
else
    echo -e "远程访问状态：${YELLOW}管理界面可访问，但 AMQP 仍需检查绑定地址${NC}"
fi

# 7. rabbitmqctl 状态检查
echo -e "${YELLOW}7. RabbitMQ 状态检查:${NC}"
if command -v rabbitmqctl &> /dev/null; then
    if rabbitmqctl status >/dev/null 2>&1; then
        echo -e "rabbitmqctl status: ${GREEN}正常${NC}"
        is_running="true"
        # 尝试从 rabbitmqctl 获取版本
        if [ "$version" = "未知" ]; then
            version=$(rabbitmqctl status 2>/dev/null | grep -oE 'RabbitMQ [0-9]+\.[0-9]+\.[0-9]+' | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
        fi
    else
        echo -e "rabbitmqctl status: ${RED}失败 (服务可能未运行)${NC}"
    fi
else
    echo -e "${YELLOW}rabbitmqctl 不可用${NC}"
fi

# 8. systemd 服务状态
echo -e "${YELLOW}8. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet rabbitmq-server 2>/dev/null; then
        echo -e "服务状态：${GREEN}active (running)${NC}"
        is_running="true"
        is_installed="true"
    elif systemctl list-units --all --type=service 2>/dev/null | grep -Eiwq 'rabbitmq'; then
        echo -e "服务状态：${YELLOW}已安装但未运行${NC}"
        is_installed="true"
    else
        echo -e "服务状态：${RED}未找到服务${NC}"
    fi
else
    echo -e "${YELLOW}systemctl 不可用，跳过服务状态检查${NC}"
fi

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: 5672,15672"
echo "REMOTE_ACCESS_AVAILABLE: $remote_access_available"
echo "MANAGEMENT_PLUGIN_ENABLED: $management_plugin_enabled"
echo "MANAGEMENT_HTTP_READY: $management_http_ready"
echo "AMQP_BIND_ALL: $amqp_bind_all"
echo "MGMT_BIND_ALL: $mgmt_bind_all"
echo "MANAGEMENT_OPEN: $management_open"
echo "------------------------"

# 最终状态摘要
if [ "$is_installed" = "true" ]; then
    if [ "$is_running" = "true" ]; then
        echo -e "${GREEN}最终状态：已安装且运行中 (v${version:-未知})${NC}"
    else
        echo -e "${YELLOW}最终状态：已安装但未运行${NC}"
    fi
else
    echo -e "${RED}最终状态：未安装${NC}"
fi
