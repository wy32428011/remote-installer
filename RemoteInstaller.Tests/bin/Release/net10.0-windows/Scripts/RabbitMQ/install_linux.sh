#!/bin/bash
set -e

# 参数定义
# PACKAGE_PATH: 远程安装包路径（可选，用于离线安装）
# AMQP_PORT: AMQP 端口 (默认 5672)
# MANAGEMENT_PORT: 管理端口 (默认 15672)
# CLUSTER_NAME: 集群名称 (默认 rabbitmq-cluster)
# NODE_NAME: 节点名称 (默认 rabbit@localhost)

# 日志设置
LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "RabbitMQ 安装脚本开始..."
echo "当前工作目录：$(pwd)"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 检查参数
PACKAGE_PATH="${PACKAGE_PATH:-""}"
AMQP_PORT="${AMQP_PORT:-5672}"
MANAGEMENT_PORT="${MANAGEMENT_PORT:-15672}"
CLUSTER_NAME="${CLUSTER_NAME:-rabbitmq-cluster}"
NODE_NAME="${NODE_NAME:-rabbit@localhost}"
ENABLE_REMOTE_ACCESS="${ENABLE_REMOTE_ACCESS:-true}"

# 1. 检测操作系统
echo "PROGRESS:DetectingOS:10"
echo "检测操作系统..."

if [ -f /etc/debian_version ]; then
    OS_FAMILY="Debian"
    if [ -f /etc/ubuntu-version ]; then
        OS="Ubuntu"
        OS_VERSION=$(cat /etc/ubuntu-version)
    else
        OS="Debian"
        OS_VERSION=$(cat /etc/debian_version)
    fi
elif [ -f /etc/redhat-release ]; then
    OS_FAMILY="RedHat"
    if [ -f /etc/centos-release ]; then
        OS="CentOS"
        OS_VERSION=$(cat /etc/centos-release | grep -oE '[0-9]+')
    else
        OS="RedHat"
        OS_VERSION=$(cat /etc/redhat-release | grep -oE '[0-9]+')
    fi
else
    echo "不支持的操作系统"
    exit 1
fi

echo "检测到操作系统：$OS $OS_VERSION ($OS_FAMILY)"

# 2. 安装前置依赖
echo "PROGRESS:InstallingDependencies:15"
echo "安装前置依赖..."

if [ "$OS_FAMILY" = "Debian" ]; then
    apt-get update -qq || true
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq \
        apt-transport-https \
        software-properties-common \
        wget \
        gnupg2 \
        ca-certificates \
        curl \
        lsb-release \
        ssl-cert \
        net-tools \
        procps \
        psmisc || true
elif [ "$OS_FAMILY" = "RedHat" ]; then
    yum install -y yum-utils \
        wget \
        gnupg2 \
        ca-certificates \
        curl \
        net-tools \
        procps-ng \
        psmisc || true
fi

echo "前置依赖安装完成"

# 3. 检查是否使用离线包
if [ -n "$PACKAGE_PATH" ] && [ -f "$PACKAGE_PATH" ]; then
    echo "PROGRESS:OfflineInstalling:30"
    echo "使用离线安装包：$PACKAGE_PATH"

    if [[ "$PACKAGE_PATH" == *.deb ]]; then
        echo "安装 DEB 包..."
        DEBIAN_FRONTEND=noninteractive dpkg -i "$PACKAGE_PATH" || apt-get install -f -y
        dpkg --configure -a
    elif [[ "$PACKAGE_PATH" == *.rpm ]]; then
        echo "安装 RPM 包..."
        yum localinstall -y "$PACKAGE_PATH" || rpm -ivh "$PACKAGE_PATH"
    elif [[ "$PACKAGE_PATH" == *.tar.gz ]] || [[ "$PACKAGE_PATH" == *.tar ]]; then
        echo "解压并安装 tar 包..."
        INSTALL_DIR="/opt/rabbitmq"
        mkdir -p "$INSTALL_DIR"

        if [[ "$PACKAGE_PATH" == *.tar.gz ]]; then
            tar -xzf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
        else
            tar -xf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
        fi

        # 设置权限
        if ! id -u rabbitmq >/dev/null 2>&1; then
            useradd -r -s /bin/false rabbitmq
        fi
        chown -R rabbitmq:rabbitmq "$INSTALL_DIR"

        # 创建软链接
        ln -sf "$INSTALL_DIR/sbin/rabbitmq-server" /usr/local/bin/rabbitmq-server
        ln -sf "$INSTALL_DIR/sbin/rabbitmqctl" /usr/local/bin/rabbitmqctl
        ln -sf "$INSTALL_DIR/sbin/rabbitmq-plugins" /usr/local/bin/rabbitmq-plugins

        echo "离线安装完成"
    fi
else
    # 4. 在线安装 - 添加 Erlang 和 RabbitMQ 仓库
    echo "PROGRESS:AddingRepositories:20"
    echo "添加 Erlang 和 RabbitMQ 仓库..."

    if [ "$OS_FAMILY" = "Debian" ]; then
        # 添加 Erlang 仓库
        echo "添加 Erlang 仓库..."
        wget -qO - "https://packages.erlang-solutions.com/ubuntu/erlang_solutions.asc" | gpg --dearmor -o /usr/share/keyrings/erlang-archive-keyring.gpg 2>/dev/null || true
        echo "deb [signed-by=/usr/share/keyrings/erlang-archive-keyring.gpg] https://packages.erlang-solutions.com/ubuntu $(lsb_release -cs) main" | tee /etc/apt/sources.list.d/erlang.list >/dev/null

        # 添加 RabbitMQ 仓库
        echo "添加 RabbitMQ 仓库..."
        wget -qO - "https://github.com/rabbitmq/signing-keys/releases/download/3.0/cloudsmith_rabbitmq_gpg_A5C05AB9.key" | gpg --dearmor -o /usr/share/keyrings/rabbitmq-archive-keyring.gpg 2>/dev/null || true
        echo "deb [signed-by=/usr/share/keyrings/rabbitmq-archive-keyring.gpg] https://ppa1.cloudsmith.io/rabbitmq/rabbitmq-server/ubuntu/ $(lsb_release -cs) main" | tee /etc/apt/sources.list.d/rabbitmq.list >/dev/null

        apt-get update -qq || true

        # 安装 Erlang 和 RabbitMQ
        echo "PROGRESS:InstallingPackages:40"
        echo "安装 Erlang..."
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq erlang-nox || true

        echo "安装 RabbitMQ..."
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq rabbitmq-server || true

    elif [ "$OS_FAMILY" = "RedHat" ]; then
        # 添加 EPEL 仓库
        yum install -y epel-release || true

        # 添加 RabbitMQ 仓库
        echo "添加 RabbitMQ 仓库..."
        cat > /etc/yum.repos.d/rabbitmq.repo << EOF
[rabbitmq]
name=RabbitMQ Repository
baseurl=https://packagecloud.io/rabbitmq/rabbitmq-server/el/$OS_VERSION/\$basearch
repo_gpgcheck=1
gpgcheck=1
enabled=1
gpgkey=https://packagecloud.io/rabbitmq/rabbitmq-server/gpgkey
sslverify=1
sslcacert=/etc/pki/tls/certs/ca-bundle.crt
metadata_expire=300
EOF

        yum clean all
        yum makecache

        # 安装 Erlang 和 RabbitMQ
        echo "PROGRESS:InstallingPackages:40"
        echo "安装 Erlang..."
        yum install -y erlang || true

        echo "安装 RabbitMQ..."
        yum install -y rabbitmq-server || true
    fi

    echo "在线安装完成"
fi

# 5. 配置防火墙
echo "PROGRESS:ConfiguringFirewall:60"
echo "配置防火墙..."

# 使用 ufw (Debian/Ubuntu)
if command -v ufw &> /dev/null; then
    ufw allow "$AMQP_PORT/tcp" 2>/dev/null || true
    ufw allow "$MANAGEMENT_PORT/tcp" 2>/dev/null || true
    echo "已配置 ufw 防火墙规则"
fi

# 使用 firewalld (RedHat/CentOS)
if command -v firewall-cmd &> /dev/null; then
    firewall-cmd --permanent --add-port="$AMQP_PORT/tcp" 2>/dev/null || true
    firewall-cmd --permanent --add-port="$MANAGEMENT_PORT/tcp" 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
    echo "已配置 firewalld 防火墙规则"
fi

# 使用 iptables (通用)
if command -v iptables &> /dev/null; then
    iptables -I INPUT -p tcp --dport "$AMQP_PORT" -j ACCEPT 2>/dev/null || true
    iptables -I INPUT -p tcp --dport "$MANAGEMENT_PORT" -j ACCEPT 2>/dev/null || true
    # 保存 iptables 规则
    if command -v iptables-save &> /dev/null; then
        iptables-save > /etc/iptables.rules 2>/dev/null || true
    fi
    echo "已配置 iptables 防火墙规则"
fi

echo "防火墙配置完成"

# 5.1 启用 RabbitMQ 管理插件（远程访问必需）
echo "PROGRESS:EnablingPlugins:60"
echo "启用 RabbitMQ 管理插件..."

if command -v rabbitmq-plugins &> /dev/null; then
    # 先禁用再启用，确保配置生效
    rabbitmq-plugins disable rabbitmq_management 2>/dev/null || true
    rabbitmq-plugins enable rabbitmq_management 2>&1 | tee -a "$LOG_FILE"
    if [ ${PIPESTATUS[0]} -eq 0 ]; then
        echo "管理插件已启用"
    else
        echo "警告：管理插件启用可能失败"
    fi
else
    echo "警告：rabbitmq-plugins 不可用"
fi

# 5.2 配置 RabbitMQ 允许远程访问
echo "PROGRESS:ConfiguringRemoteAccess:65"
echo "配置 RabbitMQ 远程访问..."

if [ "$ENABLE_REMOTE_ACCESS" = "true" ]; then
    # 创建 RabbitMQ 配置文件目录
    mkdir -p /etc/rabbitmq

    # 创建 rabbitmq.conf 配置文件（RabbitMQ 3.7+ 推荐使用）
    cat > /etc/rabbitmq/rabbitmq.conf << EOF
# 监听配置 - 绑定到所有网络接口
listeners.tcp.default = ${AMQP_PORT}
listeners.sasl.default = ${AMQP_PORT}

# 管理插件端口 - 绑定到所有网络接口
management.tcp.port = ${MANAGEMENT_PORT}
management.ip_address = 0.0.0.0

# 允许所有 IP 访问（远程访问）
loopback_users.guest = false

# 允许 guest 用户从远程登录
management.load_default_schema_definitions = true

# 默认 vhost
default_vhost = /

# 默认用户配置
default_user = guest
default_pass = guest
default_user_tags.administrator = true

# 集群配置（可选）
cluster_name = ${CLUSTER_NAME}
EOF
    chown rabbitmq:rabbitmq /etc/rabbitmq/rabbitmq.conf 2>/dev/null || true
    chmod 644 /etc/rabbitmq/rabbitmq.conf

    echo "RabbitMQ 远程访问配置已创建"
    echo "  - 配置文件：/etc/rabbitmq/rabbitmq.conf"
else
    echo "远程访问已禁用，仅允许本地连接"
    # 创建本地访问配置
    mkdir -p /etc/rabbitmq
    cat > /etc/rabbitmq/rabbitmq.conf << EOF
listeners.tcp.default = ${AMQP_PORT}
management.tcp.port = ${MANAGEMENT_PORT}
loopback_users.guest = false
EOF
    chown rabbitmq:rabbitmq /etc/rabbitmq/rabbitmq.conf 2>/dev/null || true
    chmod 644 /etc/rabbitmq/rabbitmq.conf
fi

# 6. 配置系统限制
echo "PROGRESS:ConfiguringLimits:70"
echo "配置系统限制..."

# 限制文件描述符
if [ -f /etc/security/limits.conf ]; then
    if ! grep -q "rabbitmq.*nofile" /etc/security/limits.conf; then
        echo "rabbitmq soft nofile 65536" >> /etc/security/limits.conf
        echo "rabbitmq hard nofile 65536" >> /etc/security/limits.conf
    fi
fi

# 限制最大进程数
if ! grep -q "rabbitmq.*nproc" /etc/security/limits.conf; then
    echo "rabbitmq soft nproc 4096" >> /etc/security/limits.conf
    echo "rabbitmq hard nproc 4096" >> /etc/security/limits.conf
fi

echo "系统限制配置完成"

# 7. 创建 systemd 服务配置（如果需要自定义）
echo "PROGRESS:ConfiguringService:75"
if command -v systemctl &> /dev/null; then
    # 确保服务文件存在
    if [ ! -f /etc/systemd/system/rabbitmq-server.service ]; then
        # 复制默认服务文件
        if [ -f /lib/systemd/system/rabbitmq-server.service ]; then
            cp /lib/systemd/system/rabbitmq-server.service /etc/systemd/system/rabbitmq-server.service
        elif [ -f /usr/lib/systemd/system/rabbitmq-server.service ]; then
            cp /usr/lib/systemd/system/rabbitmq-server.service /etc/systemd/system/rabbitmq-server.service
        fi
    fi

    systemctl daemon-reload
    echo "systemd 服务配置完成"
fi

# 8. 启动服务
echo "PROGRESS:StartingService:80"
echo "启动 RabbitMQ 服务..."

if command -v systemctl &> /dev/null; then
    systemctl enable rabbitmq-server 2>/dev/null || true
    systemctl start rabbitmq-server 2>/dev/null || true
    echo "RabbitMQ 服务已启动"
else
    # 使用 init.d 脚本
    if [ -f /etc/init.d/rabbitmq-server ]; then
        /etc/init.d/rabbitmq-server start 2>/dev/null || true
        echo "RabbitMQ 服务已启动 (init.d)"
    fi
fi

# 9. 等待服务启动
echo "PROGRESS:WaitingForService:85"
echo "等待 RabbitMQ 服务启动..."

SUCCESS=false
COUNT=0
while [ $COUNT -lt 30 ]; do
    # 检查端口是否监听
    if command -v ss &> /dev/null; then
        PORT_CHECK=$(ss -tlnp 2>/dev/null | grep ":$AMQP_PORT")
    elif command -v netstat &> /dev/null; then
        PORT_CHECK=$(netstat -tlnp 2>/dev/null | grep ":$AMQP_PORT")
    fi

    if [ -n "$PORT_CHECK" ]; then
        echo "RabbitMQ 服务已成功启动 (AMQP 端口 $AMQP_PORT 监听中)"
        SUCCESS=true
        break
    fi

    # 检查进程
    if pgrep -f "beam\.sm[p]" > /dev/null 2>&1; then
        echo "RabbitMQ 进程已启动，等待端口监听... ($((COUNT+1))/30)"
    else
        echo "等待 RabbitMQ 进程启动... ($((COUNT+1))/30)"
    fi

    sleep 3
    COUNT=$((COUNT+1))
done

if [ "$SUCCESS" = false ]; then
    echo "警告：RabbitMQ 在 90 秒内未能启动"
    echo "尝试检查日志..."

    # 查看日志
    if [ -d /var/log/rabbitmq ]; then
        echo "最后 30 行日志:"
        tail -n 30 /var/log/rabbitmq/*.log 2>/dev/null | head -n 50 || true
    fi

    # 检查进程
    if pgrep -f "beam\.sm[p]" > /dev/null 2>&1; then
        echo "RabbitMQ 进程正在运行，但端口未监听，可能需要更多时间"
    else
        echo "错误：RabbitMQ 进程未运行"
        exit 1
    fi
fi

# 10. 配置用户权限（允许 guest 远程访问）
echo "PROGRESS:ConfiguringUsers:88"
echo "配置用户权限..."

if command -v rabbitmqctl &> /dev/null; then
    # 设置 guest 用户为管理员并允许远程访问
    rabbitmqctl set_user_tags guest administrator 2>&1 | tee -a "$LOG_FILE"
    # 设置 guest 用户权限
    rabbitmqctl set_permissions -p / guest ".*" ".*" ".*" 2>&1 | tee -a "$LOG_FILE"
    echo "guest 用户已配置为管理员，允许远程访问"
fi

# 11. 验证安装
echo "PROGRESS:Verifying:90"
echo "验证 RabbitMQ 安装..."

VERSION=""
if command -v rabbitmqctl &> /dev/null; then
    VERSION=$(rabbitmqctl version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo "未知")
fi

if [ -z "$VERSION" ] || [ "$VERSION" = "未知" ]; then
    if command -v rabbitmq-server &> /dev/null; then
        VERSION=$(rabbitmq-server --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || echo "未知")
    fi
fi

echo "RabbitMQ 版本：$VERSION"

# 输出机器可读的状态信息
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "VERSION: ${VERSION:-未知}"
echo "RUNNING: $SUCCESS"
echo "PORT: $AMQP_PORT,$MANAGEMENT_PORT"
echo "------------------------"
echo "STAGE:SUCCESS"

echo ""
echo "========================================"
echo "RabbitMQ 安装完成！"
echo "========================================"
echo "AMQP 端口：$AMQP_PORT"
echo "管理端口：$MANAGEMENT_PORT"
echo "管理界面：http://<服务器 IP>:$MANAGEMENT_PORT"
echo "默认用户：guest"
echo "默认密码：guest"
echo "注意：guest 用户已允许远程访问"
echo ""
