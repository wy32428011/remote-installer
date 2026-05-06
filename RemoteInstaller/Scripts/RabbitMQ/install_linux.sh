#!/bin/bash
set -euo pipefail

fail() {
    echo "错误：$1"
    exit 1
}

# 参数定义
# PACKAGE_PATH: 远程安装包路径（可选，用于离线安装）
# AMQP_PORT: AMQP 端口 (默认 5672)
# MANAGEMENT_PORT: 管理端口 (默认 15672)
# CLUSTER_NAME: 集群名称 (默认 rabbitmq-cluster)
# NODE_NAME: 节点名称 (默认 rabbit@localhost)
# USERNAME: 登录用户名 (默认 guest)
# PASSWORD: 登录密码 (默认 guest)

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
USERNAME="${USERNAME:-guest}"
PASSWORD="${PASSWORD:-guest}"
ENABLE_REMOTE_ACCESS="${ENABLE_REMOTE_ACCESS:-true}"
PACKAGE_IS_FILE=false
PACKAGE_IS_DIRECTORY=false
IS_DEBIAN_OFFLINE=false

if [ -n "$PACKAGE_PATH" ]; then
    if [ -f "$PACKAGE_PATH" ]; then
        PACKAGE_IS_FILE=true
        if [[ "$PACKAGE_PATH" == *.deb ]]; then
            IS_DEBIAN_OFFLINE=true
        fi
    elif [ -d "$PACKAGE_PATH" ]; then
        PACKAGE_IS_DIRECTORY=true
        if find "$PACKAGE_PATH" -maxdepth 1 -type f -name "rabbitmq-server*.deb" | grep -q .; then
            IS_DEBIAN_OFFLINE=true
        fi
    else
        fail "PACKAGE_PATH 不存在：$PACKAGE_PATH"
    fi
fi

resolve_package_directory() {
    local package_path=$1

    if [ -d "$package_path" ]; then
        echo "$package_path"
    else
        dirname "$package_path"
    fi
}

get_package_search_directories() {
    local package_dir=$1

    echo "$package_dir"
    if [ -d "$package_dir/deps" ]; then
        echo "$package_dir/deps"
    fi
}

ENABLE_REMOTE_ACCESS_NORMALIZED=$(printf "%s" "$ENABLE_REMOTE_ACCESS" | tr '[:upper:]' '[:lower:]')
IS_REMOTE_ACCESS_ENABLED=true
case "$ENABLE_REMOTE_ACCESS_NORMALIZED" in
    false|0|no|off)
        IS_REMOTE_ACCESS_ENABLED=false
        ;;
    true|1|yes|on|"")
        IS_REMOTE_ACCESS_ENABLED=true
        ;;
    *)
        echo "警告：ENABLE_REMOTE_ACCESS 值无效（$ENABLE_REMOTE_ACCESS），默认启用远程访问"
        IS_REMOTE_ACCESS_ENABLED=true
        ;;
esac

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
    else
        OS="RedHat"
    fi

    # 优先使用 rpm 提取 RHEL 主版本，避免从 release 文本中提取出多行数字（如 7/9/2009）
    OS_VERSION=$(rpm -E '%{?rhel}' 2>/dev/null | tr -cd '0-9' || true)
    if [ -z "$OS_VERSION" ]; then
        OS_VERSION=$(sed -n 's/.*release \([0-9][0-9]*\).*/\1/p' /etc/redhat-release | head -n 1 || true)
    fi
    OS_VERSION="${OS_VERSION:-7}"
else
    echo "不支持的操作系统"
    exit 1
fi

echo "检测到操作系统：$OS $OS_VERSION ($OS_FAMILY)"

# 2. 安装前置依赖
echo "PROGRESS:InstallingDependencies:15"
echo "安装前置依赖..."

if [ "$OS_FAMILY" = "Debian" ]; then
    if [ "$IS_DEBIAN_OFFLINE" = true ]; then
        echo "Ubuntu 离线模式：跳过在线前置依赖安装"
    else
        DEBIAN_FRONTEND=noninteractive apt-get update -qq || true
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
    fi
elif [ "$OS_FAMILY" = "RedHat" ]; then
    if [ "${OS_VERSION}" = "7" ] && [ -n "$PACKAGE_PATH" ] && [ -f "$PACKAGE_PATH" ]; then
        echo "CentOS7 离线模式：跳过在线前置依赖安装"
    else
        yum install -y yum-utils \
            wget \
            gnupg2 \
            ca-certificates \
            curl \
            net-tools \
            procps-ng \
            psmisc \
            xz || true
    fi
fi

echo "前置依赖安装完成"

# 3. 检查是否使用离线包
if [ "$OS_FAMILY" = "RedHat" ] && [ "${OS_VERSION}" = "7" ]; then
    if [ -z "$PACKAGE_PATH" ] || { [ "$PACKAGE_IS_FILE" != true ] && [ "$PACKAGE_IS_DIRECTORY" != true ]; }; then
        fail "CentOS7 仅支持离线安装，请提供有效的 PACKAGE_PATH（RabbitMQ 离线目录或 rabbitmq-server*.rpm）"
    fi
fi

if [ -n "$PACKAGE_PATH" ] && { [ "$PACKAGE_IS_FILE" = true ] || [ "$PACKAGE_IS_DIRECTORY" = true ]; }; then
    echo "PROGRESS:OfflineInstalling:30"
    echo "使用离线安装资源：$PACKAGE_PATH"

    if [ "$OS_FAMILY" = "Debian" ] && [ "$IS_DEBIAN_OFFLINE" = true ]; then
        echo "安装 DEB 离线目录..."
        PACKAGE_DIR=$(resolve_package_directory "$PACKAGE_PATH")

        DEB_FILES=()
        ERLANG_DEB_FILES=()
        RABBITMQ_DEB_FILES=()
        OTHER_DEB_FILES=()
        ERLANG_BASE_PACKAGE_NAME=""
        ERLANG_BASE_VERSION=""
        RABBITMQ_PACKAGE_NAME=""
        RABBITMQ_PACKAGE_VERSION=""

        while IFS= read -r search_dir; do
            while IFS= read -r debFile; do
                [ -n "$debFile" ] || continue
                DEB_FILES+=("$debFile")
                debName=$(basename "$debFile")
                case "$debName" in
                    erlang-*.deb)
                        ERLANG_DEB_FILES+=("$debFile")
                        if [[ "$debName" == erlang-base*.deb ]] && [ -z "$ERLANG_BASE_PACKAGE_NAME" ]; then
                            ERLANG_BASE_PACKAGE_NAME="$debName"
                        fi
                        ;;
                    rabbitmq-server*.deb)
                        RABBITMQ_DEB_FILES+=("$debFile")
                        if [ -z "$RABBITMQ_PACKAGE_NAME" ]; then
                            RABBITMQ_PACKAGE_NAME="$debName"
                        fi
                        ;;
                    *)
                        OTHER_DEB_FILES+=("$debFile")
                        ;;
                esac
            done < <(find "$search_dir" -maxdepth 1 -type f -name "*.deb" | sort)
        done < <(get_package_search_directories "$PACKAGE_DIR")

        if [ ${#DEB_FILES[@]} -le 1 ]; then
            fail "Ubuntu RabbitMQ 离线安装需要完整的 Erlang/依赖 .deb 包集合，当前目录仅检测到主包，无法保证服务和管理界面正常启动"
        fi

        if [ ${#ERLANG_DEB_FILES[@]} -eq 0 ]; then
            fail "Ubuntu RabbitMQ 离线安装缺少 Erlang .deb 包，无法继续安装"
        fi

        if [ ${#RABBITMQ_DEB_FILES[@]} -eq 0 ]; then
            fail "Ubuntu RabbitMQ 离线安装缺少 rabbitmq-server*.deb 主包，无法继续安装"
        fi

        if [ -z "$ERLANG_BASE_PACKAGE_NAME" ]; then
            fail "Ubuntu RabbitMQ 离线安装缺少 erlang-base*.deb，无法校验 Erlang 版本兼容性"
        fi

        if ! printf '%s\n' "${OTHER_DEB_FILES[@]}" | grep -Eq '/logrotate_.*\.deb$'; then
            fail "Ubuntu RabbitMQ 离线安装缺少 logrotate*.deb，无法保证离线安装完整性"
        fi

        ERLANG_BASE_VERSION=$(printf "%s" "$ERLANG_BASE_PACKAGE_NAME" | grep -oE '[0-9]+\.[0-9]+(\.[0-9]+)?' | head -n 1 || true)
        RABBITMQ_PACKAGE_VERSION=$(printf "%s" "$RABBITMQ_PACKAGE_NAME" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)

        if [[ "$RABBITMQ_PACKAGE_VERSION" == 3.12.* ]]; then
            if [[ ! "$ERLANG_BASE_VERSION" =~ ^25\.|^26\. ]]; then
                fail "RabbitMQ 3.12.x 仅支持 Erlang 25.x 或 26.x，当前检测到 erlang-base 版本：${ERLANG_BASE_VERSION:-未知}"
            fi
        fi

        DPKG_OPTS="--force-confdef --force-confold"
        set +e
        echo "先安装 Erlang 离线包..."
        DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "${ERLANG_DEB_FILES[@]}"
        ERLANG_INSTALL_EXIT=$?
        DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a

        if [ ${#OTHER_DEB_FILES[@]} -gt 0 ]; then
            echo "安装其他离线依赖包（logrotate 等）..."
            DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "${OTHER_DEB_FILES[@]}"
            DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a
        fi

        echo "再安装 RabbitMQ 离线包..."
        DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "${RABBITMQ_DEB_FILES[@]}"
        RABBITMQ_INSTALL_EXIT=$?
        DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a

        FULL_PASS_EXIT=0
        if [ $ERLANG_INSTALL_EXIT -ne 0 ] || [ $RABBITMQ_INSTALL_EXIT -ne 0 ]; then
            echo "分组安装未完全成功，最后统一安装全部离线包..."
            DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "${DEB_FILES[@]}"
            FULL_PASS_EXIT=$?
            DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a
        fi
        set -e

        if [ $ERLANG_INSTALL_EXIT -ne 0 ] || [ $RABBITMQ_INSTALL_EXIT -ne 0 ] || [ $FULL_PASS_EXIT -ne 0 ]; then
            fail "Ubuntu 离线依赖不完整或版本不兼容，dpkg 安装失败（已禁用在线修复）"
        fi

        if ! dpkg -l 2>/dev/null | grep -i 'rabbitmq-server' | grep -q '^ii'; then
            fail "RabbitMQ 离线安装未完成：未检测到 rabbitmq-server 已安装"
        fi
    elif [ "$OS_FAMILY" = "RedHat" ] && [ "${OS_VERSION}" = "7" ]; then
        echo "安装 RPM 离线目录..."
        PACKAGE_DIR=$(resolve_package_directory "$PACKAGE_PATH")
        RABBITMQ_RPM=$(find "$PACKAGE_DIR" -maxdepth 1 -type f -name "rabbitmq-server-*.rpm" | head -n 1 || true)
        ERLANG_RPM=$(find "$PACKAGE_DIR" -maxdepth 1 -type f -name "erlang-*.el7*.rpm" | head -n 1 || true)

        [ -n "$RABBITMQ_RPM" ] || fail "CentOS7 离线目录缺少 rabbitmq-server*.rpm 主包"
        [ -n "$ERLANG_RPM" ] || fail "未找到 CentOS7 离线依赖包 erlang-*.el7*.rpm"

        echo "先安装 Erlang 离线包：$ERLANG_RPM"
        yum localinstall -y "$ERLANG_RPM"
        echo "再安装 RabbitMQ 主包：$RABBITMQ_RPM"
        yum localinstall -y "$RABBITMQ_RPM"
    elif [ "$PACKAGE_IS_FILE" = true ] && ([[ "$PACKAGE_PATH" == *.tar.gz ]] || [[ "$PACKAGE_PATH" == *.tar ]] || [[ "$PACKAGE_PATH" == *.tar.xz ]] || [[ "$PACKAGE_PATH" == *.tgz ]]); then
        echo "解压并安装 tar 包..."
        INSTALL_DIR="/opt/rabbitmq"
        mkdir -p "$INSTALL_DIR"

        if [[ "$PACKAGE_PATH" == *.tar.gz ]] || [[ "$PACKAGE_PATH" == *.tgz ]]; then
            tar -xzf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
        elif [[ "$PACKAGE_PATH" == *.tar.xz ]]; then
            tar -xJf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
        else
            tar -xf "$PACKAGE_PATH" -C "$INSTALL_DIR" --strip-components=1
        fi

        # 设置权限
        if ! id -u rabbitmq >/dev/null 2>&1; then
            useradd -r -s /bin/false rabbitmq
        fi
        chown -R rabbitmq:rabbitmq "$INSTALL_DIR"

        # 校验关键可执行文件
        [ -x "$INSTALL_DIR/sbin/rabbitmq-server" ] || fail "离线包缺少 rabbitmq-server: $INSTALL_DIR/sbin/rabbitmq-server"
        [ -x "$INSTALL_DIR/sbin/rabbitmqctl" ] || fail "离线包缺少 rabbitmqctl: $INSTALL_DIR/sbin/rabbitmqctl"
        [ -x "$INSTALL_DIR/sbin/rabbitmq-plugins" ] || fail "离线包缺少 rabbitmq-plugins: $INSTALL_DIR/sbin/rabbitmq-plugins"

        # 创建软链接
        ln -sf "$INSTALL_DIR/sbin/rabbitmq-server" /usr/local/bin/rabbitmq-server
        ln -sf "$INSTALL_DIR/sbin/rabbitmqctl" /usr/local/bin/rabbitmqctl
        ln -sf "$INSTALL_DIR/sbin/rabbitmq-plugins" /usr/local/bin/rabbitmq-plugins

        echo "离线安装完成"
    else
        fail "不支持的离线包格式：$PACKAGE_PATH（支持目录型 .deb/.rpm 离线资源或单文件 .tar/.tar.gz/.tar.xz/.tgz）"
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

        DEBIAN_FRONTEND=noninteractive apt-get update -qq || true

        # 安装 Erlang 和 RabbitMQ
        echo "PROGRESS:InstallingPackages:40"
        echo "安装 Erlang..."
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq erlang-nox || true

        echo "安装 RabbitMQ..."
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq rabbitmq-server || true

    elif [ "$OS_FAMILY" = "RedHat" ]; then
        # 添加 EPEL 仓库（可选）
        yum install -y epel-release || true

        # 添加 Erlang 仓库
        echo "添加 Erlang 仓库..."
        cat > /etc/yum.repos.d/erlang.repo << EOF
[rabbitmq_erlang]
name=RabbitMQ Erlang Repository
baseurl=https://packagecloud.io/rabbitmq/erlang/el/$OS_VERSION/\$basearch
enabled=1
gpgcheck=1
repo_gpgcheck=1
gpgkey=https://packagecloud.io/rabbitmq/erlang/gpgkey
sslverify=1
sslcacert=/etc/pki/tls/certs/ca-bundle.crt
metadata_expire=300
EOF

        # 添加 RabbitMQ 仓库
        echo "添加 RabbitMQ 仓库..."
        cat > /etc/yum.repos.d/rabbitmq.repo << EOF
[rabbitmq]
name=RabbitMQ Repository
baseurl=https://packagecloud.io/rabbitmq/rabbitmq-server/el/$OS_VERSION/\$basearch
enabled=1
gpgcheck=1
repo_gpgcheck=1
gpgkey=https://packagecloud.io/rabbitmq/rabbitmq-server/gpgkey
sslverify=1
sslcacert=/etc/pki/tls/certs/ca-bundle.crt
metadata_expire=300
EOF

        yum clean all
        yum makecache fast

        # 安装 Erlang 和 RabbitMQ
        echo "PROGRESS:InstallingPackages:40"
        echo "安装 Erlang..."
        yum install -y erlang

        echo "安装 RabbitMQ..."
        yum install -y rabbitmq-server

        if ! command -v rabbitmqctl &> /dev/null; then
            [ -x /usr/sbin/rabbitmqctl ] || fail "RabbitMQ 安装后未找到 rabbitmqctl，请检查 CentOS 仓库与依赖"
            ln -sf /usr/sbin/rabbitmqctl /usr/local/bin/rabbitmqctl
        fi
    fi

    echo "在线安装完成"
fi

# 5. 配置防火墙
echo "PROGRESS:ConfiguringFirewall:60"
echo "配置防火墙..."

FIREWALL_CHECK_TOOL="none"
FIREWALL_ACTIVE=false
FIREWALL_MGMT_ALLOWED=true

# 使用 ufw (Debian/Ubuntu)
if command -v ufw &> /dev/null; then
    if ufw allow "$AMQP_PORT/tcp" >/dev/null 2>&1; then
        echo "ufw 已放行 AMQP 端口：$AMQP_PORT/tcp"
    else
        echo "警告：ufw 放行 AMQP 端口失败：$AMQP_PORT/tcp"
    fi

    if ufw allow "$MANAGEMENT_PORT/tcp" >/dev/null 2>&1; then
        echo "ufw 已放行管理端口：$MANAGEMENT_PORT/tcp"
    else
        echo "警告：ufw 放行管理端口失败：$MANAGEMENT_PORT/tcp"
    fi

    if ufw status 2>/dev/null | grep -q "Status: active"; then
        FIREWALL_CHECK_TOOL="ufw"
        FIREWALL_ACTIVE=true
        if ufw status 2>/dev/null | grep -Eq "(^|[[:space:]])${MANAGEMENT_PORT}/tcp([[:space:]]|$)"; then
            FIREWALL_MGMT_ALLOWED=true
            echo "ufw 校验通过：管理端口 $MANAGEMENT_PORT/tcp 已放通"
        else
            FIREWALL_MGMT_ALLOWED=false
            echo "警告：ufw 处于启用状态，但未检测到管理端口 $MANAGEMENT_PORT/tcp 放通"
        fi
    else
        echo "ufw 未启用，跳过 ufw 放通校验"
    fi
fi

# 使用 firewalld (RedHat/CentOS)
if [ "$FIREWALL_CHECK_TOOL" = "none" ] && command -v firewall-cmd &> /dev/null; then
    FIREWALLD_STATE=$(firewall-cmd --state 2>/dev/null || true)
    if [ "$FIREWALLD_STATE" = "running" ]; then
        FIREWALL_CHECK_TOOL="firewalld"
        FIREWALL_ACTIVE=true

        if firewall-cmd --permanent --add-port="$AMQP_PORT/tcp" >/dev/null 2>&1; then
            echo "firewalld 已放行 AMQP 端口：$AMQP_PORT/tcp"
        else
            echo "警告：firewalld 放行 AMQP 端口失败：$AMQP_PORT/tcp"
        fi

        if firewall-cmd --permanent --add-port="$MANAGEMENT_PORT/tcp" >/dev/null 2>&1; then
            echo "firewalld 已放行管理端口：$MANAGEMENT_PORT/tcp"
        else
            echo "警告：firewalld 放行管理端口失败：$MANAGEMENT_PORT/tcp"
        fi

        if firewall-cmd --reload >/dev/null 2>&1; then
            echo "firewalld 已重载配置"
        else
            echo "警告：firewalld 重载失败"
        fi

        if firewall-cmd --list-ports 2>/dev/null | grep -Eq "(^|[[:space:]])${MANAGEMENT_PORT}/tcp([[:space:]]|$)"; then
            FIREWALL_MGMT_ALLOWED=true
            echo "firewalld 校验通过：管理端口 $MANAGEMENT_PORT/tcp 已放通"
        else
            FIREWALL_MGMT_ALLOWED=false
            echo "警告：firewalld 处于运行状态，但未检测到管理端口 $MANAGEMENT_PORT/tcp 放通"
        fi
    else
        echo "firewalld 未运行，跳过 firewalld 放通校验"
    fi
fi

# 使用 iptables (通用)
if [ "$FIREWALL_CHECK_TOOL" = "none" ] && command -v iptables &> /dev/null; then
    if iptables -I INPUT -p tcp --dport "$AMQP_PORT" -j ACCEPT >/dev/null 2>&1; then
        echo "iptables 已放行 AMQP 端口：$AMQP_PORT"
    else
        echo "警告：iptables 放行 AMQP 端口失败：$AMQP_PORT"
    fi

    if iptables -I INPUT -p tcp --dport "$MANAGEMENT_PORT" -j ACCEPT >/dev/null 2>&1; then
        echo "iptables 已放行管理端口：$MANAGEMENT_PORT"
    else
        echo "警告：iptables 放行管理端口失败：$MANAGEMENT_PORT"
    fi

    if command -v iptables-save &> /dev/null; then
        if iptables-save > /etc/iptables.rules 2>/dev/null; then
            echo "iptables 规则已保存到 /etc/iptables.rules"
        else
            echo "警告：iptables 规则保存失败"
        fi
    fi

    IPTABLES_POLICY=$(iptables -S INPUT 2>/dev/null | head -n 1 || true)
    if printf "%s" "$IPTABLES_POLICY" | grep -Eq -- "-P INPUT (DROP|REJECT)" || iptables -S INPUT 2>/dev/null | grep -Eq -- "-A INPUT .* -j (DROP|REJECT)"; then
        FIREWALL_CHECK_TOOL="iptables"
        FIREWALL_ACTIVE=true
        if iptables -C INPUT -p tcp --dport "$MANAGEMENT_PORT" -j ACCEPT >/dev/null 2>&1; then
            FIREWALL_MGMT_ALLOWED=true
            echo "iptables 校验通过：管理端口 $MANAGEMENT_PORT 已放通"
        else
            FIREWALL_MGMT_ALLOWED=false
            echo "警告：iptables 存在限制规则，但未检测到管理端口 $MANAGEMENT_PORT 放通"
        fi
    else
        echo "iptables 未检测到限制性规则，跳过强制放通校验"
    fi
fi

if [ "$IS_REMOTE_ACCESS_ENABLED" = true ] && [ "$FIREWALL_ACTIVE" = true ] && [ "$FIREWALL_MGMT_ALLOWED" = false ]; then
    fail "检测到防火墙（$FIREWALL_CHECK_TOOL）已启用，但管理端口 ${MANAGEMENT_PORT}/tcp 未放通，远程访问不可用"
fi

echo "防火墙配置完成"

# 5.1 启用 RabbitMQ 管理插件（远程访问必需）
echo "PROGRESS:EnablingPlugins:60"
echo "启用 RabbitMQ 管理插件..."

RABBITMQ_PLUGINS_BIN=""
if command -v rabbitmq-plugins &> /dev/null; then
    RABBITMQ_PLUGINS_BIN="$(command -v rabbitmq-plugins)"
elif [ -x /usr/sbin/rabbitmq-plugins ]; then
    RABBITMQ_PLUGINS_BIN="/usr/sbin/rabbitmq-plugins"
elif [ -x /usr/lib/rabbitmq/bin/rabbitmq-plugins ]; then
    RABBITMQ_PLUGINS_BIN="/usr/lib/rabbitmq/bin/rabbitmq-plugins"
else
    fail "rabbitmq-plugins 不可用"
fi

# 离线启用插件（不依赖节点在线）
if "$RABBITMQ_PLUGINS_BIN" enable --offline rabbitmq_management >> "$LOG_FILE" 2>&1; then
    echo "管理插件离线启用成功"
else
    fail "管理插件离线启用失败"
fi

# 5.2 配置 RabbitMQ 允许远程访问
echo "PROGRESS:ConfiguringRemoteAccess:65"
echo "配置 RabbitMQ 远程访问..."

if [ "$IS_REMOTE_ACCESS_ENABLED" = true ]; then
    # 创建 RabbitMQ 配置文件目录
    mkdir -p /etc/rabbitmq

    # 创建 rabbitmq.conf 配置文件（RabbitMQ 3.7+ 推荐使用）
    cat > /etc/rabbitmq/rabbitmq.conf << EOF
# 监听配置 - 绑定到所有网络接口
listeners.tcp.1 = 0.0.0.0:${AMQP_PORT}

# 管理插件端口 - 绑定到所有网络接口
management.tcp.port = ${MANAGEMENT_PORT}
management.tcp.ip = 0.0.0.0

# 允许所有用户（含 guest）远程访问
loopback_users = none

# 默认 vhost
default_vhost = /

# 默认用户配置
default_user = ${USERNAME}
default_pass = ${PASSWORD}
default_user_tags.administrator = true

# 集群配置（可选）
cluster_name = ${CLUSTER_NAME}
EOF
    chown rabbitmq:rabbitmq /etc/rabbitmq/rabbitmq.conf 2>/dev/null || true
    chmod 644 /etc/rabbitmq/rabbitmq.conf

    if [ ! -f /etc/rabbitmq/rabbitmq.conf ]; then
        fail "RabbitMQ 配置文件创建失败：/etc/rabbitmq/rabbitmq.conf"
    fi

    echo "RabbitMQ 远程访问配置已创建"
    echo "  - 配置文件：/etc/rabbitmq/rabbitmq.conf"
else
    echo "远程访问已禁用，仅允许本地连接"
    # 创建本地访问配置
    mkdir -p /etc/rabbitmq
    cat > /etc/rabbitmq/rabbitmq.conf << EOF
listeners.tcp.1 = 127.0.0.1:${AMQP_PORT}
management.tcp.port = ${MANAGEMENT_PORT}
management.tcp.ip = 127.0.0.1
EOF
    chown rabbitmq:rabbitmq /etc/rabbitmq/rabbitmq.conf 2>/dev/null || true
    chmod 644 /etc/rabbitmq/rabbitmq.conf

    if [ ! -f /etc/rabbitmq/rabbitmq.conf ]; then
        fail "RabbitMQ 配置文件创建失败：/etc/rabbitmq/rabbitmq.conf"
    fi
fi

# 兼容 Ubuntu/Debian：显式写入 enabled_plugins，确保管理插件在服务启动时加载
cat > /etc/rabbitmq/enabled_plugins << 'EOF'
[rabbitmq_management].
EOF
chown rabbitmq:rabbitmq /etc/rabbitmq/enabled_plugins 2>/dev/null || true
chmod 644 /etc/rabbitmq/enabled_plugins

if [ ! -f /etc/rabbitmq/enabled_plugins ]; then
    fail "RabbitMQ 插件配置文件创建失败：/etc/rabbitmq/enabled_plugins"
fi

echo "RabbitMQ 插件配置文件已创建"
echo "  - 配置文件：/etc/rabbitmq/enabled_plugins"

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
    if systemctl is-active --quiet rabbitmq-server; then
        systemctl restart rabbitmq-server
        echo "RabbitMQ 服务已重启并加载最新配置"
    else
        systemctl start rabbitmq-server
        echo "RabbitMQ 服务已启动"
    fi
else
    # 使用 init.d 脚本
    if [ -f /etc/init.d/rabbitmq-server ]; then
        /etc/init.d/rabbitmq-server start
        echo "RabbitMQ 服务已启动 (init.d)"
    else
        fail "未找到可用的 rabbitmq-server 启动方式"
    fi
fi

# 9. 等待服务启动
echo "PROGRESS:WaitingForService:85"
echo "等待 RabbitMQ 服务启动..."

SUCCESS=false
COUNT=0
while [ $COUNT -lt 30 ]; do
    PORT_CHECK=""
    # 检查端口是否监听
    if command -v ss &> /dev/null; then
        PORT_CHECK=$(ss -tlnp 2>/dev/null | grep ":$AMQP_PORT" || true)
    elif command -v netstat &> /dev/null; then
        PORT_CHECK=$(netstat -tlnp 2>/dev/null | grep ":$AMQP_PORT" || true)
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
        fail "RabbitMQ 进程存在但端口未监听，请检查 Erlang 依赖与服务日志"
    else
        fail "RabbitMQ 进程未运行"
    fi
fi

if command -v rabbitmqctl &> /dev/null; then
    rabbitmqctl await_startup
fi

# 9.1 在线启用并校验管理插件（确保控制台可用）
if "$RABBITMQ_PLUGINS_BIN" enable rabbitmq_management >> "$LOG_FILE" 2>&1; then
    echo "管理插件在线启用成功"
else
    fail "管理插件在线启用失败"
fi

if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet rabbitmq-server; then
        systemctl restart rabbitmq-server
    else
        systemctl start rabbitmq-server
    fi
elif [ -f /etc/init.d/rabbitmq-server ]; then
    /etc/init.d/rabbitmq-server restart
fi

if command -v rabbitmqctl &> /dev/null; then
    rabbitmqctl await_startup
fi

if "$RABBITMQ_PLUGINS_BIN" list -E -m 2>/dev/null | grep -qx "rabbitmq_management"; then
    echo "管理插件状态校验通过"
else
    fail "管理插件未启用，管理控制台不可用"
fi

if [ ! -f /etc/rabbitmq/rabbitmq.conf ]; then
    fail "未找到 RabbitMQ 配置文件：/etc/rabbitmq/rabbitmq.conf"
fi

if [ ! -f /etc/rabbitmq/enabled_plugins ]; then
    fail "未找到 RabbitMQ 插件配置文件：/etc/rabbitmq/enabled_plugins"
fi

# 9.2 管理端口与 HTTP 就绪探测（避免仅端口监听就误判成功）
echo "PROGRESS:CheckingManagementPort:87"
echo "检测管理端口 ${MANAGEMENT_PORT} 就绪状态..."

MGMT_READY=false
MGMT_BIND_ALL=false
MANAGEMENT_HTTP_READY=false
MGMT_COUNT=0
while [ $MGMT_COUNT -lt 20 ]; do
    MGMT_PORT_CHECK=""
    if command -v ss &> /dev/null; then
        MGMT_PORT_CHECK=$(ss -tlnp 2>/dev/null | grep ":$MANAGEMENT_PORT" || true)
    elif command -v netstat &> /dev/null; then
        MGMT_PORT_CHECK=$(netstat -tlnp 2>/dev/null | grep ":$MANAGEMENT_PORT" || true)
    fi

    if [ -n "$MGMT_PORT_CHECK" ]; then
        MGMT_BIND_LINE=$(printf "%s\n" "$MGMT_PORT_CHECK" | head -n 1)
        if printf "%s" "$MGMT_BIND_LINE" | grep -Eq "0\.0\.0\.0:$MANAGEMENT_PORT|\*:$MANAGEMENT_PORT|\[::\]:$MANAGEMENT_PORT"; then
            MGMT_BIND_ALL=true
            echo "管理端口 ${MANAGEMENT_PORT} 已监听且全网卡绑定：$MGMT_BIND_LINE"
        else
            MGMT_BIND_ALL=false
            echo "管理端口 ${MANAGEMENT_PORT} 已监听，但当前仅本地绑定：$MGMT_BIND_LINE"
        fi

        if command -v curl &> /dev/null; then
            HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:${MANAGEMENT_PORT}/api/overview" || true)
            case "$HTTP_STATUS" in
                200|301|302|401|403)
                    MANAGEMENT_HTTP_READY=true
                    echo "管理 HTTP 接口已响应：/api/overview -> $HTTP_STATUS"
                    ;;
                *)
                    MANAGEMENT_HTTP_READY=false
                    echo "管理 HTTP 接口暂未就绪：/api/overview -> ${HTTP_STATUS:-无响应}"
                    ;;
            esac
        else
            echo "系统未安装 curl，跳过管理 HTTP 校验，回退到端口监听校验"
        fi

        if [ "$IS_REMOTE_ACCESS_ENABLED" = true ]; then
            if [ "$MGMT_BIND_ALL" = true ]; then
                if command -v curl &> /dev/null; then
                    if [ "$MANAGEMENT_HTTP_READY" = true ]; then
                        MGMT_READY=true
                        break
                    fi
                else
                    MGMT_READY=true
                    break
                fi
            fi
        else
            if command -v curl &> /dev/null; then
                if [ "$MANAGEMENT_HTTP_READY" = true ]; then
                    MGMT_READY=true
                    break
                fi
            else
                MGMT_READY=true
                break
            fi
        fi
    fi

    echo "等待管理端口监听... ($((MGMT_COUNT+1))/20)"
    sleep 3
    MGMT_COUNT=$((MGMT_COUNT+1))
done

if [ "$MGMT_READY" = false ]; then
    if command -v curl &> /dev/null; then
        fail "管理端口 ${MANAGEMENT_PORT} 已监听但管理 HTTP 接口未就绪，管理控制台不可访问"
    elif [ "$IS_REMOTE_ACCESS_ENABLED" = true ]; then
        fail "管理端口 ${MANAGEMENT_PORT} 未达到远程访问要求（需非回环绑定）"
    else
        fail "管理端口 ${MANAGEMENT_PORT} 未监听，管理控制台不可访问"
    fi
fi

# 9.3 远程访问最终校验（防止误报成功）
if [ "$IS_REMOTE_ACCESS_ENABLED" = true ]; then
    if [ "$MGMT_BIND_ALL" != true ]; then
        fail "远程访问已启用，但管理端口 ${MANAGEMENT_PORT} 未全网卡绑定"
    fi
fi

# 10. 配置用户权限（按参数创建/更新用户）
echo "PROGRESS:ConfiguringUsers:88"
echo "配置用户权限..."

if command -v rabbitmqctl &> /dev/null; then
    # 进一步等待 RabbitMQ 内部启动完成
    rabbitmqctl await_startup

    if rabbitmqctl list_users | awk '{print $1}' | grep -qx "$USERNAME"; then
        echo "用户 $USERNAME 已存在，更新密码..."
        rabbitmqctl change_password "$USERNAME" "$PASSWORD" 2>&1 | tee -a "$LOG_FILE"
    else
        echo "创建用户 $USERNAME ..."
        rabbitmqctl add_user "$USERNAME" "$PASSWORD" 2>&1 | tee -a "$LOG_FILE"
    fi

    rabbitmqctl set_user_tags "$USERNAME" administrator 2>&1 | tee -a "$LOG_FILE"
    rabbitmqctl set_permissions -p / "$USERNAME" ".*" ".*" ".*" 2>&1 | tee -a "$LOG_FILE"
    echo "用户 $USERNAME 已配置为管理员并授予权限"
else
    fail "rabbitmqctl 不可用，无法完成用户授权"
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
echo "MANAGEMENT_HTTP_READY: $MANAGEMENT_HTTP_READY"
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
echo "默认用户：$USERNAME"
echo "默认密码：$PASSWORD"
echo "注意：已为用户 $USERNAME 配置管理权限"
echo ""
