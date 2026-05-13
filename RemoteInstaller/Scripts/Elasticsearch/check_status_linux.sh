#!/bin/bash

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo "========================================"
echo "      Elasticsearch 状态检测脚本"
echo "========================================"

# 初始化状态
is_installed="false"
is_running="false"
version="未知"
es_home=""
package_installed="false"
service_only_stale="false"
SERVICE_NAME="elasticsearch"
SERVICE_STATUS="not-found"
SERVICE_FOUND=false
STATUS_SCRIPT_PID=$$
STATUS_SCRIPT_PPID=$PPID

HTTP_PORT=9200

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"

# 检查常见安装路径
if [ -f /usr/share/elasticsearch/bin/elasticsearch ]; then
    is_installed="true"
    es_home="/usr/share/elasticsearch"
    echo -e "Elasticsearch 已安装: ${GREEN}是 (系统包)${NC}"
    echo -e "安装路径: $es_home"
elif [ -f /opt/elasticsearch/bin/elasticsearch ]; then
    is_installed="true"
    es_home="/opt/elasticsearch"
    echo -e "Elasticsearch 已安装: ${GREEN}是 (手动安装)${NC}"
    echo -e "安装路径: $es_home"
elif [ -f /usr/bin/elasticsearch ]; then
    is_installed="true"
    es_home="/usr"
    echo -e "Elasticsearch 已安装: ${GREEN}是 (PATH)${NC}"
elif command -v elasticsearch &> /dev/null; then
    is_installed="true"
    ES_PATH=$(which elasticsearch 2>/dev/null)
    es_home=$(dirname "$(dirname "$ES_PATH")")
    echo -e "Elasticsearch 已安装: ${GREEN}是 (命令)${NC}"
    echo -e "路径: $ES_PATH"
else
    # 检查包管理器
    if dpkg -l 2>/dev/null | grep -qE "^ii.*elasticsearch"; then
        package_installed="true"
        is_installed="true"
        echo -e "Elasticsearch 已安装: ${GREEN}是 (Debian/Ubuntu)${NC}"
        # 尝试确定安装路径
        if [ -f /usr/share/elasticsearch/bin/elasticsearch ]; then
            es_home="/usr/share/elasticsearch"
        elif [ -f /usr/bin/elasticsearch ]; then
            es_home="/usr"
        fi
    elif rpm -qa 2>/dev/null | grep -q "^elasticsearch"; then
        package_installed="true"
        is_installed="true"
        echo -e "Elasticsearch 已安装: ${GREEN}是 (RedHat/CentOS)${NC}"
        # 尝试确定安装路径
        if [ -f /usr/share/elasticsearch/bin/elasticsearch ]; then
            es_home="/usr/share/elasticsearch"
        elif [ -f /usr/bin/elasticsearch ]; then
            es_home="/usr"
        fi
    fi
fi

# 检查 systemd 服务
if [ "$is_installed" = "false" ]; then
    if systemctl list-unit-files 2>/dev/null | grep -q '^elasticsearch\.service' || \
       systemctl list-units --all --type=service 2>/dev/null | grep -qi "elasticsearch"; then
        SERVICE_FOUND=true
        echo -e "Elasticsearch systemd 服务定义：${YELLOW}存在，继续核对二进制、包、进程和端口${NC}"
    fi
fi

# 2. 获取版本信息
echo -e "${YELLOW}2. 获取版本信息:${NC}"
if [ -n "$es_home" ] && [ -f "$es_home/bin/elasticsearch" ]; then
    v_out=$("$es_home/bin/elasticsearch" --version 2>&1) || true
elif command -v elasticsearch &> /dev/null; then
    v_out=$(elasticsearch --version 2>&1) || true
fi

if [ -n "$v_out" ]; then
    version=$(echo "$v_out" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -z "$version" ]; then
        version=$(echo "$v_out" | grep -oE 'version "[0-9]+\.[0-9]+\.[0-9]+"' | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    fi
fi
echo -e "版本: ${GREEN}${version:-未知}${NC}"

# 3. 检查运行进程
echo -e "${YELLOW}3. 检查运行进程:${NC}"

find_es_pids() {
    local pids=""
    local pid
    local cmdline

    for pid in $(pgrep -f "elasticsearch|org\.elasticsearch\.bootstrap\.Elasticsearch|/usr/share/elasticsearch|/opt/elasticsearch" 2>/dev/null || true); do
        if [ "$pid" = "$STATUS_SCRIPT_PID" ] || [ "$pid" = "$STATUS_SCRIPT_PPID" ] || [ "$pid" = "$$" ]; then
            continue
        fi

        if [ -r "/proc/$pid/cmdline" ]; then
            cmdline=$(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null)
            if echo "$cmdline" | grep -Eq "REMOTE_INSTALLER_CHECK_STATUS_SCRIPT|check_status_linux\.sh"; then
                continue
            fi

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

es_pid=$(find_es_pids | awk '{print $1}')
if [ -n "$es_pid" ]; then
    is_running="true"
    is_installed="true"
    echo -e "Elasticsearch 运行状态: ${GREEN}运行中 (PID: $es_pid)${NC}"
    ps -p "$es_pid" -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "Elasticsearch 运行状态: ${RED}未运行 (进程检测)${NC}"
fi

# 4. 检查端口监听
echo -e "${YELLOW}4. 检查端口监听 ($HTTP_PORT):${NC}"
port_open=false
if command -v ss &> /dev/null; then
    result=$(ss -tlnp 2>/dev/null | grep -E ":(9200|9201|9300)[[:space:]]" || true)
elif command -v netstat &> /dev/null; then
    result=$(netstat -tlnp 2>/dev/null | grep -E ":(9200|9201|9300)[[:space:]]" || true)
fi

if [ -n "$result" ]; then
    echo -e "端口监听: ${GREEN}是${NC}"
    echo "$result"
    port_open=true
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
    is_installed="true"
else
    echo -e "端口监听: ${RED}否 (端口未开放)${NC}"
fi

# 5. API 测试
echo -e "${YELLOW}5. API 连接测试:${NC}"
if command -v curl &> /dev/null; then
    api_response=$(curl -s -k --connect-timeout 5 "http://localhost:$HTTP_PORT" 2>/dev/null) || true
    if [ -n "$api_response" ]; then
        echo -e "Elasticsearch API: ${GREEN}响应正常${NC}"

        # 从 API 响应中提取版本 (ES 8.x JSON 结构: {"version":{"number":"8.x.x",...}})
        api_version=$(echo "$api_response" | grep -oE '"number"[[:space:]]*:[[:space:]]*"[0-9]+\.[0-9]+\.[0-9]+"' | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1) || true
        if [ -n "$api_version" ]; then
            version="$api_version"
            echo -e "API 版本: ${GREEN}$version${NC}"
        fi

        # 显示简要信息
        cluster_name=$(echo "$api_response" | grep -oE '"cluster_name"[[:space:]]*:[[:space:]]*"[^"]+"' | cut -d'"' -f4) || true
        if [ -n "$cluster_name" ]; then
            echo -e "集群名称: ${BLUE}$cluster_name${NC}"
        fi

        is_running="true"
        is_installed="true"
    else
        echo -e "Elasticsearch API: ${RED}无响应${NC}"
    fi
else
    echo -e "${YELLOW}curl 不可用，跳过 API 测试${NC}"
fi

# 6. systemd 服务状态
echo -e "${YELLOW}6. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet elasticsearch 2>/dev/null; then
        echo -e "服务状态: ${GREEN}active (running)${NC}"
        SERVICE_STATUS="active"
        SERVICE_FOUND=true
        is_running="true"
        is_installed="true"
    elif systemctl list-unit-files 2>/dev/null | grep -q '^elasticsearch\.service' || \
         systemctl list-units --all --type=service 2>/dev/null | grep -qi "elasticsearch"; then
        SERVICE_FOUND=true
        SERVICE_STATUS=$(systemctl is-active elasticsearch 2>/dev/null || echo "inactive")
        if [ "$is_installed" = "true" ] || [ "$is_running" = "true" ] || [ "$port_open" = "true" ]; then
            echo -e "服务状态: ${YELLOW}已安装但未运行 (${SERVICE_STATUS})${NC}"
        else
            service_only_stale="true"
            echo -e "${YELLOW}Elasticsearch 服务定义存在，但未发现二进制、包、进程或端口，按残留服务处理${NC}"
        fi
    else
        echo -e "服务状态: ${GRAY}未找到 systemd 服务${NC}"
    fi
else
    echo -e "${GRAY}systemctl 不可用，跳过服务状态检查${NC}"
fi

if [ "$is_running" = "true" ] && [ "$is_installed" = "false" ]; then
    is_installed="true"
    echo -e "${YELLOW}检测到 Elasticsearch 正在运行，已将安装状态归一为已安装${NC}"
fi

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: $HTTP_PORT"
echo "PACKAGE_INSTALLED: ${package_installed:-false}"
echo "SERVICE_NAME: ${SERVICE_NAME:-unknown}"
echo "SERVICE_STATUS: ${SERVICE_STATUS:-unknown}"
echo "SERVICE_ONLY_STALE: ${service_only_stale:-false}"
echo "------------------------"

# 最终状态摘要
echo ""
if [ "$is_installed" = "true" ]; then
    if [ "$is_running" = "true" ]; then
        echo -e "${GREEN}最终状态：已安装且运行中 (v${version:-未知})${NC}"
        exit 0
    else
        echo -e "${YELLOW}最终状态：已安装但未运行${NC}"
        exit 1
    fi
else
    echo -e "${RED}最终状态：未安装${NC}"
    exit 0
fi
