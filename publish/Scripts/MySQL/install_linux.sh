#!/bin/bash
set -euo pipefail

# ==========================================
# 环境初始化
# ==========================================
export TERM=dumb
export DEBIAN_FRONTEND=noninteractive
export DEBCONF_NONINTERACTIVE_SEEN=true
export DEBCONF_FRONTEND=noninteractive
export APT_KEY_DONT_WARN_ON_DANGEROUS_USAGE=1

# ==========================================
# 参数定义
# ==========================================
PACKAGE_PATH=${PACKAGE_PATH:-}
ROOT_PASSWORD=${ROOT_PASSWORD:-MySql@123}
PORT=${PORT:-3306}
ALLOW_REMOTE=${ALLOW_REMOTE:-true}

LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

# ==========================================
# 终极服务名检测：先看文件，再看 systemd
# ==========================================
detect_mysql_service() {
    if [ -f /usr/lib/systemd/system/mysqld.service ]; then
        echo "mysqld"
        return
    fi
    if [ -f /usr/lib/systemd/system/mysql.service ]; then
        echo "mysql"
        return
    fi
    if systemctl list-unit-files | grep -q 'mysql.service' 2>/dev/null; then
        echo "mysql"
        return
    fi
    if systemctl list-unit-files | grep -q 'mysqld.service' 2>/dev/null; then
        echo "mysqld"
        return
    fi
    echo "mysqld"
}

# ==========================================
# MySQL 就绪检测
# ==========================================
wait_mysql_ready() {
    local service_name=$1
    echo "======================================"
    echo "正在等待 MySQL 服务就绪..."

    echo "1/3 检查服务状态: $service_name"
    for i in {1..30}; do
        status=$(systemctl is-active $service_name 2>/dev/null || echo "unknown")
        if [ "$status" == "active" ]; then
            echo "   ✅ 服务已正常运行"
            break
        elif [ "$status" == "failed" ]; then
            echo "   ❌ 服务启动失败！日志: journalctl -u $service_name"
            exit 1
        fi
        echo "   等待服务启动... ($i/30) 当前状态: $status"
        sleep 2
    done

    echo "2/3 检查端口 $PORT 监听状态"
    for i in {1..20}; do
        if ss -tulpn | grep -q ":$PORT "; then
            echo "   ✅ 端口 $PORT 已正常监听"
            break
        fi
        echo "   等待端口就绪... ($i/20)"
        sleep 2
    done

    echo "3/3 检查 MySQL 连接响应"
    for i in {1..15}; do
        if timeout 5s mysql --connect_timeout=3 -uroot -e "SELECT 1" 2>/dev/null; then
            echo "   ✅ MySQL 已就绪，可以执行 SQL"
            echo "======================================"
            return 0
        fi
        echo "   等待连接响应... ($i/15)"
        sleep 2
    done
    echo "警告：MySQL 就绪超时，尝试继续..."
    echo "======================================"
}

# ==========================================
# 主流程
# ==========================================
echo "PROGRESS:Initializing:5"
echo "MySQL 安装脚本开始..."
echo "当前工作目录: $(pwd)"

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 清理旧的残留
echo "清理旧的 MySQL/MariaDB 残留..."
systemctl stop mysql 2>/dev/null || true
systemctl stop mysqld 2>/dev/null || true

# 针对 update-alternatives 报错修复：如果 fallback 不存在但被 alternatives 引用，会导致配置失败
if [ -f /etc/debian_version ]; then
    echo "检查并修复 update-alternatives 状态..."
    if update-alternatives --display my.cnf >/dev/null 2>&1; then
        update-alternatives --remove-all my.cnf 2>/dev/null || true
    fi
    # 强制清理可能残留的损坏链接
    rm -f /etc/alternatives/my.cnf 2>/dev/null || true
fi

dpkg --remove --force-remove-reinstreq mysql-community-server mysql-community-server-core mysql-community-client mysql-common mysql-client mysql-server 2>/dev/null || true
apt-get purge -y mysql* libmysqlclient* mariadb* libmariadb* 2>/dev/null || true
apt-get autoremove -y 2>/dev/null || true
apt-get autoclean 2>/dev/null || true
rm -rf /var/lib/mysql /etc/mysql /var/log/mysql* /var/log/mysqld.log 2>/dev/null || true

# OS 检测
OS=""
if [ -f /etc/debian_version ]; then
    OS="Debian"
    echo "正在准备 Debian 非交互安装环境..."
    
    echo "更新 apt 源并修复可能存在的状态问题..."
    # 彻底清理锁
    rm -f /var/lib/dpkg/lock-frontend /var/lib/apt/lists/lock /var/cache/apt/archives/lock 2>/dev/null || true
    rm -f /var/lib/dpkg/lock 2>/dev/null || true
    
    # 优先修复已中断的 dpkg 状态
    dpkg --configure -a || true
    
    # 针对顽固的 "held broken packages"，尝试清除可能导致冲突的临时状态
    apt-get clean -qq || true
    
    # 增加 --allow-releaseinfo-change 应对系统版本变更
    echo "正在拉取最新的软件包索引..."
    if ! apt-get update -qq --allow-releaseinfo-change; then
        echo "⚠️  apt-get update 遇到错误（可能是第三方源失效），尝试清理索引缓存后重试..."
        rm -rf /var/lib/apt/lists/*
        apt-get update -qq --allow-releaseinfo-change || true
    fi

    # 预修复依赖
    apt-get install -f -y -qq || true
    apt-get autoremove -y -qq || true
    
    echo "预安装基础工具..."
    # 采用重试机制和更激进的修复参数
    for i in {1..3}; do
        if DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --fix-broken --fix-missing debconf-utils apt-utils iproute2 expect; then
            echo "✅ 基础工具安装成功"
            break
        fi
        if [ $i -eq 3 ]; then
            echo "❌ 基础工具安装在 3 次尝试后依然失败。这通常意味着系统 apt 环境存在严重冲突。"
            echo "尝试使用 --ignore-hold 参数强制安装..."
            DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --fix-broken --fix-missing --ignore-hold debconf-utils apt-utils iproute2 expect || true
        else
            echo "⚠️ 基础工具安装尝试 $i 失败，执行系统级依赖修复..."
            apt-get install -f -y -qq || true
            dpkg --configure -a || true
            apt-get update -qq --allow-releaseinfo-change || true
        fi
    done
    
    echo "预安装 MySQL 核心依赖..."
    # 增加 --ignore-missing，因为有些系统可能已经有类似包或无法连接某些源
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --ignore-missing --fix-broken \
        libmecab2 \
        libjson-perl \
        mecab-ipadic-utf8 \
        mecab-utils \
        numactl || {
        echo "警告：部分核心依赖安装失败，依赖 apt-get install -f 进行自动补全"
        apt-get install -f -y -qq || true
    }

    if command -v debconf-set-selections >/dev/null 2>&1; then
        echo "注入静默安装参数..."
        pkgs=(
            "mysql-server" "mysql-community-server" "mysql-server-8.0" "mysql-server-5.7"
        )
        for pkg in "${pkgs[@]}"; do
            echo "$pkg mysql-server/root_password password $ROOT_PASSWORD" | debconf-set-selections
            echo "$pkg mysql-server/root_password_again password $ROOT_PASSWORD" | debconf-set-selections
            echo "$pkg mysql-server/start-on-boot boolean true" | debconf-set-selections
            echo "$pkg $pkg/root_password password $ROOT_PASSWORD" | debconf-set-selections
            echo "$pkg $pkg/root-pass password $ROOT_PASSWORD" | debconf-set-selections
            echo "$pkg mysql-server/root_password seen true" | debconf-set-selections
            echo "$pkg $pkg/default-auth-override select Use Strong Password Encryption (RECOMMENDED)" | debconf-set-selections
        done
    fi
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    echo "不支持的操作系统"
    exit 1
fi
echo "检测到操作系统: $OS"

HAS_LOCAL_PACKAGE=false
if [ -n "$PACKAGE_PATH" ] && [ -f "$PACKAGE_PATH" ]; then
    HAS_LOCAL_PACKAGE=true
    echo "使用本地安装包: $PACKAGE_PATH"
else
    echo "未指定本地包，将在线安装..."
fi

echo "PROGRESS:Installing:15"

IS_BINARY=false
EXTRACT_DIR=""

if [ "$HAS_LOCAL_PACKAGE" = true ]; then
    case "$PACKAGE_PATH" in
        *.tar*|*.tgz)
            echo "处理 tar 安装包..."
            EXTRACT_DIR="/tmp/mysql_extract_$(date +%s)"
            rm -rf "$EXTRACT_DIR"
            mkdir -p "$EXTRACT_DIR"
            tar -xf "$PACKAGE_PATH" -C "$EXTRACT_DIR" --no-same-owner
            
            if [ "$OS" == "RedHat" ] && ls "$EXTRACT_DIR"/*.rpm >/dev/null 2>&1; then
                yum remove -y mariadb-libs || true
                yum localinstall -y "$EXTRACT_DIR"/*.rpm
            elif [ "$OS" == "Debian" ] && ls "$EXTRACT_DIR"/*.deb >/dev/null 2>&1; then
                echo "按正确顺序静默安装 DEB 包..."
                OLD_PWD=$(pwd)
                cd "$EXTRACT_DIR"
                DPKG_OPTS="--force-confdef --force-confold"
                
                echo "  1/8 安装 mysql-common..."
                # 在安装前再次清理 alternatives 状态以确保万无一失
                update-alternatives --remove-all my.cnf 2>/dev/null || true
                dpkg $DPKG_OPTS -i mysql-common_*.deb || {
                    echo "⚠️ mysql-common 安装失败，尝试修复 alternatives 后重试..."
                    update-alternatives --remove-all my.cnf 2>/dev/null || true
                    # 尝试强制安装，跳过部分配置错误
                    dpkg --force-all -i mysql-common_*.deb
                }
                
                echo "  2/8 安装 client-plugins..."
                dpkg $DPKG_OPTS -i mysql-community-client-plugins_*.deb
                
                echo "  3/8 安装 libmysqlclient..."
                dpkg $DPKG_OPTS -i libmysqlclient*.deb
                
                echo "  4/8 安装 client-core..."
                dpkg $DPKG_OPTS -i mysql-community-client-core_*.deb
                
                echo "  5/8 安装 community-client..."
                dpkg $DPKG_OPTS -i mysql-community-client_*.deb
                
                echo "  6/8 安装 client meta..."
                dpkg $DPKG_OPTS -i mysql-client_*.deb
                
                echo "  7/8 安装 server-core..."
                dpkg $DPKG_OPTS -i mysql-community-server-core_*.deb
                
                echo "  8/8 安装 community-server..."
                expect -c "
                    set timeout 60
                    spawn dpkg $DPKG_OPTS -i mysql-community-server_*.deb
                    expect {
                        \"Enter root password:\" { send \"$ROOT_PASSWORD\r\"; exp_continue }
                        \"Password:\" { send \"$ROOT_PASSWORD\r\"; exp_continue }
                        eof
                    }
                "
                
                echo "  9/9 安装 server meta..."
                dpkg $DPKG_OPTS -i mysql-server_*.deb
                
                # 【关键】这里会自动安装缺失的依赖（包括 libaio1/libaio1t64）
                echo "修复剩余依赖并确保所有包正确安装..."
                DEBIAN_FRONTEND=noninteractive apt-get install -f -y -qq || {
                    echo "第二次尝试修复依赖..."
                    apt-get update -qq
                    DEBIAN_FRONTEND=noninteractive apt-get install -f -y -qq
                }
                
                cd "$OLD_PWD"
            elif [ -d "$EXTRACT_DIR"/*/bin ] || [ -d "$EXTRACT_DIR"/bin ]; then
                echo "二进制安装..."
                if [ -d "$EXTRACT_DIR"/bin ]; then SRC_ROOT="$EXTRACT_DIR"; else SRC_ROOT=$(dirname $(ls -d "$EXTRACT_DIR"/*/bin | head -n 1)); fi
                if [ "$OS" == "Debian" ]; then DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --ignore-missing numactl; fi
                groupadd mysql || true; useradd -r -g mysql -s /bin/false mysql || true
                INSTALL_DIR="/usr/local/mysql"
                rm -rf "$INSTALL_DIR"; mkdir -p "/usr/local"; cp -R "$SRC_ROOT" "$INSTALL_DIR"; chown -R mysql:mysql "$INSTALL_DIR"
                mkdir -p "$INSTALL_DIR/mysql-files"; chown mysql:mysql "$INSTALL_DIR/mysql-files"; chmod 750 "$INSTALL_DIR/mysql-files"
                "$INSTALL_DIR/bin/mysqld" --initialize-insecure --user=mysql --basedir="$INSTALL_DIR" --datadir="$INSTALL_DIR/data"
                ln -sf "$INSTALL_DIR/bin/mysql" /usr/bin/mysql; ln -sf "$INSTALL_DIR/bin/mysqld" /usr/sbin/mysqld
                cat > /etc/systemd/system/mysqld.service <<EOF
[Unit]
Description=MySQL Server
After=network.target
[Service]
User=mysql
Group=mysql
ExecStart=$INSTALL_DIR/bin/mysqld --defaults-file=/etc/my.cnf
[Install]
WantedBy=multi-user.target
EOF
                if [ ! -f /etc/my.cnf ]; then
                    cat > /etc/my.cnf <<EOF
[mysqld]
basedir=$INSTALL_DIR
datadir=$INSTALL_DIR/data
port=$PORT
bind-address=0.0.0.0
EOF
                fi
                systemctl daemon-reload; systemctl enable mysqld
                IS_BINARY=true
            else
                echo "无法识别包内容"
                rm -rf "$EXTRACT_DIR"
                exit 1
            fi
            rm -rf "$EXTRACT_DIR"
            ;;
        *.deb)
            dpkg --force-confdef --force-confold -i "$PACKAGE_PATH" || DEBIAN_FRONTEND=noninteractive apt-get install -f -y
            ;;
        *.rpm)
            yum localinstall -y "$PACKAGE_PATH"
            ;;
    esac
else
    if [ "$OS" == "Debian" ]; then
        apt-get update -qq || true
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq mysql-server-8.0 || apt-get install -y -qq mysql-server
    else
        yum install -y mysql-server
    fi
fi

# 校验安装
echo "校验安装结果..."
if [ ! -f /usr/sbin/mysqld ]; then
    echo "❌ 安装失败：未找到 mysqld 二进制文件，请检查上面的安装日志！"
    exit 1
fi
echo "✅ 二进制文件检查通过：/usr/sbin/mysqld"

# 刷新 systemd
echo "刷新 systemd 服务配置..."
systemctl daemon-reload
sleep 2

# 检测服务名
service_name=$(detect_mysql_service)
echo "检测到服务名: $service_name"

# 启动服务
echo "启动服务: $service_name"
systemctl start $service_name

echo "PROGRESS:Configuring:40"
echo "修改配置文件..."

# 修改配置
CONF_FILES=("/etc/mysql/mysql.conf.d/mysqld.cnf" "/etc/mysql/my.cnf" "/etc/my.cnf" "/etc/my.cnf.d/mysql-server.cnf")
SELECTED_CONF=""
for f in "${CONF_FILES[@]}"; do
    if [ -f "$f" ]; then SELECTED_CONF="$f"; break; fi
done

if [ -n "$SELECTED_CONF" ]; then
    cp "$SELECTED_CONF" "${SELECTED_CONF}.bak"
    if grep -q "^[[:space:]]*port" "$SELECTED_CONF"; then
        sed -i "s|^[[:space:]]*port.*|port = $PORT|" "$SELECTED_CONF"
    elif grep -q "\[mysqld\]" "$SELECTED_CONF"; then
        sed -i "/\[mysqld\]/a port = $PORT" "$SELECTED_CONF"
    fi
    if [ "$ALLOW_REMOTE" == "true" ]; then
        if grep -q "^[[:space:]]*bind-address" "$SELECTED_CONF"; then
            sed -i "s|^[[:space:]]*bind-address.*|bind-address = 0.0.0.0|" "$SELECTED_CONF"
        elif grep -q "\[mysqld\]" "$SELECTED_CONF"; then
             sed -i "/\[mysqld\]/a bind-address = 0.0.0.0" "$SELECTED_CONF"
        fi
    fi
fi

# 重启服务
echo "重启服务应用新配置: $service_name"
systemctl restart $service_name

echo "PROGRESS:SettingPassword:60"

# 等待就绪
wait_mysql_ready $service_name

# 执行 SQL
TEMP_PWD=""
if [ "$IS_BINARY" != "true" ]; then
    LOG_FILES=("/var/log/mysqld.log" "/var/log/mysql/error.log")
    for log_path in "${LOG_FILES[@]}"; do
        if [ -f "$log_path" ]; then
            TEMP_PWD=$(grep 'temporary password' "$log_path" | awk '{print $NF}' | tail -n 1)
            [ -n "$TEMP_PWD" ] && break
        fi
    done
fi

execute_sql() {
    local sql=$1
    timeout 10s mysql --connect_timeout=5 -uroot -e "$sql" 2>/dev/null || \
    ( [ -n "$TEMP_PWD" ] && timeout 10s mysql --connect_timeout=5 --connect-expired-password -uroot -p"$TEMP_PWD" -e "$sql" 2>/dev/null ) || \
    timeout 10s mysql --connect_timeout=5 -uroot -p"$ROOT_PASSWORD" -e "$sql" 2>/dev/null || \
    echo "SQL 执行跳过: $sql"
    return 0
}

execute_sql "ALTER USER 'root'@'localhost' IDENTIFIED BY '$ROOT_PASSWORD';"
execute_sql "SET PASSWORD FOR 'root'@'localhost' = PASSWORD('$ROOT_PASSWORD');"

if [ "$ALLOW_REMOTE" == "true" ]; then
    execute_sql "CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '$ROOT_PASSWORD';"
    execute_sql "ALTER USER 'root'@'%' IDENTIFIED BY '$ROOT_PASSWORD';"
    execute_sql "GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;"
fi

echo "PROGRESS:Starting:80"
echo "最后验证..."

SUCCESS=false
for i in {1..10}; do
    if timeout 10s mysqladmin --connect_timeout=5 -uroot -p"$ROOT_PASSWORD" -P"$PORT" -h127.0.0.1 ping >/dev/null 2>&1; then
        SUCCESS=true
        break
    fi
    sleep 2
done

if [ "$SUCCESS" = true ]; then
    echo "PROGRESS:Complete:100"
    echo "🎉 MySQL 8.0 静默安装完成！"
    echo "连接信息: Host=127.0.0.1, Port=$PORT, User=root, Password=$ROOT_PASSWORD"
else
    echo "❌ 最终验证失败，请检查日志: $LOG_FILE"
    exit 1
fi