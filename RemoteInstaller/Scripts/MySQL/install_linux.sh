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
DATA_DIRECTORY=${DATA_DIRECTORY:-}

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

resolve_debian_mysql_package_dir() {
    local package_root=$1

    if [ -d "$package_root/mysql" ]; then
        echo "$package_root/mysql"
        return 0
    fi

    echo "$package_root"
}

resolve_debian_dependency_dir() {
    local package_root=$1

    if [ -d "$package_root/deps" ]; then
        echo "$package_root/deps"
        return 0
    fi

    echo "$package_root"
}

get_debian_offline_dependency_packages() {
    if [ "$OS" != "Debian" ]; then
        return 0
    fi

    local version_id=${VERSION_ID:-}
    if [ -z "$version_id" ] && [ -f /etc/os-release ]; then
        version_id=$(grep '^VERSION_ID=' /etc/os-release | head -n 1 | cut -d'=' -f2 | tr -d '"')
    fi

    case "${version_id%%.*}" in
        24)
            printf '%s\n' \
                adduser \
                debconf \
                libaio1t64 \
                libcap2 \
                libcom-err2 \
                libcrypt1 \
                libdb5.3t64 \
                libgdbm-compat4t64 \
                libgdbm6t64 \
                libgssapi-krb5-2 \
                libk5crypto3 \
                libkeyutils1 \
                libkrb5-3 \
                libkrb5support0 \
                libmecab2 \
                libnuma1 \
                libperl5.38t64 \
                libsasl2-2 \
                libsasl2-modules-db \
                libssl3t64 \
                libtinfo6 \
                libtirpc-common \
                libtirpc3t64 \
                libudev1 \
                perl \
                perl-modules-5.38 \
                psmisc \
                zlib1g
            ;;
        *)
            printf '%s\n' \
                adduser \
                debconf \
                libaio1 \
                libcom-err2 \
                libcrypt1 \
                libgdbm-compat4 \
                libgdbm6 \
                libgssapi-krb5-2 \
                libk5crypto3 \
                libkeyutils1 \
                libkrb5-3 \
                libkrb5support0 \
                libmecab2 \
                libnuma1 \
                libperl5.34 \
                libsasl2-2 \
                libsasl2-modules-db \
                libssl3 \
                libtinfo6 \
                libtirpc-common \
                libtirpc3 \
                libudev1 \
                perl \
                perl-modules-5.34 \
                psmisc \
                zlib1g
            ;;
    esac
}

is_debian_package_installed() {
    local package_name=$1
    dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null | grep -q 'install ok installed'
}

find_debian_dependency_file() {
    local dependency_dir=$1
    local package_name=$2
    local file

    for file in "$dependency_dir"/"${package_name}"_*.deb "$dependency_dir"/"${package_name}"-*.deb; do
        if [ -f "$file" ]; then
            echo "$file"
            return 0
        fi
    done

    return 1
}

prepare_debian_offline_dependencies() {
    local package_root=$1
    local dependency_dir
    local dependency_name
    local dependency_file
    local -a missing_packages=()
    local -a install_files=()

    dependency_dir=$(resolve_debian_dependency_dir "$package_root")

    while IFS= read -r dependency_name; do
        if [ -z "$dependency_name" ]; then
            continue
        fi

        if is_debian_package_installed "$dependency_name"; then
            echo "离线依赖已安装，跳过：$dependency_name"
            continue
        fi

        if ! dependency_file=$(find_debian_dependency_file "$dependency_dir" "$dependency_name"); then
            missing_packages+=("$dependency_name")
            continue
        fi

        install_files+=("$dependency_file")
    done < <(get_debian_offline_dependency_packages)

    if [ ${#missing_packages[@]} -gt 0 ]; then
        echo "错误：离线目录缺少以下 Debian 依赖包："
        printf '%s\n' "${missing_packages[@]}"
        echo "请将对应 .deb 文件放入目录：$dependency_dir"
        exit 1
    fi

    if [ ${#install_files[@]} -eq 0 ]; then
        echo "离线依赖已满足，无需额外安装。"
        return 0
    fi

    echo "正在安装缺失的 Debian 离线依赖包..."
    dpkg --force-confdef --force-confold -i "${install_files[@]}"
    dpkg --configure -a
}

install_debian_from_directory() {
    local package_root=$1
    local package_dir
    local -a deb_files
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

    package_dir=$(resolve_debian_mysql_package_dir "$package_root")
    deb_files=("$package_dir"/*.deb)

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
        dpkg --force-confdef --force-confold -i "${install_queue[@]}"
        dpkg --configure -a
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
        dpkg --force-confdef --force-confold -i "${remaining_files[@]}"
    fi

    dpkg --configure -a

    if [ -z "$(find_mysql_binary)" ]; then
        echo "错误：离线 DEB 安装完成后未找到 mysqld，请检查安装包是否完整且与目标系统兼容"
        exit 1
    fi
}

# 读取 RedHat 主版本号，用于识别是否需要启用 EL7 严格离线路径。
get_redhat_major_version() {
    # 优先读取环境里已有的 VERSION_ID，减少重复解析系统文件。
    local version_id=${VERSION_ID:-}

    # 常规系统优先从 /etc/os-release 读取版本号。
    if [ -z "$version_id" ] && [ -f /etc/os-release ]; then
        version_id=$(grep '^VERSION_ID=' /etc/os-release | head -n 1 | cut -d'=' -f2 | tr -d '"')
    fi

    # 某些老系统可能没有完整的 os-release，因此退回 redhat-release。
    if [ -z "$version_id" ] && [ -f /etc/redhat-release ]; then
        version_id=$(grep -oE '[0-9]+' /etc/redhat-release | head -n 1 || true)
    fi

    # 这里只关心主版本号，因为离线兼容性按 EL7 判断即可。
    version_id=${version_id%%.*}
    printf '%s' "$version_id"
}

# 只有 EL7/CentOS7 才启用更严格的离线依赖校验，避免旧系统先删包再失败。
is_redhat_7() {
    # 非 RedHat 系列直接返回 false。
    [ "$OS" = "RedHat" ] || return 1
    # EL7 是当前离线 RPM 资源的目标平台。
    [ "$(get_redhat_major_version)" = "7" ]
}

# 标准化 RPM capability 文本，避免空白差异干扰依赖比对。
normalize_rpm_capability() {
    # rpm -qpR / --provides 的输出可能带不规则空白，这里统一压平。
    local capability=${1:-}
    printf '%s\n' "$capability" | awk 'NF { $1=$1; print }'
}

# rpmlib 内部能力不需要用户额外准备离线 RPM，因此在校验时直接跳过。
is_ignored_rpm_requirement() {
    local requirement=${1:-}

    # rpmlib(...) 是 RPM 自身内部能力，不属于离线包闭包缺失。
    case "$requirement" in
        ""|rpmlib\(*)
            return 0
            ;;
    esac

    return 1
}

# 汇总离线目录内所有 RPM 自身提供的 capability，供后续依赖闭包校验复用。
collect_local_rpm_capabilities() {
    local package_dir=$1
    local output_file=$2
    local rpm_file
    local provides_output

    # 先清空输出文件，避免重复调用时污染上次结果。
    : > "$output_file"

    # 逐个 RPM 读取 provides，确保后续校验只依赖本地目录能力集合。
    for rpm_file in "$package_dir"/*.rpm; do
        provides_output=$(rpm -qp --provides "$rpm_file" 2>/dev/null) || {
            echo "错误：读取 RPM 提供能力失败：$rpm_file"
            exit 1
        }
        printf '%s\n' "$provides_output" >> "$output_file"
    done

    # 统一格式并去重，减少能力比对时的噪音。
    awk 'NF { $1=$1; print }' "$output_file" | sort -u > "${output_file}.normalized"
    mv -f "${output_file}.normalized" "$output_file"
}

# 先判断离线目录本身是否已经覆盖 requirement，优先避免误报缺依赖。
local_rpm_capabilities_satisfy_requirement() {
    local requirement=$1
    local provides_file=$2
    local requirement_name=${requirement%% *}
    local provided_capability

    # 逐条扫描本地 provides，优先做完整 capability 匹配。
    while IFS= read -r provided_capability; do
        # 带版本比较的 requirement 必须完整命中，避免把低版本误判为满足。
        if [ "$provided_capability" = "$requirement" ]; then
            return 0
        fi

        # 只有不带版本比较的 requirement，才允许用 capability 名称做宽松匹配。
        if [ "$requirement_name" = "$requirement" ] && { [ "$provided_capability" = "$requirement_name" ] || [[ "$provided_capability" == "$requirement_name "* ]]; }; then
            return 0
        fi
    done < "$provides_file"

    return 1
}

# 再判断目标系统是否已安装该 capability，这样已有基础依赖时无需重复携带 RPM。
system_satisfies_rpm_requirement() {
    local requirement=$1
    local requirement_name=${requirement%% *}

    # 先按完整 requirement 查询，保留版本比较条件。
    rpm -q --whatprovides "$requirement" >/dev/null 2>&1 && return 0

    # 带版本比较的 requirement 不再做名称兜底，避免把不兼容版本误判为已安装。
    [ "$requirement_name" = "$requirement" ] || return 1

    # 仅对无版本比较的 requirement，按 capability 名称兜底匹配。
    rpm -q --whatprovides "$requirement_name" >/dev/null 2>&1
}

# 用禁用 repo 的事务测试提前验证安装计划，避免先删冲突包再在事务阶段失败。
validate_redhat_offline_transaction() {
    local package_dir=$1
    shift
    local output_file

    # 事务测试会覆盖依赖、冲突与替换关系，比纯 capability 校验更接近真实安装行为。
    output_file=$(mktemp)

    if ! yum --disablerepo='*' --setopt=tsflags=test localinstall -y "$@" >"$output_file" 2>&1; then
        echo "错误：严格离线模式事务预检失败，请检查以下输出："
        cat "$output_file"
        rm -f "$output_file"
        exit 1
    fi

    rm -f "$output_file"
}

# 在动系统包之前先做依赖闭包校验，避免先卸载 mariadb-libs 再在缺依赖处失败。
validate_redhat_offline_dependencies() {
    local package_dir=$1
    local provides_file
    local missing_file
    local rpm_file
    local requirements_output
    local requirement
    local normalized_requirement

    # 用临时文件保存本地 provides 和缺失项，便于去重和最终输出。
    provides_file=$(mktemp)
    missing_file=$(mktemp)

    # 先建立离线目录 capability 索引，后面每个 RPM 都复用这一份索引。
    collect_local_rpm_capabilities "$package_dir" "$provides_file"

    # 遍历目录内每个 RPM，逐项检查它声明的 requires 是否可被满足。
    for rpm_file in "$package_dir"/*.rpm; do
        requirements_output=$(rpm -qpR "$rpm_file" 2>/dev/null) || {
            rm -f "$provides_file" "$missing_file"
            echo "错误：读取 RPM 依赖能力失败：$rpm_file"
            exit 1
        }

        # 逐条处理 requirement，确保错误输出能精确到 capability 级别。
        while IFS= read -r requirement; do
            normalized_requirement=$(normalize_rpm_capability "$requirement")

            # 跳过无需人工补包的内部能力，避免误报。
            if is_ignored_rpm_requirement "$normalized_requirement"; then
                continue
            fi

            # 如果离线目录本身已经提供该能力，则视为闭包完整。
            if local_rpm_capabilities_satisfy_requirement "$normalized_requirement" "$provides_file"; then
                continue
            fi

            # 如果目标系统已安装该能力，也无需强制要求离线目录重复携带。
            if system_satisfies_rpm_requirement "$normalized_requirement"; then
                continue
            fi

            # 只有本地和系统都不满足时，才记为真正缺失的 capability。
            printf '%s\n' "$normalized_requirement" >> "$missing_file"
        done <<< "$requirements_output"
    done

    # 如果存在缺失 capability，则直接失败，阻止后续卸载冲突包。
    if [ -s "$missing_file" ]; then
        # 先去重排序，让错误信息稳定可读。
        sort -u -o "$missing_file" "$missing_file"
        echo "错误：严格离线模式下，离线目录缺少以下 RPM 依赖能力："
        cat "$missing_file"
        echo "请将满足以上能力的 EL7 RPM 一并放入目录：$package_dir"
        rm -f "$provides_file" "$missing_file"
        exit 1
    fi

    # 校验通过后清理临时文件，避免污染系统临时目录。
    rm -f "$provides_file" "$missing_file"
}

install_redhat_from_directory() {
    local package_dir=$1
    local -a rpm_files=("$package_dir"/*.rpm)

    # 离线目录为空时直接失败，避免后续 yum 操作空跑。
    if [ ${#rpm_files[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 .rpm 文件：$package_dir"
        exit 1
    fi

    # 主 RPM 不存在时直接报错，这是 MySQL 安装的最基本前提。
    if ! ls "$package_dir"/mysql-community-server-*.rpm >/dev/null 2>&1 && ! ls "$package_dir"/mysql-server-*.rpm >/dev/null 2>&1; then
        echo "错误：离线目录中缺少 MySQL 8.x 主 RPM 包：$package_dir"
        exit 1
    fi

    # EL7/CentOS7 先做严格离线依赖校验，防止先删冲突包再中途失败。
    if is_redhat_7; then
        echo "检测到 EL7/CentOS7，启用严格离线模式..."
        echo "正在校验离线 RPM 依赖能力..."
        validate_redhat_offline_dependencies "$package_dir"
        echo "正在执行离线事务预检..."
        validate_redhat_offline_transaction "$package_dir" "${rpm_files[@]}"
    fi

    echo "正在使用离线 RPM 目录安装 MySQL..."
    echo "当前 yum 操作已禁用全部外部 repo。"
    # 删除与 MySQL 冲突的 mariadb-libs，但不允许访问任何外部仓库。
    yum --disablerepo='*' remove -y mariadb-libs 2>/dev/null || true
    # 仅使用本地 RPM 完成安装，彻底隔离坏掉的第三方 repo。
    yum --disablerepo='*' localinstall -y "${rpm_files[@]}"
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

run_mysql_query() {
    local sql=$1
    local mysql_cmd=$2
    local temp_password=${3:-}

    if timeout 10s "$mysql_cmd" --connect-timeout=5 -N -s -uroot -e "$sql" 2>/dev/null; then
        return 0
    fi

    if [ -n "$temp_password" ] && timeout 10s "$mysql_cmd" --connect-timeout=5 --connect-expired-password -N -s -uroot -p"$temp_password" -e "$sql" 2>/dev/null; then
        return 0
    fi

    timeout 10s "$mysql_cmd" --connect-timeout=5 -N -s -uroot -p"$ROOT_PASSWORD" -e "$sql" 2>/dev/null
}

require_mysql_sql() {
    local sql=$1
    local error_message=$2
    local mysql_cmd=$3
    local temp_password=${4:-}

    if ! run_mysql_sql "$sql" "$mysql_cmd" "$temp_password"; then
        echo "错误：$error_message"
        exit 1
    fi
}

echo "PROGRESS:Installing:20"
if [ "$PACKAGE_IS_DIRECTORY" = true ]; then
    if [ "$OS" = "Debian" ]; then
        prepare_debian_offline_dependencies "$PACKAGE_PATH"
        install_debian_from_directory "$PACKAGE_PATH"
    else
        install_redhat_from_directory "$PACKAGE_PATH"
    fi
elif [ "$PACKAGE_IS_FILE" = true ]; then
    case "$PACKAGE_PATH" in
        *.deb)
            prepare_debian_offline_dependencies "$(dirname "$PACKAGE_PATH")"
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
    # MySQL Linux 安装在当前项目中必须使用本地离线资源，避免在目标机器上隐式联网拉包。
    echo "错误：MySQL Linux 安装仅支持本地离线资源"
    echo "请通过 PACKAGE_PATH 提供离线包目录或归档文件。"
    echo "可用目录示例：Scripts/MySQL/mysql-ubuntu/22、Scripts/MySQL/mysql-ubuntu/24、Scripts/MySQL/mysql-centos7"
    exit 1
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

if [ -n "$DATA_DIRECTORY" ]; then
    mkdir -p "$DATA_DIRECTORY"
    chown -R mysql:mysql "$DATA_DIRECTORY"
    ensure_mysqld_option "$CONFIG_FILE" datadir "$DATA_DIRECTORY"
fi

echo "PROGRESS:Starting:65"
SERVICE_NAME=$(detect_mysql_service)
echo "检测到服务名: $SERVICE_NAME"

if command -v systemctl >/dev/null 2>&1; then
    SERVICE_MASKED=false
    UNMASK_ATTEMPTED=false
    if [ "$(systemctl is-enabled "$SERVICE_NAME" 2>/dev/null || true)" = "masked" ]; then
        SERVICE_MASKED=true
        UNMASK_ATTEMPTED=true
        systemctl unmask "$SERVICE_NAME" >/dev/null 2>&1 || true
    fi

    echo "SERVICE_NAME: $SERVICE_NAME"
    echo "SERVICE_MASKED: $SERVICE_MASKED"
    echo "UNMASK_ATTEMPTED: $UNMASK_ATTEMPTED"

    systemctl daemon-reload || true
    systemctl enable "$SERVICE_NAME" >/dev/null 2>&1 || true
    systemctl restart "$SERVICE_NAME" || systemctl start "$SERVICE_NAME"
else
    service "$SERVICE_NAME" restart
fi

wait_for_service_ready "$SERVICE_NAME"

MYSQL_CMD=$(find_mysql_command)
MYSQLADMIN_CMD=$(find_mysqladmin_command)
TEMP_PASSWORD=$(find_temp_password || true)
ESCAPED_ROOT_PASSWORD=$(sql_escape "$ROOT_PASSWORD")

echo "PROGRESS:Configuring:80"
if [ -z "$MYSQL_CMD" ]; then
    echo "错误：安装后未找到 mysql 客户端命令，无法完成 root 账户初始化"
    exit 1
fi

run_mysql_sql "ALTER USER 'root'@'localhost' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "$MYSQL_CMD" "$TEMP_PASSWORD" || true

if [ "$ALLOW_REMOTE" = "true" ]; then
    require_mysql_sql "CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "创建 root@'%' 用户失败" "$MYSQL_CMD" "$TEMP_PASSWORD"
    require_mysql_sql "ALTER USER 'root'@'%' IDENTIFIED BY '$ESCAPED_ROOT_PASSWORD';" "更新 root@'%' 密码失败" "$MYSQL_CMD" "$TEMP_PASSWORD"
    require_mysql_sql "GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;" "授予 root@'%' 远程权限失败" "$MYSQL_CMD" "$TEMP_PASSWORD"
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

if [ "$ALLOW_REMOTE" = "true" ]; then
    current_bind_address=$(grep -E '^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)' "$CONFIG_FILE" 2>/dev/null | head -n 1 | sed -E 's/^[[:space:]]*bind-address([[:space:]]*=|[[:space:]]+)//' | tr -d '\r')
    if [ "$current_bind_address" != "0.0.0.0" ]; then
        echo "错误：已启用远程访问，但 bind-address 未配置为 0.0.0.0"
        exit 1
    fi

    remote_root_count=$(run_mysql_query "SELECT COUNT(*) FROM mysql.user WHERE User='root' AND Host='%';" "$MYSQL_CMD" "$TEMP_PASSWORD" | tail -n 1 | tr -d '[:space:]')
    if [ -z "$remote_root_count" ] || [ "$remote_root_count" = "0" ]; then
        echo "错误：MySQL 本地可用，但 root@'%' 远程账户未创建成功"
        exit 1
    fi

    if ! run_mysql_sql "SHOW GRANTS FOR 'root'@'%';" "$MYSQL_CMD" "$TEMP_PASSWORD"; then
        echo "错误：MySQL 本地可用，但 root@'%' 远程授权未生效"
        exit 1
    fi
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
