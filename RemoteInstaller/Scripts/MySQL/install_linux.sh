#!/bin/bash
set -euo pipefail

export TERM=dumb
export DEBIAN_FRONTEND=noninteractive
export DEBCONF_NONINTERACTIVE_SEEN=true
export DEBCONF_FRONTEND=noninteractive
export APT_KEY_DONT_WARN_ON_DANGEROUS_USAGE=1

shopt -s nullglob

PACKAGE_PATH=${PACKAGE_PATH:-}
ROOT_PASSWORD=${ROOT_PASSWORD:-MySql@123}
PORT=${PORT:-3306}
ALLOW_REMOTE=${ALLOW_REMOTE:-true}

LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "MySQL 安装脚本开始..."
echo "当前工作目录: $(pwd)"
echo "PACKAGE_PATH: ${PACKAGE_PATH:-<未指定>}"
echo "PORT: $PORT"
echo "ALLOW_REMOTE: $ALLOW_REMOTE"

if [ "$(id -u)" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

OS=""
if [ -f /etc/debian_version ]; then
    OS="Debian"
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    echo "错误：不支持的操作系统"
    exit 1
fi

echo "检测到操作系统: $OS"

PACKAGE_IS_FILE=false
PACKAGE_IS_DIRECTORY=false
if [ -n "$PACKAGE_PATH" ]; then
    if [ -f "$PACKAGE_PATH" ]; then
        PACKAGE_IS_FILE=true
    elif [ -d "$PACKAGE_PATH" ]; then
        PACKAGE_IS_DIRECTORY=true
    else
        echo "错误：PACKAGE_PATH 不存在：$PACKAGE_PATH"
        exit 1
    fi
fi

sql_escape() {
    local value=${1:-}
    value=${value//\\/\\\\}
    value=${value//\'/\\\'}
    printf '%s' "$value"
}

is_port_listening() {
    local port=$1
    if command -v ss >/dev/null 2>&1; then
        ss -tln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    elif command -v netstat >/dev/null 2>&1; then
        netstat -tln 2>/dev/null | grep -Eq ":[[:space:]]*${port}\>|:${port}[[:space:]]"
    else
        return 1
    fi
}

get_config_candidates() {
    local -a files=()
    local file

    for file in \
        /etc/my.cnf \
        /etc/mysql/my.cnf \
        /etc/mysql/mysql.conf.d/mysqld.cnf \
        /etc/my.cnf.d/mysql-server.cnf; do
        if [ -f "$file" ]; then
            files+=("$file")
        fi
    done

    printf '%s\n' "${files[@]}"
}

get_primary_config_file() {
    local file
    while IFS= read -r file; do
        if [ -n "$file" ]; then
            echo "$file"
            return 0
        fi
    done < <(get_config_candidates)

    if [ "$OS" = "Debian" ]; then
        mkdir -p /etc/mysql/mysql.conf.d
        cat > /etc/mysql/mysql.conf.d/mysqld.cnf <<'EOF'
[mysqld]
EOF
        echo "/etc/mysql/mysql.conf.d/mysqld.cnf"
        return 0
    fi

    mkdir -p /etc
    cat > /etc/my.cnf <<'EOF'
[mysqld]
EOF
    echo "/etc/my.cnf"
}

ensure_mysqld_option() {
    local file=$1
    local key=$2
    local value=$3

    if grep -Eq "^[[:space:]]*${key}[[:space:]]*=" "$file"; then
        sed -i -E "s|^[[:space:]]*${key}[[:space:]]*=.*|${key} = ${value}|" "$file"
    elif grep -Eq "^[[:space:]]*${key}[[:space:]]+" "$file"; then
        sed -i -E "s|^[[:space:]]*${key}[[:space:]]+.*|${key} = ${value}|" "$file"
    elif grep -Eq '^\[mysqld\]' "$file"; then
        sed -i "/^\[mysqld\]/a ${key} = ${value}" "$file"
    else
        printf '\n[mysqld]\n%s = %s\n' "$key" "$value" >> "$file"
    fi
}

detect_mysql_service() {
    if command -v systemctl >/dev/null 2>&1; then
        for svc in mysqld mysql mariadb; do
            if systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\.service"; then
                echo "$svc"
                return 0
            fi
        done
    fi

    if [ "$OS" = "Debian" ]; then
        echo "mysql"
    else
        echo "mysqld"
    fi
}

find_mysql_binary() {
    local candidate
    for candidate in /usr/sbin/mysqld /usr/bin/mysqld /usr/local/mysql/bin/mysqld; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mysqld 2>/dev/null || true
}

find_mysql_command() {
    local candidate
    for candidate in /usr/bin/mysql /usr/local/mysql/bin/mysql; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mysql 2>/dev/null || true
}

find_mysqladmin_command() {
    local candidate
    for candidate in /usr/bin/mysqladmin /usr/local/mysql/bin/mysqladmin; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mysqladmin 2>/dev/null || true
}

find_temp_password() {
    local log_path
    for log_path in /var/log/mysqld.log /var/log/mysql/error.log; do
        if [ -f "$log_path" ]; then
            grep -E 'temporary password' "$log_path" 2>/dev/null | awk '{print $NF}' | tail -n 1
            return 0
        fi
    done

    return 1
}

wait_for_service_ready() {
    local service_name=$1

    if command -v systemctl >/dev/null 2>&1; then
        for i in $(seq 1 30); do
            if systemctl is-active --quiet "$service_name" 2>/dev/null; then
                break
            fi
            if [ "$i" -eq 30 ]; then
                echo "错误：MySQL 服务未能成功启动：$service_name"
                journalctl -u "$service_name" -n 50 --no-pager 2>/dev/null || true
                exit 1
            fi
            sleep 2
        done
    fi

    for i in $(seq 1 30); do
        if is_port_listening "$PORT"; then
            return 0
        fi
        sleep 2
    done

    echo "错误：MySQL 启动后端口 $PORT 未监听"
    if command -v systemctl >/dev/null 2>&1; then
        journalctl -u "$service_name" -n 50 --no-pager 2>/dev/null || true
    fi
    exit 1
}

preseed_mysql_password() {
    if ! command -v debconf-set-selections >/dev/null 2>&1; then
        return 0
    fi

    local pkg
    local -a pkgs=(
        mysql-server
        mysql-community-server
        mysql-server-8.0
    )

    for pkg in "${pkgs[@]}"; do
        echo "$pkg mysql-server/root_password password $ROOT_PASSWORD" | debconf-set-selections
        echo "$pkg mysql-server/root_password_again password $ROOT_PASSWORD" | debconf-set-selections
        echo "$pkg mysql-server/start_on_boot boolean true" | debconf-set-selections
        echo "$pkg $pkg/root_password password $ROOT_PASSWORD" | debconf-set-selections
        echo "$pkg $pkg/root_password_again password $ROOT_PASSWORD" | debconf-set-selections
    done
}

install_debian_from_directory() {
    local package_dir=$1
    local -a deb_files=("$package_dir"/*.deb)
    local -a ordered_patterns=(
        'mysql-common_*.deb'
        'mysql-community-client-plugins_*.deb'
        'libmysqlclient*.deb'
        'mysql-community-client-core_*.deb'
        'mysql-community-client_*.deb'
        'mysql-client_*.deb'
        'mysql-community-server-core_*.deb'
        'mysql-community-server_*.deb'
        'mysql-server_*.deb'
    )
    local -a install_queue=()
    local -a remaining_files=()
    local pattern
    local pkg
    local matched=false

    if [ ${#deb_files[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 .deb 文件：$package_dir"
        exit 1
    fi

    if ! ls "$package_dir"/mysql-server_*.deb >/dev/null 2>&1 && ! ls "$package_dir"/mysql-community-server_*.deb >/dev/null 2>&1; then
        echo "错误：离线目录中缺少 MySQL 8.x 主 DEB 包：$package_dir"
        exit 1
    fi

    echo "正在使用离线 DEB 目录安装 MySQL..."
    preseed_mysql_password

    remaining_files=()
    for pattern in "${ordered_patterns[@]}"; do
        for pkg in "$package_dir"/$pattern; do
            if [ -f "$pkg" ]; then
                install_queue+=("$pkg")
            fi
        done
    done

    if [ ${#install_queue[@]} -gt 0 ]; then
        echo "按 MySQL 8.x 依赖顺序安装离线 DEB 包..."
        dpkg --force-confdef --force-confold -i "${install_queue[@]}" || true
        dpkg --configure -a || true
    fi

    for pkg in "${deb_files[@]}"; do
        matched=false
        for ordered in "${install_queue[@]}"; do
            if [ "$pkg" = "$ordered" ]; then
                matched=true
                break
            fi
        done
        if [ "$matched" = false ]; then
            remaining_files+=("$pkg")
        fi
    done

    if [ ${#remaining_files[@]} -gt 0 ]; then
        dpkg --force-confdef --force-confold -i "${remaining_files[@]}" || true
    fi

    apt-get install -f -y -qq \
        -o Dpkg::Options::="--force-confdef" \
        -o Dpkg::Options::="--force-confold" || true
    dpkg --configure -a || true
}

install_redhat_from_directory() {
    local package_dir=$1
    local -a rpm_files=("$package_dir"/*.rpm)

    if [ ${#rpm_files[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 .rpm 文件：$package_dir"
        exit 1
    fi

    if ! ls "$package_dir"/mysql-community-server-*.rpm >/dev/null 2>&1 && ! ls "$package_dir"/mysql-server-*.rpm >/dev/null 2>&1; then
        echo "错误：离线目录中缺少 MySQL 8.x 主 RPM 包：$package_dir"
        exit 1
    fi

    echo "正在使用离线 RPM 目录安装 MySQL..."
    yum remove -y mariadb-libs 2>/dev/null || true
    yum localinstall -y "${rpm_files[@]}" || rpm -ivh --replacepkgs "${rpm_files[@]}"
}

install_from_archive() {
    local archive_path=$1
    local extract_dir="/tmp/mysql_extract_$(date +%s)"

    rm -rf "$extract_dir"
    mkdir -p "$extract_dir"
    tar -xf "$archive_path" -C "$extract_dir" --no-same-owner

    if [ "$OS" = "RedHat" ] && find "$extract_dir" -type f -name '*.rpm' | grep -q .; then
        local rpm_root
        rpm_root=$(find "$extract_dir" -type f -name 'mysql-community-server-*.rpm' -o -name 'mysql-server-*.rpm' | head -n 1 | xargs -r dirname)
        if [ -z "$rpm_root" ]; then
            echo "错误：压缩包中未找到 MySQL 主 RPM 包"
            rm -rf "$extract_dir"
            exit 1
        fi
        install_redhat_from_directory "$rpm_root"
        rm -rf "$extract_dir"
        return 0
    fi

    if [ "$OS" = "Debian" ] && find "$extract_dir" -type f -name '*.deb' | grep -q .; then
        local deb_root
        deb_root=$(find "$extract_dir" -type f \( -name 'mysql-server_*.deb' -o -name 'mysql-community-server_*.deb' \) | head -n 1 | xargs -r dirname)
        if [ -z "$deb_root" ]; then
            echo "错误：压缩包中未找到 MySQL 主 DEB 包"
            rm -rf "$extract_dir"
            exit 1
        fi
        install_debian_from_directory "$deb_root"
        rm -rf "$extract_dir"
        return 0
    fi

    echo "错误：无法识别压缩包中的 MySQL 安装内容"
    rm -rf "$extract_dir"
    exit 1
}

run_mysql_sql() {
    local sql=$1
    local mysql_cmd=$2
    local temp_password=${3:-}

    if timeout 10s "$mysql_cmd" --connect-timeout=5 -uroot -e "$sql" >/dev/null 2>&1; then
        return 0
    fi

    if [ -n "$temp_password" ] && timeout 10s "$mysql_cmd" --connect-timeout=5 --connect-expired-password -uroot -p"$temp_password" -e "$sql" >/dev/null 2>&1; then
        return 0
    fi

    if timeout 10s "$mysql_cmd" --connect-timeout=5 -uroot -p"$ROOT_PASSWORD" -e "$sql" >/dev/null 2>&1; then
        return 0
    fi

    return 1
}

echo "PROGRESS:Installing:20"
if [ "$PACKAGE_IS_DIRECTORY" = true ]; then
    if [ "$OS" = "Debian" ]; then
        install_debian_from_directory "$PACKAGE_PATH"
    else
        install_redhat_from_directory "$PACKAGE_PATH"
    fi
elif [ "$PACKAGE_IS_FILE" = true ]; then
    case "$PACKAGE_PATH" in
        *.deb)
            install_debian_from_directory "$(dirname "$PACKAGE_PATH")"
            ;;
        *.rpm)
            install_redhat_from_directory "$(dirname "$PACKAGE_PATH")"
            ;;
        *.tar*|*.tgz)
            install_from_archive "$PACKAGE_PATH"
            ;;
        *)
            echo "错误：不支持的 MySQL 安装包格式：$PACKAGE_PATH"
            exit 1
            ;;
    esac
else
    echo "未提供本地离线包，使用在线安装..."
    if [ "$OS" = "Debian" ]; then
        preseed_mysql_password
        apt-get update -qq || true
        apt-get install -y -qq mysql-server-8.0 || apt-get install -y -qq mysql-server
    else
        yum install -y mysql-community-server || yum install -y mysql-server
    fi
fi

MYSQLD_BIN=$(find_mysql_binary)
if [ -z "$MYSQLD_BIN" ]; then
    echo "错误：安装后未找到 mysqld 二进制文件"
    exit 1
fi

echo "已检测到 mysqld: $MYSQLD_BIN"

CONFIG_FILE=$(get_primary_config_file)
echo "使用配置文件: $CONFIG_FILE"
cp "$CONFIG_FILE" "${CONFIG_FILE}.bak" 2>/dev/null || true

ensure_mysqld_option "$CONFIG_FILE" port "$PORT"
if [ "$ALLOW_REMOTE" = "true" ]; then
    ensure_mysqld_option "$CONFIG_FILE" bind-address "0.0.0.0"
else
    ensure_mysqld_option "$CONFIG_FILE" bind-address "127.0.0.1"
fi

echo "PROGRESS:Starting:65"
SERVICE_NAME=$(detect_mysql_service)
echo "检测到服务名: $SERVICE_NAME"

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl enable "$SERVICE_NAME" >/dev/null 2>&1 || true
    systemctl restart "$SERVICE_NAME"
else
    service "$SERVICE_NAME" restart
fi

wait_for_service_ready "$SERVICE_NAME"

MYSQL_CMD=$(find_mysql_command)
MYSQLADMIN_CMD=$(find_mysqladmin_command)
TEMP_PASSWORD=$(find_temp_password || true)
ESCAPED_ROOT_PASSWORD=$(sql_escape "$ROOT_PASSWORD")

echo "PROGRESS:Configuring:80"
if [ -n "$MYSQL_CMD" ]; then
    run_mysql_sql "ALTER USER 'root'@'localhost' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "$MYSQL_CMD" "$TEMP_PASSWORD" || true

    if [ "$ALLOW_REMOTE" = "true" ]; then
        run_mysql_sql "CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "$MYSQL_CMD" "$TEMP_PASSWORD" || true
        run_mysql_sql "ALTER USER 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "$MYSQL_CMD" "$TEMP_PASSWORD" || true
        run_mysql_sql "GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;" "$MYSQL_CMD" "$TEMP_PASSWORD" || true
    fi
fi

SUCCESS=false
if [ -n "$MYSQLADMIN_CMD" ]; then
    for i in $(seq 1 20); do
        if timeout 10s "$MYSQLADMIN_CMD" --connect-timeout=5 -uroot -p"$ROOT_PASSWORD" -P"$PORT" -h127.0.0.1 ping >/dev/null 2>&1; then
            SUCCESS=true
            break
        fi
        sleep 2
    done
else
    if is_port_listening "$PORT"; then
        SUCCESS=true
    fi
fi

if [ "$SUCCESS" != true ]; then
    echo "错误：MySQL 安装后的最终验证失败"
    if command -v systemctl >/dev/null 2>&1; then
        journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
    fi
    exit 1
fi

VERSION=$( ( "$MYSQLD_BIN" --version 2>/dev/null || true ) | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 )

echo "PROGRESS:Complete:100"
echo "MySQL 安装完成！"
echo "连接信息: Host=127.0.0.1, Port=$PORT, User=root, Password=$ROOT_PASSWORD"
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "RUNNING: true"
echo "PORT: $PORT"
echo "VERSION: ${VERSION:-未知}"
echo "SERVICE_NAME: ${SERVICE_NAME:-unknown}"
echo "CONFIG_PATH: ${CONFIG_FILE:-unknown}"
echo "STAGE:SUCCESS"
echo "------------------------"
