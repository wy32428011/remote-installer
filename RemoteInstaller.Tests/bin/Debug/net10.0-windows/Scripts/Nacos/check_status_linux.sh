#!/bin/bash

# =================================================================
# Nacos 状态检测脚本 (Linux)
# =================================================================

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "========================================"
echo "      Nacos 状态检测脚本"
echo "========================================"

# 初始化状态
is_installed="false"
is_running="false"
version="未知"

# 默认端口
HTTP_PORT=8848
RAFT_PORT=9848
GRPC_PORT=9849

# 1. 检查安装情况
echo -e "${YELLOW}1. 检查安装情况:${NC}"

# 检查安装目录
nacos_dir=""
for dir in /opt/nacos /opt/nacos-* /usr/local/nacos /usr/share/nacos; do
    if [ -d "$dir/conf" ]; then
        nacos_dir="$dir"
        break
    fi
done

if [ -n "$nacos_dir" ]; then
    is_installed="true"
    echo -e "Nacos 已安装：${GREEN}是${NC}"
    echo "安装目录：$nacos_dir"

    # 尝试获取版本
    if [ -d "$nacos_dir/lib" ]; then
        version=$(ls "$nacos_dir/lib/" 2>/dev/null | grep -oE 'nacos-server-[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' || echo "未知")
    fi
    echo "版本：${version:-未知}"
else
    echo -e "Nacos 已安装：${RED}否${NC}"
fi

# 如果未安装，直接输出结果
if [ "$is_installed" = "false" ]; then
    echo ""
    echo "--- MACHINE READABLE ---"
    echo "INSTALLED: false"
    echo "VERSION: 未知"
    echo "RUNNING: false"
    echo "PORT: 8848,9848,9849"
    echo "------------------------"
    exit 0
fi

# 2. 检查运行进程
echo -e "${YELLOW}2. 检查运行进程:${NC}"
nacos_pid=$(pgrep -f "nacos.nacos" 2>/dev/null || pgrep -f "nacos-server.jar" 2>/dev/null)
if [ -n "$nacos_pid" ]; then
    is_running="true"
    echo -e "Nacos 运行状态：${GREEN}运行中 (PID: $nacos_pid)${NC}"
    ps -p "$nacos_pid" -o pid,cmd --no-headers 2>/dev/null || true
else
    echo -e "Nacos 运行状态：${RED}未运行 (进程检测)${NC}"
fi

# 3. 检查端口监听
echo -e "${YELLOW}3. 检查端口监听:${NC}"
http_open=false
raft_open=false
grpc_open=false
http_bind_all=false

# 检查 HTTP 端口
if command -v ss &> /dev/null; then
    http_result=$(ss -tlnp 2>/dev/null | grep ":$HTTP_PORT")
elif command -v netstat &> /dev/null; then
    http_result=$(netstat -tlnp 2>/dev/null | grep ":$HTTP_PORT")
fi

if [ -n "$http_result" ]; then
    echo -e "HTTP 端口 ($HTTP_PORT): ${GREEN}监听中${NC}"
    echo "$http_result"
    http_open=true
    if [ "$is_running" = "false" ]; then
        is_running="true"
    fi
    # 检查是否监听在所有接口
    if echo "$http_result" | grep -qE "0\.0\.0\.0:$HTTP_PORT|\*:$HTTP_PORT|\[::\]:$HTTP_PORT"; then
        echo -e "HTTP 绑定地址：${GREEN}0.0.0.0 (允许远程访问)${NC}"
        http_bind_all=true
    else
        echo -e "HTTP 绑定地址：${RED}127.0.0.1 (仅本地访问)${NC}"
    fi
else
    echo -e "HTTP 端口 ($HTTP_PORT): ${RED}未监听${NC}"
fi

# 检查 Raft 端口
if command -v ss &> /dev/null; then
    raft_result=$(ss -tlnp 2>/dev/null | grep ":$RAFT_PORT")
elif command -v netstat &> /dev/null; then
    raft_result=$(netstat -tlnp 2>/dev/null | grep ":$RAFT_PORT")
fi

if [ -n "$raft_result" ]; then
    echo -e "Raft 端口 ($RAFT_PORT): ${GREEN}监听中${NC}"
    echo "$raft_result"
    raft_open=true
else
    echo -e "Raft 端口 ($RAFT_PORT): ${YELLOW}未监听${NC}"
fi

# 检查 gRPC 端口
if command -v ss &> /dev/null; then
    grpc_result=$(ss -tlnp 2>/dev/null | grep ":$GRPC_PORT")
elif command -v netstat &> /dev/null; then
    grpc_result=$(netstat -tlnp 2>/dev/null | grep ":$GRPC_PORT")
fi

if [ -n "$grpc_result" ]; then
    echo -e "gRPC 端口 ($GRPC_PORT): ${GREEN}监听中${NC}"
    echo "$grpc_result"
    grpc_open=true
else
    echo -e "gRPC 端口 ($GRPC_PORT): ${YELLOW}未监听${NC}"
fi

# 4. HTTP 健康检查
echo -e "${YELLOW}4. HTTP 健康检查:${NC}"
if [ "$http_open" = "true" ]; then
    http_status=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$HTTP_PORT/nacos/" 2>/dev/null || echo "000")
    if [ "$http_status" = "200" ] || [ "$http_status" = "302" ]; then
        echo -e "HTTP 健康状态：${GREEN}正常 (HTTP $http_status)${NC}"
        is_running="true"
    else
        echo -e "HTTP 健康状态：${YELLOW}异常 (HTTP $http_status)${NC}"
    fi
else
    echo -e "HTTP 健康状态：${RED}无法访问${NC}"
fi

# 5. systemd 服务状态
echo -e "${YELLOW}5. systemd 服务状态:${NC}"
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nacos 2>/dev/null; then
        echo -e "服务状态：${GREEN}active (running)${NC}"
        is_running="true"
        is_installed="true"
    elif systemctl list-units --all --type=service 2>/dev/null | grep -Eiwq 'nacos'; then
        echo -e "服务状态：${YELLOW}已安装但未运行${NC}"
        is_installed="true"
    else
        echo -e "服务状态：${BLUE}未配置 systemd 服务${NC}"
    fi
else
    echo -e "${YELLOW}systemctl 不可用，跳过服务状态检查${NC}"
fi

# 6. 远程访问检查
echo -e "${YELLOW}6. 远程访问检查:${NC}"
if [ "$http_bind_all" = "true" ] && [ "$http_open" = "true" ]; then
    echo -e "远程访问状态：${GREEN}可用${NC}"
elif [ "$http_open" = "true" ]; then
    echo -e "远程访问状态：${YELLOW}端口监听中但可能仅绑定本地地址${NC}"
else
    echo -e "远程访问状态：${RED}不可用 (服务未运行)${NC}"
fi

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: $is_installed"
echo "VERSION: ${version:-未知}"
echo "RUNNING: $is_running"
echo "PORT: $HTTP_PORT,$RAFT_PORT,$GRPC_PORT"
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
