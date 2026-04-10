#!/bin/bash
set -e

# 参数定义
# PACKAGE_PATH: 远程安装包路径
# PORT: 服务端口 (默认 80)

# 日志设置
LOG_FILE="install_nginx.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "Nginx 安装脚本开始..."
echo "当前工作目录: $(pwd)"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 检查参数
PORT=${PORT:-80}

# 检查 OS
if [ -f /etc/debian_version ]; then
    OS="Debian"
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    echo "不支持的操作系统"
    exit 1
fi
echo "检测到操作系统: $OS"

echo "PROGRESS:Installing:20"

# 1. 安装 Nginx
if [ "$OS" == "Debian" ]; then
    apt-get update
    apt-get install -y nginx
else
    if ! rpm -qa | grep -q epel-release; then
        yum install -y epel-release
    fi
    yum install -y nginx
fi

echo "PROGRESS:Configuring:60"

# 2. 配置端口
if [ "$PORT" != "80" ]; then
    echo "正在将默认端口从 80 修改为 $PORT..."
    
    # Ubuntu/Debian 默认配置
    if [ -f /etc/nginx/sites-available/default ]; then
        sed -i "s/listen 80 default_server;/listen $PORT default_server;/g" /etc/nginx/sites-available/default
        sed -i "s/listen \[::\]:80 default_server;/listen [::]:$PORT default_server;/g" /etc/nginx/sites-available/default
    fi
    
    # CentOS 默认配置
    if [ -f /etc/nginx/nginx.conf ]; then
        # 匹配 listen 80 default_server; 或 listen 80;
        sed -i "s/listen[[:space:]]*80[[:space:]]*default_server;/listen $PORT default_server;/g" /etc/nginx/nginx.conf
        sed -i "s/listen[[:space:]]*80;/listen $PORT;/g" /etc/nginx/nginx.conf
    fi
fi

# 3. 防火墙配置
if command -v firewall-cmd >/dev/null 2>&1; then
    echo "正在配置防火墙开放 $PORT 端口..."
    firewall-cmd --permanent --add-port=$PORT/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
elif command -v ufw >/dev/null 2>&1; then
    echo "正在配置 ufw 开放 $PORT 端口..."
    ufw allow $PORT/tcp 2>/dev/null || true
fi

echo "PROGRESS:Starting:80"

# 4. 启动并启用服务
systemctl enable nginx
systemctl restart nginx

echo "PROGRESS:Finishing:95"
echo "Nginx 安装完成，端口: $PORT"
echo "STAGE:SUCCESS"
