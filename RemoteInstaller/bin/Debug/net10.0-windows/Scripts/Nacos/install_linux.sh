#!/bin/bash
set -e

# =================================================================
# Nacos Linux 安装脚本
# =================================================================

LOG_FILE="nacos_install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "Nacos 安装脚本开始..."

# 参数
HTTP_PORT="${HTTP_PORT:-8848}"
RAFT_PORT="${RAFT_PORT:-9848}"
GRPC_PORT="${GRPC_PORT:-9849}"
MODE="${MODE:-standalone}"
USERNAME="${USERNAME:-nacos}"
PASSWORD="${PASSWORD:-nacos}"
INSTALL_DIR="/opt/nacos"

# 颜色输出
write_color() {
    local text="$1"
    local color="$2"
    echo -e "\033[${color}m${text}\033[0m"
}

write_progress() {
    local stage="$1"
    local percent="$2"
    echo "PROGRESS:${stage}:${percent}"
}

# 1. 检查 Root 权限
write_progress "CheckingPermissions" 5
write_color "1. 检查 Root 权限:" "Yellow"
if [ "$EUID" -ne 0 ]; then
    write_color "错误：请使用 root 权限运行此脚本" "Red"
    exit 1
fi
write_color "Root 权限：已获取" "Green"

# 2. 检查 Java 环境
write_progress "CheckingJava" 10
write_color "2. 检查 Java 环境:" "Yellow"

if command -v java &> /dev/null; then
    JAVA_VERSION=$(java -version 2>&1 | head -n 1)
    write_color "Java 版本：$JAVA_VERSION" "Green"

    # 检查 Java 版本
    JAVA_MAJOR_VERSION=$(java -version 2>&1 | grep -oE '[0-9]+' | head -n 1)
    if [ -z "$JAVA_MAJOR_VERSION" ]; then
        # 旧版本格式 1.8.0
        JAVA_MAJOR_VERSION=$(java -version 2>&1 | grep -oE '1\.[0-9]+' | head -n 1 | cut -d. -f2)
    fi

    if [ "$JAVA_MAJOR_VERSION" -ge 8 ] 2>/dev/null; then
        write_color "Java 版本满足要求 (需要 1.8+)" "Green"
    else
        write_color "警告：Java 版本可能过低，需要 JDK 1.8+" "Yellow"
    fi
else
    write_color "错误：未找到 Java，请先安装 JDK 1.8+" "Red"
    write_color "Ubuntu/Debian: sudo apt-get install openjdk-11-jdk" "Yellow"
    write_color "CentOS/RHEL: sudo yum install java-11-openjdk-devel" "Yellow"
    exit 1
fi

# 3. 检查安装包
write_progress "CheckingPackage" 15
write_color "3. 检查安装包:" "Yellow"

PACKAGE_PATH="${PACKAGE_PATH:-""}"
if [ -z "$PACKAGE_PATH" ] || [ ! -f "$PACKAGE_PATH" ]; then
    write_color "错误：未提供 Nacos 安装包" "Red"
    exit 1
fi
write_color "安装包路径：$PACKAGE_PATH" "Green"

# 4. 解压安装包
write_progress "ExtractingPackage" 20
write_color "4. 解压 Nacos 安装包:" "Yellow"

mkdir -p "$INSTALL_DIR"
if [[ "$PACKAGE_PATH" == *.zip ]]; then
    unzip -q "$PACKAGE_PATH" -d "$INSTALL_DIR"
elif [[ "$PACKAGE_PATH" == *.tar.gz ]]; then
    tar -xzf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
else
    write_color "错误：不支持的包格式" "Red"
    exit 1
fi

# 查找解压后的目录
NACOS_DIR=$(find "$INSTALL_DIR" -maxdepth 1 -type d -name "nacos*" | head -n 1)
if [ -z "$NACOS_DIR" ]; then
    NACOS_DIR="$INSTALL_DIR"
fi

write_color "解压完成：$NACOS_DIR" "Green"

# 5. 配置 Nacos
write_progress "ConfiguringNacos" 30
write_color "5. 配置 Nacos:" "Yellow"

CONF_DIR="$NACOS_DIR/conf"

# 配置 application.properties
write_color "配置 application.properties..." "Yellow"
cat > "$CONF_DIR/application.properties" << EOF
# 服务端口配置
server.port=${HTTP_PORT}

# 运行模式
spring.datasource.platform=native

# 集群配置
nacos.naming.client.expired.time=180
nacos.core.auth.enabled=true
nacos.core.auth.default.token.secret.key=SecretKey012345678901234567890123456789012345678901234567890123456789
nacos.core.auth.server.identity.key=nacos-server-identity
nacos.core.auth.server.identity.value=nacos-server-identity-value

# 内置数据库配置
nacos.standalone=true

# 用户配置
nacos.core.auth.default.user.name=${USERNAME}
nacos.core.auth.default.user.password=${PASSWORD}

# gRPC 端口 (2.x 版本需要)
nacos.grpc.server.port=${GRPC_PORT}
EOF

write_color "application.properties 配置完成" "Green"

# 6. 配置防火墙
write_progress "ConfiguringFirewall" 50
write_color "6. 配置防火墙:" "Yellow"

# ufw
if command -v ufw &> /dev/null; then
    ufw allow "$HTTP_PORT/tcp" 2>/dev/null || true
    ufw allow "$RAFT_PORT/tcp" 2>/dev/null || true
    ufw allow "$GRPC_PORT/tcp" 2>/dev/null || true
    write_color "已配置 ufw 防火墙规则" "Green"
fi

# firewalld
if command -v firewall-cmd &> /dev/null; then
    firewall-cmd --permanent --add-port="$HTTP_PORT/tcp" 2>/dev/null || true
    firewall-cmd --permanent --add-port="$RAFT_PORT/tcp" 2>/dev/null || true
    firewall-cmd --permanent --add-port="$GRPC_PORT/tcp" 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
    write_color "已配置 firewalld 防火墙规则" "Green"
fi

# iptables
if command -v iptables &> /dev/null; then
    iptables -I INPUT -p tcp --dport "$HTTP_PORT" -j ACCEPT 2>/dev/null || true
    iptables -I INPUT -p tcp --dport "$RAFT_PORT" -j ACCEPT 2>/dev/null || true
    iptables -I INPUT -p tcp --dport "$GRPC_PORT" -j ACCEPT 2>/dev/null || true
    if command -v iptables-save &> /dev/null; then
        iptables-save > /etc/iptables.rules 2>/dev/null || true
    fi
    write_color "已配置 iptables 防火墙规则" "Green"
fi

# 7. 启动 Nacos
write_progress "StartingNacos" 70
write_color "7. 启动 Nacos:" "Yellow"

cd "$NACOS_DIR/bin"

# 设置 JVM 参数
export NACOS_JVM="-Xms512m -Xmx512m -Xmn256m"

# 启动 Nacos
nohup ./startup.sh -m "$MODE" > nacos_start.log 2>&1 &

write_color "Nacos 启动命令已执行" "Yellow"

# 8. 等待服务启动
write_progress "WaitingForService" 80
write_color "8. 等待 Nacos 服务启动:" "Yellow"

SUCCESS=false
COUNT=0
while [ $COUNT -lt 60 ]; do
    # 检查端口
    if command -v ss &> /dev/null; then
        PORT_CHECK=$(ss -tlnp 2>/dev/null | grep ":$HTTP_PORT")
    elif command -v netstat &> /dev/null; then
        PORT_CHECK=$(netstat -tlnp 2>/dev/null | grep ":$HTTP_PORT")
    fi

    if [ -n "$PORT_CHECK" ]; then
        # 检查 HTTP 响应
        HTTP_CHECK=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$HTTP_PORT/nacos/" 2>/dev/null || echo "000")
        if [ "$HTTP_CHECK" = "200" ] || [ "$HTTP_CHECK" = "302" ]; then
            write_color "Nacos 服务已成功启动" "Green"
            SUCCESS=true
            break
        fi
    fi

    write_color "等待服务启动... ($((COUNT+1))/60)" "Gray"
    sleep 3
    COUNT=$((COUNT+1))
done

if [ "$SUCCESS" = false ]; then
    write_color "警告：Nacos 在 180 秒内未能启动" "Yellow"
    write_color "请检查日志：$NACOS_DIR/logs/start.out" "Yellow"
fi

# 9. 验证安装
write_progress "Verifying" 90
write_color "9. 验证 Nacos 安装:" "Yellow"

VERSION=""
if [ -d "$NACOS_DIR/lib" ]; then
    VERSION=$(ls "$NACOS_DIR/lib/" 2>/dev/null | grep -oE 'nacos-server-[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' || echo "未知")
fi

write_color "Nacos 版本：$VERSION" "Green"
write_color "HTTP 端口：$HTTP_PORT" "Green"
write_color "gRPC 端口：$GRPC_PORT" "Green"
write_color "管理界面：http://localhost:$HTTP_PORT/nacos" "Green"

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: $SUCCESS"
echo "PORT: $HTTP_PORT,$RAFT_PORT,$GRPC_PORT"
echo "------------------------"
echo "STAGE:SUCCESS"

write_color "" "Cyan"
write_color "========================================" "Cyan"
write_color "      Nacos 安装完成！" "Green"
write_color "========================================" "Cyan"
write_color "安装目录：$NACOS_DIR" "Yellow"
write_color "HTTP 端口：$HTTP_PORT" "Yellow"
write_color "gRPC 端口：$GRPC_PORT" "Yellow"
write_color "管理界面：http://<服务器 IP>:$HTTP_PORT/nacos" "Yellow"
write_color "用户名：$USERNAME" "Yellow"
write_color "密码：$PASSWORD" "Yellow"
write_color ""
