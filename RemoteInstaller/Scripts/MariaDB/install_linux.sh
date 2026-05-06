#!/bin/bash
set -euo pipefail

export TERM=dumb
export DEBIAN_FRONTEND=noninteractive
export DEBCONF_NONINTERACTIVE_SEEN=true
export DEBCONF_FRONTEND=noninteractive
export APT_KEY_DONT_WARN_ON_DANGEROUS_USAGE=1

shopt -s nullglob

PACKAGE_PATH=${PACKAGE_PATH:-}
ROOT_PASSWORD=${ROOT_PASSWORD:-MariaDb@123}
PORT=${PORT:-3306}
ALLOW_REMOTE=${ALLOW_REMOTE:-true}
DATA_DIRECTORY=${DATA_DIRECTORY:-}

LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "MariaDB 安装脚本开始..."
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
        /etc/mysql/mariadb.conf.d/50-server.cnf \
        /etc/my.cnf \
        /etc/mysql/my.cnf \
        /etc/my.cnf.d/server.cnf; do
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
        mkdir -p /etc/mysql/mariadb.conf.d
        cat > /etc/mysql/mariadb.conf.d/50-server.cnf <<'EOF'
[mysqld]
EOF
        echo "/etc/mysql/mariadb.conf.d/50-server.cnf"
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

detect_mariadb_service() {
    if command -v systemctl >/dev/null 2>&1; then
        for svc in mariadb mysql mysqld; do
            if systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\.service"; then
                echo "$svc"
                return 0
            fi
        done
    fi

    echo "mariadb"
}

find_mariadb_server_binary() {
    local candidate
    for candidate in /usr/sbin/mariadbd /usr/bin/mariadbd /usr/libexec/mariadbd /usr/sbin/mysqld; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mariadbd 2>/dev/null || command -v mysqld 2>/dev/null || true
}

find_mariadb_command() {
    local candidate
    for candidate in /usr/bin/mariadb /usr/local/bin/mariadb /usr/bin/mysql; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mariadb 2>/dev/null || command -v mysql 2>/dev/null || true
}

find_mariadb_admin_command() {
    local candidate
    for candidate in /usr/bin/mariadb-admin /usr/bin/mysqladmin /usr/local/bin/mariadb-admin; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    command -v mariadb-admin 2>/dev/null || command -v mysqladmin 2>/dev/null || true
}

wait_for_service_ready() {
    local service_name=$1

    if command -v systemctl >/dev/null 2>&1; then
        for i in $(seq 1 30); do
            if systemctl is-active --quiet "$service_name" 2>/dev/null; then
                break
            fi
            if [ "$i" -eq 30 ]; then
                echo "错误：MariaDB 服务未能成功启动：$service_name"
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

    echo "错误：MariaDB 启动后端口 $PORT 未监听"
    if command -v systemctl >/dev/null 2>&1; then
        journalctl -u "$service_name" -n 50 --no-pager 2>/dev/null || true
    fi
    exit 1
}

is_mariadb_server_installed() {
    if command -v mariadbd >/dev/null 2>&1 || [ -x /usr/sbin/mariadbd ] || [ -x /usr/bin/mariadbd ] || [ -x /usr/libexec/mariadbd ]; then
        return 0
    fi

    if command -v systemctl >/dev/null 2>&1 && systemctl list-unit-files 2>/dev/null | grep -Eq '^mariadb\.service|^mysql\.service|^mysqld\.service'; then
        return 0
    fi

    if [ "$OS" = "Debian" ]; then
        dpkg -l 2>/dev/null | grep -Ei '^ii[[:space:]]+mariadb-server([[:space:]:-]|$)' | grep -q .
        return $?
    fi

    rpm -qa 2>/dev/null | grep -Ei '^(MariaDB-server|mariadb-server)(-|$)' | grep -q .
}

has_debian_local_repository() {
    local package_dir=$1

    [ -f "$package_dir/Packages" ] || return 1
    compgen -G "$package_dir/MariaDB-*-public.asc" >/dev/null 2>&1
}

build_debian_direct_install_queue() {
    local package_dir=$1
    local pattern
    local deb_file
    local -a ordered_patterns=(
        'mysql-common_*.deb'
        'mariadb-common_*.deb'
        'galera-4_*.deb'
        'libmariadb3_*.deb'
        'libmariadb3-compat_*.deb'
        'libmysqlclient18_*.deb'
        'libmariadbclient18_*.deb'
        'mariadb-client-core_*.deb'
        'mariadb-client_*.deb'
        'mariadb-client-compat_*.deb'
        'mariadb-server-core_*.deb'
        'mariadb-server_*.deb'
        'mariadb-server-compat_*.deb'
    )

    for pattern in "${ordered_patterns[@]}"; do
        for deb_file in "$package_dir"/$pattern; do
            if [ -f "$deb_file" ]; then
                printf '%s\n' "$deb_file"
            fi
        done
    done
}

get_redhat_major_version() {
    local version_id

    if [ -f /etc/os-release ]; then
        version_id=$(grep '^VERSION_ID=' /etc/os-release | head -n 1 | cut -d'=' -f2 | tr -d '"')
    fi

    if [ -z "${version_id:-}" ] && [ -f /etc/redhat-release ]; then
        version_id=$(grep -oE '[0-9]+' /etc/redhat-release | head -n 1 || true)
    fi

    version_id=${version_id%%.*}
    printf '%s' "$version_id"
}

is_redhat_7() {
    [ "$OS" = "RedHat" ] || return 1
    [ "$(get_redhat_major_version)" = "7" ]
}

normalize_rpm_capability() {
    local capability=${1:-}
    printf '%s\n' "$capability" | awk 'NF { $1=$1; print }'
}

is_ignored_rpm_requirement() {
    local requirement=${1:-}

    case "$requirement" in
        ""|rpmlib\(*)
            return 0
            ;;
    esac

    return 1
}

collect_local_rpm_capabilities() {
    local package_dir=$1
    local output_file=$2
    local rpm_file
    local provides_output

    : > "$output_file"

    for rpm_file in "$package_dir"/*.rpm; do
        provides_output=$(rpm -qp --provides "$rpm_file" 2>/dev/null) || {
            echo "错误：读取 RPM 提供能力失败：$rpm_file"
            exit 1
        }
        printf '%s\n' "$provides_output" >> "$output_file"
    done

    awk 'NF { $1=$1; print }' "$output_file" | sort -u > "${output_file}.normalized"
    mv -f "${output_file}.normalized" "$output_file"
}

local_rpm_capabilities_satisfy_requirement() {
    local requirement=$1
    local provides_file=$2
    local requirement_name=${requirement%% *}
    local provided_capability

    while IFS= read -r provided_capability; do
        if [ "$provided_capability" = "$requirement" ]; then
            return 0
        fi

        if [ "$requirement_name" = "$requirement" ] && { [ "$provided_capability" = "$requirement_name" ] || [[ "$provided_capability" == "$requirement_name "* ]]; }; then
            return 0
        fi
    done < "$provides_file"

    return 1
}

system_satisfies_rpm_requirement() {
    local requirement=$1
    local requirement_name=${requirement%% *}

    rpm -q --whatprovides "$requirement" >/dev/null 2>&1 && return 0
    [ "$requirement_name" = "$requirement" ] || return 1
    rpm -q --whatprovides "$requirement_name" >/dev/null 2>&1
}

validate_redhat_offline_transaction() {
    local package_dir=$1
    shift
    local output_file

    output_file=$(mktemp)

    if ! yum --disablerepo='*' --setopt=tsflags=test localinstall -y "$@" >"$output_file" 2>&1; then
        echo "错误：严格离线模式事务预检失败，请检查以下输出："
        cat "$output_file"
        rm -f "$output_file"
        exit 1
    fi

    rm -f "$output_file"
}

validate_redhat_offline_dependencies() {
    local package_dir=$1
    local provides_file
    local missing_file
    local rpm_file
    local requirements_output
    local requirement
    local normalized_requirement

    provides_file=$(mktemp)
    missing_file=$(mktemp)

    collect_local_rpm_capabilities "$package_dir" "$provides_file"

    for rpm_file in "$package_dir"/*.rpm; do
        requirements_output=$(rpm -qpR "$rpm_file" 2>/dev/null) || {
            rm -f "$provides_file" "$missing_file"
            echo "错误：读取 RPM 依赖能力失败：$rpm_file"
            exit 1
        }

        while IFS= read -r requirement; do
            normalized_requirement=$(normalize_rpm_capability "$requirement")

            if is_ignored_rpm_requirement "$normalized_requirement"; then
                continue
            fi

            if local_rpm_capabilities_satisfy_requirement "$normalized_requirement" "$provides_file"; then
                continue
            fi

            if system_satisfies_rpm_requirement "$normalized_requirement"; then
                continue
            fi

            printf '%s\n' "$normalized_requirement" >> "$missing_file"
        done <<< "$requirements_output"
    done

    if [ -s "$missing_file" ]; then
        sort -u -o "$missing_file" "$missing_file"
        echo "错误：严格离线模式下，离线目录缺少以下 RPM 依赖能力："
        cat "$missing_file"
        echo "请将满足以上能力的 EL7 RPM 一并放入目录：$package_dir"
        rm -f "$provides_file" "$missing_file"
        exit 1
    fi

    rm -f "$provides_file" "$missing_file"
}

install_debian_from_repository() {
    local package_dir=$1

    echo "正在使用 MariaDB 本地 APT 仓库进行严格离线安装..."
    (
        set -euo pipefail

        local -a repo_key_candidates=("$package_dir"/MariaDB-*-public.asc)
        local repo_key_source=${repo_key_candidates[0]:-}
        local repo_list=/etc/apt/sources.list.d/remoteinstaller-mariadb-offline.list
        local repo_keyring=/etc/apt/keyrings/remoteinstaller-mariadb-offline.asc

        if ! has_debian_local_repository "$package_dir"; then
            echo "错误：离线目录缺少 MariaDB 本地仓库元数据（Packages 或签名 key）：$package_dir"
            exit 1
        fi

        trap 'rm -f "$repo_list" "$repo_keyring"' EXIT

        mkdir -p /etc/apt/keyrings
        rm -f "$repo_list" "$repo_keyring"

        cp "$repo_key_source" "$repo_keyring"
        chmod 644 "$repo_keyring"

        printf 'deb [signed-by=%s] file://%s ./\n' "$repo_keyring" "$package_dir" > "$repo_list"
        chmod 644 "$repo_list"

        echo "MariaDB 仓库签名 key: $repo_key_source"
        echo "临时 APT key 文件: $repo_keyring"
        echo "临时 APT 源文件: $repo_list"
        echo "当前 APT 操作仅使用 file:// 本地仓库，不读取其它 sourceparts。"

        apt-get \
            -o Dir::Etc::sourcelist="$repo_list" \
            -o Dir::Etc::sourceparts=/dev/null \
            -o Acquire::Languages=none \
            -o APT::Get::List-Cleanup=0 \
            update

        apt-get -y -qq \
            -o Dir::Etc::sourcelist="$repo_list" \
            -o Dir::Etc::sourceparts=/dev/null \
            -o Dpkg::Options::="--force-confdef" \
            -o Dpkg::Options::="--force-confold" \
            install mariadb-server
    )
}

cleanup_debian_repo_artifacts() {
    rm -f /etc/apt/sources.list.d/remoteinstaller-mariadb-offline.list
    rm -f /etc/apt/keyrings/remoteinstaller-mariadb-offline.asc
}

log_debian_package_diagnostics() {
    local package_dir=$1
    local deb_count
    deb_count=$(find "$package_dir" -maxdepth 1 -type f -name '*.deb' | wc -l | tr -d ' ')
    echo "MariaDB 安装模式候选: direct-deb"
    echo "离线目录: $package_dir"
    echo "DEB 文件数量: ${deb_count:-0}"
    echo "主包列表: $(find "$package_dir" -maxdepth 1 -type f \( -name 'mariadb-server_*.deb' -o -name 'mariadb-server-core_*.deb' \) -printf '%f ' 2>/dev/null)"
    echo "当前 hold 包: $(apt-mark showhold 2>/dev/null | tr '\n' ' ' || true)"
    echo "已安装数据库相关包: $(dpkg -l 2>/dev/null | awk '/^(ii|hi)/ && $2 ~ /(mariadb|mysql|percona)/ {printf "%s=%s ", $2, $3}' || true)"
}

install_debian_from_directory() {
    local package_dir=$1
    local -a deb_files=("$package_dir"/*.deb)
    local -a install_queue=()
    local deb_file

    if [ ${#deb_files[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 .deb 文件：$package_dir"
        exit 1
    fi

    if ! ls "$package_dir"/mariadb-server_*.deb >/dev/null 2>&1 && ! ls "$package_dir"/mariadb-server-core_*.deb >/dev/null 2>&1; then
        echo "错误：离线目录中缺少 MariaDB 主 DEB 包：$package_dir"
        exit 1
    fi

    if is_mariadb_server_installed; then
        echo "检测到 MariaDB 已安装，跳过离线安装步骤"
        return 0
    fi

    cleanup_debian_repo_artifacts
    log_debian_package_diagnostics "$package_dir"

    if has_debian_local_repository "$package_dir"; then
        echo "检测到 Packages 与签名 key，优先使用本地 file:// APT 仓库安装 MariaDB..."
        install_debian_from_repository "$package_dir"
        return 0
    fi

    while IFS= read -r deb_file; do
        if [ -n "$deb_file" ]; then
            install_queue+=("$deb_file")
        fi
    done < <(build_debian_direct_install_queue "$package_dir")

    if [ ${#install_queue[@]} -eq 0 ]; then
        echo "错误：未构建出可用于 direct-deb 安装的 MariaDB 包队列"
        exit 1
    fi

    echo "开始直接安装最小必要 MariaDB DEB 包集合..."
    dpkg --force-confdef --force-confold -i "${install_queue[@]}"
    dpkg --configure -a

    if is_mariadb_server_installed; then
        echo "MariaDB direct-deb 安装完成"
        return 0
    fi

    echo "direct-deb 安装后仍未检测到 MariaDB。"
    echo "错误：当前离线目录缺少可用的本地 repo 元数据，且 direct-deb 未完成安装。"
    exit 1
}

install_redhat_from_directory() {
    local package_dir=$1
    local -a rpm_files=("$package_dir"/*.rpm)

    if [ ${#rpm_files[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 .rpm 文件：$package_dir"
        exit 1
    fi

    if ! ls "$package_dir"/MariaDB-server-*.rpm >/dev/null 2>&1 && ! ls "$package_dir"/mariadb-server-*.rpm >/dev/null 2>&1; then
        echo "错误：离线目录中缺少 MariaDB 主 RPM 包：$package_dir"
        exit 1
    fi

    if is_mariadb_server_installed; then
        echo "检测到 MariaDB 已安装，跳过离线安装步骤"
        return 0
    fi

    if is_redhat_7; then
        echo "检测到 EL7/CentOS7，启用严格离线模式..."
        echo "正在校验离线 RPM 依赖能力..."
        validate_redhat_offline_dependencies "$package_dir"
        echo "正在执行离线事务预检..."
        validate_redhat_offline_transaction "$package_dir" "${rpm_files[@]}"
    fi

    echo "正在使用离线 RPM 目录安装 MariaDB..."
    echo "当前 yum 操作已禁用全部外部 repo。"
    yum --disablerepo='*' remove -y mariadb-libs 2>/dev/null || true
    yum --disablerepo='*' localinstall -y "${rpm_files[@]}"
}

run_mariadb_sql() {
    local sql=$1
    local mariadb_cmd=$2
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -uroot -e "$sql" >/dev/null 2>&1 || \
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -uroot -p"$ROOT_PASSWORD" -e "$sql" >/dev/null 2>&1
}

run_mariadb_query() {
    local sql=$1
    local mariadb_cmd=$2
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -N -s -uroot -e "$sql" 2>/dev/null || \
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -N -s -uroot -p"$ROOT_PASSWORD" -e "$sql" 2>/dev/null
}

require_mariadb_sql() {
    local sql=$1
    local error_message=$2
    local mariadb_cmd=$3

    if ! run_mariadb_sql "$sql" "$mariadb_cmd"; then
        echo "错误：$error_message"
        exit 1
    fi
}

verify_local_sql_ready() {
    local mariadb_cmd=$1
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -N -s -uroot -e "SELECT 1" >/dev/null 2>&1 || \
    timeout 10s "$mariadb_cmd" --connect-timeout=5 -N -s -uroot -p"$ROOT_PASSWORD" -e "SELECT 1" >/dev/null 2>&1
}

verify_tcp_sql_ready() {
    local mariadb_cmd=$1
    timeout 10s "$mariadb_cmd" --connect-timeout=5 --protocol=TCP -N -s -h127.0.0.1 -P"$PORT" -uroot -p"$ROOT_PASSWORD" -e "SELECT 1" >/dev/null 2>&1
}

echo "PROGRESS:Installing:20"
echo "PACKAGE_IS_DIRECTORY: $PACKAGE_IS_DIRECTORY"
echo "PACKAGE_IS_FILE: $PACKAGE_IS_FILE"
if [ "$PACKAGE_IS_DIRECTORY" = true ]; then
    if [ "$OS" = "Debian" ]; then
        install_debian_from_directory "$PACKAGE_PATH"
    else
        install_redhat_from_directory "$PACKAGE_PATH"
    fi
elif [ "$PACKAGE_IS_FILE" = true ]; then
    if [[ "$PACKAGE_PATH" == *.deb ]]; then
        install_debian_from_directory "$(dirname "$PACKAGE_PATH")"
    elif [[ "$PACKAGE_PATH" == *.rpm ]]; then
        install_redhat_from_directory "$(dirname "$PACKAGE_PATH")"
    else
        echo "错误：不支持的 MariaDB 安装包格式：$PACKAGE_PATH"
        exit 1
    fi
else
    echo "错误：MariaDB 仅支持离线安装，请提供本地安装目录。"
    exit 1
fi

MARIADB_SERVER_BIN=$(find_mariadb_server_binary)
if [ -z "$MARIADB_SERVER_BIN" ]; then
    echo "错误：安装后未找到 mariadbd 二进制文件"
    exit 1
fi

CONFIG_FILE=$(get_primary_config_file)
echo "使用配置文件: $CONFIG_FILE"
cp "$CONFIG_FILE" "${CONFIG_FILE}.bak" 2>/dev/null || true

ensure_mysqld_option "$CONFIG_FILE" port "$PORT"
if [ "$ALLOW_REMOTE" = "true" ]; then
    ensure_mysqld_option "$CONFIG_FILE" bind-address "0.0.0.0"
else
    ensure_mysqld_option "$CONFIG_FILE" bind-address "127.0.0.1"
fi

if [ -n "$DATA_DIRECTORY" ]; then
    mkdir -p "$DATA_DIRECTORY"
    chown -R mysql:mysql "$DATA_DIRECTORY" 2>/dev/null || true
    ensure_mysqld_option "$CONFIG_FILE" datadir "$DATA_DIRECTORY"
fi

echo "PROGRESS:Starting:65"
SERVICE_NAME=$(detect_mariadb_service)
echo "检测到服务名: $SERVICE_NAME"

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl enable "$SERVICE_NAME" >/dev/null 2>&1 || true
    systemctl restart "$SERVICE_NAME" || systemctl start "$SERVICE_NAME"
else
    service "$SERVICE_NAME" restart
fi

wait_for_service_ready "$SERVICE_NAME"

MARIADB_CMD=$(find_mariadb_command)
MARIADB_ADMIN_CMD=$(find_mariadb_admin_command)
ESCAPED_ROOT_PASSWORD=$(sql_escape "$ROOT_PASSWORD")

echo "PROGRESS:Configuring:80"
if [ -z "$MARIADB_CMD" ]; then
    echo "错误：安装后未找到 mariadb 客户端命令，无法完成 root 账户初始化"
    exit 1
fi

if ! run_mariadb_sql "ALTER USER 'root'@'localhost' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "$MARIADB_CMD"; then
    echo "警告：更新 root@localhost 密码未立即成功，将继续验证本地 SQL 可用性"
fi

local_ready=false
for i in $(seq 1 20); do
    if verify_local_sql_ready "$MARIADB_CMD"; then
        local_ready=true
        break
    fi
    sleep 2
done

if [ "$local_ready" != true ]; then
    echo "错误：MariaDB 本地 SQL 校验失败，root 账户初始化未完成"
    if command -v systemctl >/dev/null 2>&1; then
        journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
    fi
    exit 1
fi

if [ "$ALLOW_REMOTE" = "true" ]; then
    require_mariadb_sql "CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "创建 root@'%' 用户失败" "$MARIADB_CMD"
    require_mariadb_sql "ALTER USER 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "更新 root@'%' 密码失败" "$MARIADB_CMD"
    require_mariadb_sql "GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;" "授予 root@'%' 远程权限失败" "$MARIADB_CMD"
fi

SUCCESS=true
if [ -n "$MARIADB_ADMIN_CMD" ] && [ "$ALLOW_REMOTE" = "true" ]; then
    tcp_ready=false
    for i in $(seq 1 20); do
        if timeout 10s "$MARIADB_ADMIN_CMD" --connect-timeout=5 -uroot -p"$ROOT_PASSWORD" -P"$PORT" -h127.0.0.1 ping >/dev/null 2>&1; then
            tcp_ready=true
            break
        fi
        if verify_tcp_sql_ready "$MARIADB_CMD"; then
            tcp_ready=true
            break
        fi
        sleep 2
    done

    if [ "$tcp_ready" != true ]; then
        echo "错误：MariaDB 本地可用，但 TCP 登录校验失败"
        if command -v systemctl >/dev/null 2>&1; then
            journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
        fi
        exit 1
    fi
elif [ "$ALLOW_REMOTE" = "true" ]; then
    if ! verify_tcp_sql_ready "$MARIADB_CMD"; then
        echo "错误：MariaDB 本地可用，但 TCP 登录校验失败"
        if command -v systemctl >/dev/null 2>&1; then
            journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
        fi
        exit 1
    fi
fi

if [ "$ALLOW_REMOTE" = "true" ]; then
    current_bind_address=$(grep -E '^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)' "$CONFIG_FILE" 2>/dev/null | head -n 1 | sed -E 's/^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)//' | tr -d '\r')
    if [ "$current_bind_address" != "0.0.0.0" ]; then
        echo "错误：已启用远程访问，但 bind-address 未配置为 0.0.0.0"
        exit 1
    fi

    remote_root_count=$(run_mariadb_query "SELECT COUNT(*) FROM mysql.user WHERE User='root' AND Host='%';" "$MARIADB_CMD" | tail -n 1 | tr -d '[:space:]')
    if [ -z "$remote_root_count" ] || [ "$remote_root_count" = "0" ]; then
        echo "错误：MariaDB 本地可用，但 root@'%' 远程账户未创建成功"
        exit 1
    fi

    if ! run_mariadb_sql "SHOW GRANTS FOR 'root'@'%';" "$MARIADB_CMD"; then
        echo "错误：MariaDB 本地可用，但 root@'%' 远程授权未生效"
        exit 1
    fi
fi

VERSION=$( ( "$MARIADB_SERVER_BIN" --version 2>/dev/null || true ) | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)

echo "PROGRESS:Complete:100"
echo "MariaDB 安装完成！"
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
