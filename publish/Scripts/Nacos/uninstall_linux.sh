#!/bin/bash

# =================================================================
# Nacos Linux 卸载脚本
# =================================================================

echo "========================================"
echo "      Nacos 卸载脚本"
echo "========================================"

# 参数
INSTALL_DIR="${INSTALL_DIR:-/opt/nacos}"

# 1. 停止 Nacos
echo -e "\033[1;33m1. 停止 Nacos 服务:\033[0m"

NACOS_PID=$(pgrep -f "nacos.nacos" || pgrep -f "nacos-server.jar")
if [ -n "$NACOS_PID" ]; then
    echo "发现 Nacos 进程 (PID: $NACOS_PID)，正在停止..."
    kill -9 "$NACOS_PID" 2>/dev/null || true
    sleep 2

    # 再次检查
    NACOS_PID=$(pgrep -f "nacos.nacos" || pgrep -f "nacos-server.jar")
    if [ -z "$NACOS_PID" ]; then
        echo -e "Nacos 进程已停止\033[0m"
    else
        echo "强制终止 Nacos 进程..."
        kill -9 "$NACOS_PID" 2>/dev/null || true
    fi
else
    echo "未找到运行中的 Nacos 进程"
fi

# 2. 检查并停止 systemd 服务
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nacos 2>/dev/null; then
        echo "停止 Nacos systemd 服务..."
        systemctl stop nacos 2>/dev/null || true
        systemctl disable nacos 2>/dev/null || true
        echo "Nacos 服务已停止并禁用"
    fi
fi

# 3. 删除安装目录
echo -e "\033[1;33m2. 删除 Nacos 安装目录:\033[0m"

if [ -d "$INSTALL_DIR" ]; then
    echo "删除 $INSTALL_DIR ..."
    rm -rf "$INSTALL_DIR"
    echo -e "安装目录已删除\033[0m"
else
    # 尝试其他常见路径
    for dir in /opt/nacos-* /usr/local/nacos; do
        if [ -d "$dir" ]; then
            echo "删除 $dir ..."
            rm -rf "$dir"
            echo -e "安装目录已删除\033[0m"
            break
        fi
    done
fi

# 4. 清理防火墙规则
echo -e "\033[1;33m3. 清理防火墙规则:\033[0m"

# ufw
if command -v ufw &> /dev/null; then
    ufw delete allow 8848/tcp 2>/dev/null || true
    ufw delete allow 9848/tcp 2>/dev/null || true
    ufw delete allow 9849/tcp 2>/dev/null || true
    echo "已清理 ufw 防火墙规则"
fi

# firewalld
if command -v firewall-cmd &> /dev/null; then
    firewall-cmd --permanent --remove-port=8848/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=9848/tcp 2>/dev/null || true
    firewall-cmd --permanent --remove-port=9849/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
    echo "已清理 firewalld 防火墙规则"
fi

# 5. 清理环境变量
echo -e "\033[1;33m4. 清理环境变量:\033[0m"

# 从 /etc/profile 中清理
if [ -f /etc/profile ]; then
    sed -i '/NACOS_HOME/d' /etc/profile 2>/dev/null || true
    sed -i '/nacos/d' /etc/profile 2>/dev/null || true
fi

# 从 /etc/environment 中清理
if [ -f /etc/environment ]; then
    sed -i '/NACOS_HOME/d' /etc/environment 2>/dev/null || true
fi

echo "环境变量清理完成"

# 6. 删除日志目录
if [ -d /var/log/nacos ]; then
    echo "删除日志目录 /var/log/nacos ..."
    rm -rf /var/log/nacos
fi

# 7. 清理 systemd 服务
if command -v systemctl &> /dev/null; then
    rm -f /etc/systemd/system/nacos.service 2>/dev/null || true
    rm -f /lib/systemd/system/nacos.service 2>/dev/null || true
    rm -f /usr/lib/systemd/system/nacos.service 2>/dev/null || true
    systemctl daemon-reload 2>/dev/null || true
    systemctl reset-failed 2>/dev/null || true
    echo "systemd 服务已清理"
fi

# 8. 最终验证
echo ""
echo "========================================"
echo "      Nacos 卸载完成！"
echo "========================================"

echo ""
echo -e "\033[1;33m最终验证:\033[0m"

FAILED=0

# 验证进程（最多重试3次）
for retry in 1 2 3; do
    nacos_pid=$(pgrep -f "nacos.nacos" || pgrep -f "nacos-server.jar" || true)
    if [ -z "$nacos_pid" ]; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 Nacos 进程残留，强制终止后重试 ($retry/3)..."
        kill -9 $nacos_pid 2>/dev/null || true
        sleep 2
    fi
done

nacos_pid=$(pgrep -f "nacos.nacos" || pgrep -f "nacos-server.jar" || true)
if [ -n "$nacos_pid" ]; then
    echo -e "\033[0;31m警告：仍有 Nacos 进程运行\033[0m"
    FAILED=1
else
    echo -e "\033[0;32mNacos 进程：已停止\033[0m"
fi

# 验证服务
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet nacos 2>/dev/null; then
        echo -e "\033[0;31m警告：Nacos 服务仍在运行\033[0m"
        FAILED=1
    else
        echo -e "\033[0;32mNacos 服务：已停止\033[0m"
    fi
fi

# 验证端口 8848 和 9848
if command -v ss &> /dev/null; then
    if ss -tuln 2>/dev/null | grep -E ':(8848|9848)[[:space:]]' | grep -q .; then
        echo -e "\033[0;31m警告：Nacos 端口（8848/9848）仍在监听\033[0m"
        FAILED=1
    else
        echo -e "\033[0;32mNacos 端口（8848/9848）：已释放\033[0m"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln 2>/dev/null | grep -E ':(8848|9848)[[:space:]]' | grep -q .; then
        echo -e "\033[0;31m警告：Nacos 端口（8848/9848）仍在监听\033[0m"
        FAILED=1
    else
        echo -e "\033[0;32mNacos 端口（8848/9848）：已释放\033[0m"
    fi
fi

# 输出结果
echo ""
echo "--- MACHINE READABLE ---"
if [ "$FAILED" = 0 ]; then
    echo "UNINSTALLED: true"
    echo "STAGE:SUCCESS"
else
    echo "UNINSTALLED: true"
    echo "STAGE:PARTIAL"
fi
echo "------------------------"

echo ""
echo "========================================"
echo "      Nacos 卸载完成！"
echo "========================================"
echo ""
