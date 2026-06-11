
#!/bin/bash
set -e

# 参数定义
# PACKAGE_PATH: 远程安装包路径
# PASSWORD: Redis 密码 (默认无)
# PORT: 服务端口 (默认 6379)
# ALLOW_REMOTE: 是否允许远程访问 (默认 true)

# 日志设置
LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "Redis 安装脚本开始..."
echo "当前工作目录: $(pwd)"
echo "日志文件: $(pwd)/$LOG_FILE"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 检查参数
PASSWORD=${PASSWORD:-}
PORT=${PORT:-6379}
ALLOW_REMOTE=${ALLOW_REMOTE:-true}

echo "安装参数："
echo "  PORT=$PORT"
echo "  PASSWORD=${PASSWORD:+<已设置>}${PASSWORD:-<未设置>}"
echo "  ALLOW_REMOTE=$ALLOW_REMOTE"
echo "  PACKAGE_PATH=${PACKAGE_PATH:-<未指定>}"

# 检查 OS
if [ -f /etc/debian_version ]; then
    OS="Debian"
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    echo "错误：不支持的操作系统"
    exit 1
fi
echo "检测到操作系统: $OS"

# 检查是否有本地包（支持单文件或目录）
PACKAGE_IS_FILE=false
PACKAGE_IS_DIRECTORY=false
if [ -n "${PACKAGE_PATH:-}" ] && [ -f "$PACKAGE_PATH" ]; then
    HAS_LOCAL_PACKAGE=true
    PACKAGE_IS_FILE=true
    PACKAGE_SIZE=$(du -h "$PACKAGE_PATH" 2>/dev/null | cut -f1)
    echo "使用本地安装包: $PACKAGE_PATH (大小: $PACKAGE_SIZE)"
elif [ -n "${PACKAGE_PATH:-}" ] && [ -d "$PACKAGE_PATH" ]; then
    HAS_LOCAL_PACKAGE=true
    PACKAGE_IS_DIRECTORY=true
    FILE_COUNT=$(find "$PACKAGE_PATH" -type f | wc -l | tr -d ' ')
    echo "使用本地安装包目录: $PACKAGE_PATH (文件数: $FILE_COUNT)"
else
    HAS_LOCAL_PACKAGE=false
    echo "未指定有效的本地安装包，将尝试通过包管理器在线安装..."
fi

echo "PROGRESS:Installing:15"

# 兼容旧版平铺目录与新增 deps 子目录。
get_debian_dependency_search_directories() {
    local package_root=$1

    if [ -d "$package_root/deps" ]; then
        echo "$package_root/deps"
    fi

    echo "$package_root"
}

# 返回当前离线安装需要预检的 Debian 依赖包。
get_debian_offline_dependency_packages() {
    if [ "$OS" != "Debian" ]; then
        return 0
    fi

    local version_id=${VERSION_ID:-}
    if [ -z "$version_id" ] && [ -f /etc/os-release ]; then
        version_id=$(grep '^VERSION_ID=' /etc/os-release | head -n 1 | cut -d'=' -f2 | tr -d '"')
    fi

    cat <<EOF
adduser
libatomic1
libjemalloc2
liblzf1
libsystemd0
sysvinit-utils
EOF

    if [[ "$version_id" == 24* ]]; then
        echo "libssl3t64"
    else
        echo "libssl3"
    fi
}

# 已安装依赖直接跳过，避免重复安装。
is_debian_package_installed() {
    local package_name=$1
    dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null | grep -q 'install ok installed'
}

# 优先从 deps 中找依赖，找不到再兼容旧版根目录平铺。
find_debian_dependency_file() {
    local package_root=$1
    local package_name=$2
    local search_dir
    local file

    while IFS= read -r search_dir; do
        for file in "$search_dir"/"${package_name}"_*.deb "$search_dir"/"${package_name}"-*.deb; do
            if [ -f "$file" ]; then
                echo "$file"
                return 0
            fi
        done
    done < <(get_debian_dependency_search_directories "$package_root")

    return 1
}

# 先补齐缺失依赖，再进入 Redis 主包安装。
prepare_debian_offline_dependencies() {
    local package_root=$1
    local dependency_name
    local dependency_file
    local -a missing_packages=()
    local -a install_files=()

    while IFS= read -r dependency_name; do
        if [ -z "$dependency_name" ]; then
            continue
        fi

        if is_debian_package_installed "$dependency_name"; then
            echo "离线依赖已安装，跳过：$dependency_name"
            continue
        fi

        if ! dependency_file=$(find_debian_dependency_file "$package_root" "$dependency_name"); then
            missing_packages+=("$dependency_name")
            continue
        fi

        install_files+=("$dependency_file")
    done < <(get_debian_offline_dependency_packages)

    if [ ${#missing_packages[@]} -gt 0 ]; then
        echo "错误：离线目录缺少以下 Redis Debian 依赖包："
        printf '%s\n' "${missing_packages[@]}"
        echo "请将对应 .deb 文件放入目录：$package_root/deps（兼容旧布局时也可放在根目录）"
        exit 1
    fi

    if [ ${#install_files[@]} -eq 0 ]; then
        echo "Redis 离线依赖已满足，无需额外安装。"
        return 0
    fi

    echo "正在安装缺失的 Redis Debian 离线依赖包..."
    DEBIAN_FRONTEND=noninteractive dpkg --force-confdef --force-confold -i "${install_files[@]}"
}

# 根目录只负责主包，依赖包由 prepare 阶段提前处理。
install_debian_from_directory() {
    local package_root=$1
    local -a redis_server_debs=()
    local -a redis_tools_debs=()

    mapfile -t redis_server_debs < <(find "$package_root" -maxdepth 1 -type f -name "redis-server*.deb" | sort)
    mapfile -t redis_tools_debs < <(find "$package_root" -maxdepth 1 -type f -name "redis-tools*.deb" | sort)

    if [ ${#redis_server_debs[@]} -eq 0 ]; then
        echo "错误：离线目录中缺少 redis-server*.deb：$package_root"
        exit 1
    fi

    if [ ${#redis_tools_debs[@]} -eq 0 ]; then
        echo "错误：离线目录中缺少 redis-tools*.deb：$package_root"
        exit 1
    fi

    echo "安装 redis-tools..."
    DEBIAN_FRONTEND=noninteractive dpkg --force-confdef --force-confold -i "${redis_tools_debs[@]}"

    echo "安装 redis-server..."
    DEBIAN_FRONTEND=noninteractive dpkg --force-confdef --force-confold -i "${redis_server_debs[@]}"
}

# 1. 安装 Redis
IS_SOURCE_INSTALL=false

# 尝试清理旧的 Redis，避免干扰
if [ "$HAS_LOCAL_PACKAGE" = "true" ]; then
    echo "清理可能存在的旧版本..."
    for svc in redis redis-server redis_6379; do
        systemctl stop "$svc" 2>/dev/null || true
        systemctl disable "$svc" 2>/dev/null || true
    done
fi

if [ "$HAS_LOCAL_PACKAGE" = true ]; then
    if [ "$PACKAGE_IS_DIRECTORY" = true ]; then
        if [ "$OS" = "Debian" ]; then
            echo "正在使用离线 DEB 目录安装 Redis..."
            prepare_debian_offline_dependencies "$PACKAGE_PATH"
            install_debian_from_directory "$PACKAGE_PATH"
        elif [ "$OS" = "RedHat" ]; then
            echo "正在使用离线 RPM 目录安装 Redis..."
            mapfile -t RPM_FILES < <(find "$PACKAGE_PATH" -maxdepth 1 -type f -name "redis-*.rpm" | sort)
            if [ ${#RPM_FILES[@]} -eq 0 ]; then
                echo "错误：离线目录中未找到 redis-*.rpm：$PACKAGE_PATH"
                exit 1
            fi
            yum localinstall -y "${RPM_FILES[@]}" || rpm -ivh "${RPM_FILES[@]}"
        fi
    else
        case "$PACKAGE_PATH" in
            *.deb)
                echo "正在使用单个 DEB 包安装 Redis..."
                prepare_debian_offline_dependencies "$(dirname "$PACKAGE_PATH")"
                install_debian_from_directory "$(dirname "$PACKAGE_PATH")"
                ;;
            *.rpm)
                echo "正在使用单个 RPM 包安装 Redis..."
                yum localinstall -y "$PACKAGE_PATH" || rpm -ivh "$PACKAGE_PATH"
                ;;
            *.tar*|*.tgz)
                echo "正在从源码编译安装 Redis..."

                # 安装编译依赖
                echo "安装编译依赖..."
                if [ "$OS" = "RedHat" ]; then
                    yum install -y gcc make tcl pkgconfig
                else
                    (DEBIAN_FRONTEND=noninteractive apt-get update -qq || true) && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq gcc make tcl pkg-config
                fi
                echo "编译依赖安装完成"

                EXTRACT_DIR="/tmp/redis_extract_$(date +%s)"
                mkdir -p "$EXTRACT_DIR"
                echo "解压安装包到: $EXTRACT_DIR"
                tar -xf "$PACKAGE_PATH" -C "$EXTRACT_DIR"

                # 找到解压后的目录名 (可能是 redis-7.2.3 或 redis)
                SRC_DIR=$(find "$EXTRACT_DIR" -maxdepth 1 -mindepth 1 -type d | head -n 1)

                if [ -z "$SRC_DIR" ]; then
                    echo "错误：无法在压缩包中找到有效的目录"
                    ls -la "$EXTRACT_DIR"
                    rm -rf "$EXTRACT_DIR"
                    exit 1
                fi
                echo "源码目录: $SRC_DIR"

                cd "$SRC_DIR"

                # 编译
                CPU_CORES=$(nproc 2>/dev/null || echo 1)
                echo "开始编译 Redis (并行核心数: $CPU_CORES)..."
                if ! make -j"$CPU_CORES" 2>&1; then
                    echo "并行编译失败，尝试单线程编译..."
                    make
                fi
                echo "编译完成，安装二进制文件..."
                make install

                # 验证安装
                echo "验证安装的二进制文件..."
                for bin in redis-server redis-cli; do
                    if [ -f "/usr/local/bin/$bin" ]; then
                        echo "  ✓ /usr/local/bin/$bin"
                    else
                        echo "  ✗ /usr/local/bin/$bin (缺失)"
                    fi
                done

                # 配置目录
                echo "创建配置和数据目录..."
                mkdir -p /etc/redis
                mkdir -p /var/lib/redis
                mkdir -p /var/log/redis
                mkdir -p /var/run/redis

                # 确保复制了 redis.conf，如果不存在则创建默认配置
                if [ -f "redis.conf" ]; then
                    cp redis.conf /etc/redis/redis.conf
                    echo "已复制源码包中的 redis.conf"
                else
                    echo "警告：源码包中未找到 redis.conf，正在创建默认配置..."
                    cat > /etc/redis/redis.conf <<DEFAULTCONF
# Redis 默认配置文件 (由安装脚本自动生成)
bind 127.0.0.1
port 6379
daemonize no
dir /var/lib/redis
logfile /var/log/redis/redis.log
pidfile /var/run/redis/redis.pid
loglevel notice
databases 16
save 900 1
save 300 10
save 60 10000
rdbcompression yes
dbfilename dump.rdb
appendonly no
appendfilename "appendonly.aof"
DEFAULTCONF
                    echo "默认配置文件已创建"
                fi

                # 创建 redis 用户（如果不存在）
                if ! id -u redis >/dev/null 2>&1; then
                    echo "创建 redis 系统用户..."
                    useradd -r -s /sbin/nologin redis 2>/dev/null || useradd -r -s /usr/sbin/nologin redis 2>/dev/null || true
                fi

                # 设置目录权限
                echo "设置目录权限..."
                chown -R redis:redis /var/lib/redis
                chown -R redis:redis /etc/redis
                chown -R redis:redis /var/log/redis
                chown -R redis:redis /var/run/redis

                # 创建 systemd 服务
                echo "创建 systemd 服务单元..."
                cat > /etc/systemd/system/redis.service <<EOF
[Unit]
Description=Redis In-Memory Data Store
After=network.target

[Service]
User=redis
Group=redis
ExecStart=/usr/local/bin/redis-server /etc/redis/redis.conf
ExecStop=/usr/local/bin/redis-cli shutdown
Restart=always
RestartSec=3
LimitNOFILE=65535
RuntimeDirectory=redis
RuntimeDirectoryMode=0755

[Install]
WantedBy=multi-user.target
EOF
                echo "systemd 服务单元已创建: /etc/systemd/system/redis.service"

                IS_SOURCE_INSTALL=true
                cd -
                rm -rf "$EXTRACT_DIR"
                echo "临时解压目录已清理"
                ;;
            *)
                echo "错误：不支持的安装包格式: $PACKAGE_PATH"
                echo "支持的格式: .deb, .rpm, .tar.gz, .tar.xz, .tar.bz2, .tgz"
                exit 1
                ;;
        esac
    fi
else
    # 在线安装
    if [ "$OS" = "Debian" ]; then
        echo "正在通过 apt 安装 Redis..."
        INSTALL_SUCCESS=false
        for i in 1 2 3; do
            echo "安装尝试 $i/3..."
            DEBIAN_FRONTEND=noninteractive apt-get update -qq || true
            if DEBIAN_FRONTEND=noninteractive apt-get install -y -qq redis-server; then
                INSTALL_SUCCESS=true
                echo "apt 安装成功"
                break
            fi
            echo "安装失败，等待重试 ($i/3)..."
            sleep 5
        done
        if [ "$INSTALL_SUCCESS" = "false" ]; then
            echo "错误：通过 apt 安装 Redis 失败"
            exit 1
        fi
    elif [ "$OS" = "RedHat" ]; then
        echo "正在通过 yum 安装 Redis..."
        echo "安装 EPEL 仓库..."
        yum install -y epel-release || true
        yum makecache || true
        INSTALL_SUCCESS=false
        for i in 1 2 3; do
            echo "安装尝试 $i/3..."
            if yum install -y redis; then
                INSTALL_SUCCESS=true
                echo "yum 安装成功"
                break
            fi
            echo "安装失败，等待重试 ($i/3)..."
            sleep 5
        done
        if [ "$INSTALL_SUCCESS" = "false" ]; then
            echo "错误：通过 yum 安装 Redis 失败"
            exit 1
        fi
    fi
fi

# 验证安装
echo "验证 Redis 安装..."
if command -v redis-server >/dev/null 2>&1; then
    REDIS_VERSION=$(redis-server --version 2>/dev/null || echo "版本获取失败")
    echo "Redis 已安装: $REDIS_VERSION"
elif [ -f "/usr/local/bin/redis-server" ]; then
    REDIS_VERSION=$(/usr/local/bin/redis-server --version 2>/dev/null || echo "版本获取失败")
    echo "Redis 已安装 (源码编译): $REDIS_VERSION"
else
    echo "错误：Redis 安装验证失败，未找到 redis-server"
    exit 1
fi

echo "PROGRESS:Configuring:40"
echo "正在配置 Redis..."

# 2. 查找并修改配置
CONF_FILE=""
CONF_SEARCH_PATHS=(
    "/etc/redis/redis.conf"
    "/etc/redis.conf"
    "/etc/redis-server/redis.conf"
    "/usr/local/etc/redis.conf"
)

for path in "${CONF_SEARCH_PATHS[@]}"; do
    if [ -f "$path" ]; then
        CONF_FILE="$path"
        break
    fi
done

# 如果找不到配置文件且是包管理器安装，尝试搜寻
if [ -z "$CONF_FILE" ] && [ "$HAS_LOCAL_PACKAGE" = false ]; then
    POTENTIAL_CONF=$(find /etc -name "redis.conf" 2>/dev/null | head -n 1)
    if [ -n "$POTENTIAL_CONF" ]; then
        CONF_FILE="$POTENTIAL_CONF"
    fi
fi

if [ -n "$CONF_FILE" ] && [ -f "$CONF_FILE" ]; then
    echo "使用配置文件: $CONF_FILE"
    cp "$CONF_FILE" "${CONF_FILE}.bak"
    echo "已备份原始配置: ${CONF_FILE}.bak"
    
    CONF_DIR=$(dirname "$CONF_FILE")
    mkdir -p "$CONF_DIR"
    
    # 配置绑定地址
    echo "配置绑定地址 (ALLOW_REMOTE=$ALLOW_REMOTE)..."
    if [ "$ALLOW_REMOTE" = "true" ]; then
        if grep -q "^[[:space:]]*#\?[[:space:]]*bind[[:space:]]\+127.0.0.1" "$CONF_FILE"; then
            sed -i 's/^[[:space:]]*#\?[[:space:]]*bind[[:space:]]\+127.0.0.1.*/bind 0.0.0.0/' "$CONF_FILE"
        elif grep -q "^[[:space:]]*bind " "$CONF_FILE"; then
            sed -i 's/^[[:space:]]*bind .*/bind 0.0.0.0/' "$CONF_FILE"
        else
            echo "bind 0.0.0.0" >> "$CONF_FILE"
        fi
        echo "  bind 0.0.0.0"
        
        # 关闭保护模式
        if grep -q "^[[:space:]]*#\?[[:space:]]*protected-mode" "$CONF_FILE"; then
            sed -i 's/^[[:space:]]*#\?[[:space:]]*protected-mode.*/protected-mode no/' "$CONF_FILE"
        else
            echo "protected-mode no" >> "$CONF_FILE"
        fi
        echo "  protected-mode no"
    else
        if grep -q "^[[:space:]]*bind " "$CONF_FILE"; then
            sed -i 's/^[[:space:]]*bind .*/bind 127.0.0.1/' "$CONF_FILE"
        fi
        if grep -q "^[[:space:]]*protected-mode" "$CONF_FILE"; then
            sed -i 's/^[[:space:]]*protected-mode.*/protected-mode yes/' "$CONF_FILE"
        fi
        echo "  bind 127.0.0.1"
        echo "  protected-mode yes"
    fi
    
    # 配置端口
    echo "配置端口: $PORT"
    if grep -q "^[[:space:]]*#\?[[:space:]]*port " "$CONF_FILE"; then
        sed -i "s/^[[:space:]]*#\?[[:space:]]*port .*/port $PORT/" "$CONF_FILE"
    else
        echo "port $PORT" >> "$CONF_FILE"
    fi
    
    # 配置密码
    if [ -n "$PASSWORD" ]; then
        echo "配置密码: 已设置"
        if grep -q "^[[:space:]]*#\?[[:space:]]*requirepass" "$CONF_FILE"; then
            sed -i "s/^[[:space:]]*#\?[[:space:]]*requirepass .*/requirepass $PASSWORD/" "$CONF_FILE"
        else
            echo "requirepass $PASSWORD" >> "$CONF_FILE"
        fi
    else
        echo "配置密码: 未设置"
        sed -i 's/^[[:space:]]*requirepass .*/# requirepass ""/' "$CONF_FILE"
    fi
    
    # 强制 daemonize no (systemd 管理)
    echo "配置 daemonize: no (systemd 管理)"
    if grep -q "^[[:space:]]*#\?[[:space:]]*daemonize " "$CONF_FILE"; then
        sed -i 's/^[[:space:]]*#\?[[:space:]]*daemonize .*/daemonize no/' "$CONF_FILE"
    else
        echo "daemonize no" >> "$CONF_FILE"
    fi
    
    # 设置工作目录和日志
    if grep -q "^[[:space:]]*#\?[[:space:]]*dir " "$CONF_FILE"; then
        sed -i 's|^[[:space:]]*#\?[[:space:]]*dir .*|dir /var/lib/redis|' "$CONF_FILE"
    fi
    if grep -q "^[[:space:]]*#\?[[:space:]]*logfile " "$CONF_FILE"; then
        sed -i 's|^[[:space:]]*#\?[[:space:]]*logfile .*|logfile /var/log/redis/redis.log|' "$CONF_FILE"
    fi
    
    # 如果是源码安装，确保配置文件权限正确
    if [ "$IS_SOURCE_INSTALL" = true ]; then
        chown redis:redis "$CONF_FILE" 2>/dev/null || true
    fi
    
    echo "配置修改完成"
else
    echo "警告：未找到 Redis 配置文件，跳过配置步骤"
fi

echo "PROGRESS:Starting:70"
echo "正在启动 Redis 服务..."

# 3. 启动服务
systemctl daemon-reload
sleep 1

# 检测服务名
SERVICE_NAME=""
if systemctl list-unit-files 2>/dev/null | grep -q "redis-server.service"; then
    SERVICE_NAME="redis-server"
elif systemctl list-unit-files 2>/dev/null | grep -q "redis.service"; then
    SERVICE_NAME="redis"
fi

# 如果仍未找到，尝试直接检查服务文件
if [ -z "$SERVICE_NAME" ]; then
    for file in /etc/systemd/system/redis.service /lib/systemd/system/redis.service /usr/lib/systemd/system/redis.service; do
        if [ -f "$file" ]; then
            SERVICE_NAME="redis"
            break
        fi
    done
fi
if [ -z "$SERVICE_NAME" ]; then
    for file in /etc/systemd/system/redis-server.service /lib/systemd/system/redis-server.service /usr/lib/systemd/system/redis-server.service; do
        if [ -f "$file" ]; then
            SERVICE_NAME="redis-server"
            break
        fi
    done
fi

if [ -z "$SERVICE_NAME" ]; then
    echo "错误：找不到 Redis 服务项"
    exit 1
fi

echo "检测到服务名: $SERVICE_NAME"
echo "启用开机自启..."
systemctl enable "$SERVICE_NAME"

echo "启动服务..."
if ! systemctl restart "$SERVICE_NAME" 2>&1; then
    echo "首次启动失败，诊断信息："
    journalctl -u "$SERVICE_NAME" -n 20 --no-pager 2>/dev/null || true
    
    sleep 2
    echo "尝试再次启动..."
    if ! systemctl start "$SERVICE_NAME" 2>&1; then
        echo "错误：Redis 服务启动失败"
        journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
        exit 1
    fi
fi

# 4. 验证
echo "PROGRESS:Verifying:90"
echo "正在验证服务状态..."

VERIFY_SUCCESS=false
for i in $(seq 1 15); do
    sleep 2
    
    SERVICE_STATUS=$(systemctl is-active "$SERVICE_NAME" 2>/dev/null || echo "unknown")
    REDIS_PID=$(pgrep -x redis-server 2>/dev/null || echo "")
    
    if [ -n "$REDIS_PID" ] || [ "$SERVICE_STATUS" = "active" ]; then
        # 尝试 ping Redis
        if [ -n "$PASSWORD" ]; then
            PING_RESULT=$(redis-cli -p "$PORT" -a "$PASSWORD" --no-auth-warning ping 2>/dev/null || echo "")
        else
            PING_RESULT=$(redis-cli -p "$PORT" ping 2>/dev/null || echo "")
        fi
        
        if [ "$PING_RESULT" = "PONG" ]; then
            echo "Redis 服务已成功启动并响应 PING (PID: $REDIS_PID)"
            VERIFY_SUCCESS=true
            break
        fi
    fi
    echo "等待服务就绪 ($i/15)... [状态: $SERVICE_STATUS, PID: ${REDIS_PID:-无}]"
done

if [ "$VERIFY_SUCCESS" = false ]; then
    if pgrep redis-server > /dev/null 2>&1 || systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
        echo "警告：Redis 进程已运行，但 PING 验证未通过（可能是配置问题）"
    else
        echo "错误：Redis 服务未能在启动后运行"
        echo "最近服务日志："
        journalctl -u "$SERVICE_NAME" -n 30 --no-pager 2>/dev/null || true
        exit 1
    fi
fi

# 获取版本
INSTALLED_VERSION=$(redis-server --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1 || echo "未知")
FINAL_RUNNING=false
if [ "$VERIFY_SUCCESS" = true ] || pgrep -x redis-server >/dev/null 2>&1 || systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    FINAL_RUNNING=true
fi

echo "PROGRESS:Complete:100"
echo "Redis 安装完成！"
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "VERSION: ${INSTALLED_VERSION:-未知}"
echo "RUNNING: $FINAL_RUNNING"
echo "PORT: $PORT"
echo "STAGE:SUCCESS"
echo "------------------------"
echo ""
echo "--- 连接信息 ---"
echo "端口: $PORT"
echo "密码: ${PASSWORD:+已设置}${PASSWORD:-无}"
echo "远程访问: $ALLOW_REMOTE"
echo "服务名: $SERVICE_NAME"
echo "版本: $INSTALLED_VERSION"
echo "配置文件: ${CONF_FILE:-默认}"
echo "----------------"
