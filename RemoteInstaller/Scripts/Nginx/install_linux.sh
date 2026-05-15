#!/bin/bash
set -e

# 参数定义
# PACKAGE_PATH: 远程安装包路径（文件或目录）
# PORT: 服务端口 (默认 80)

LOG_FILE="install_nginx.log"
exec > >(tee -a "$LOG_FILE") 2>&1

shopt -s nullglob

echo "PROGRESS:Initializing:5"
echo "Nginx 安装脚本开始..."
echo "当前工作目录: $(pwd)"

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

PORT=${PORT:-80}
PACKAGE_PATH=${PACKAGE_PATH:-}
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

if [ -f /etc/debian_version ]; then
    OS="Debian"
elif [ -f /etc/redhat-release ]; then
    OS="RedHat"
else
    echo "不支持的操作系统"
    exit 1
fi

echo "检测到操作系统: $OS"
echo "离线包路径: ${PACKAGE_PATH:-<未提供>}"

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

validate_nginx_port() {
    local port=$1

    if [[ ! "$port" =~ ^[0-9]+$ ]] || [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
        echo "错误：无效的 Nginx 端口：<不安全端口，已拒绝>"
        exit 1
    fi
}

is_debian_nginx_installed() {
    dpkg-query -W -f='${Status}' nginx 2>/dev/null | grep -q 'install ok installed'
}

is_rpm_nginx_installed() {
    rpm -q nginx >/dev/null 2>&1
}

install_nginx_port_selinux_policy_module_from_cil() {
    local module_name=$1
    local port=$2
    local work_dir=$3
    local cil_file="$work_dir/${module_name}.cil"

    command -v semodule >/dev/null 2>&1 || return 1

    cat > "$cil_file" <<EOF
(block $module_name
    (portcon tcp $port (system_u object_r http_port_t ((s0)(s0))))
)
EOF

    semodule -i "$cil_file" >/dev/null 2>&1
}

install_nginx_port_selinux_policy_module_from_te() {
    local module_name=$1
    local port=$2
    local work_dir=$3
    local policy_file="$work_dir/${module_name}.te"
    local mod_file="$work_dir/${module_name}.mod"
    local pp_file="$work_dir/${module_name}.pp"

    command -v checkmodule >/dev/null 2>&1 || return 1
    command -v semodule_package >/dev/null 2>&1 || return 1
    command -v semodule >/dev/null 2>&1 || return 1

    cat > "$policy_file" <<EOF
module ${module_name} 1.0;

require {
    type http_port_t;
}

portcon tcp $port system_u:object_r:http_port_t:s0;
EOF

    checkmodule -M -m -o "$mod_file" "$policy_file" >/dev/null 2>&1 && \
        semodule_package -o "$pp_file" -m "$mod_file" >/dev/null 2>&1 && \
        semodule -i "$pp_file" >/dev/null 2>&1
}

ensure_nginx_selinux_state_dir() {
    local state_dir=$1
    local owner_id
    local group_id
    local permissions

    if [ -L "$state_dir" ]; then
        echo "错误：SELinux 状态目录不可信：$state_dir"
        return 1
    fi

    install -d -o root -g root -m 700 "$state_dir" || return 1

    if [ -L "$state_dir" ] || [ ! -d "$state_dir" ]; then
        echo "错误：SELinux 状态目录不可信：$state_dir"
        return 1
    fi

    owner_id=$(stat -c '%u' "$state_dir" 2>/dev/null || echo "")
    group_id=$(stat -c '%g' "$state_dir" 2>/dev/null || echo "")
    permissions=$(stat -c '%a' "$state_dir" 2>/dev/null || echo "")
    if [ "$owner_id" != "0" ] || [ "$group_id" != "0" ] || [ "$permissions" != "700" ]; then
        echo "错误：SELinux 状态目录权限不可信：$state_dir"
        return 1
    fi
}

write_nginx_selinux_state_file() {
    local state_file=$1
    local module_name=$2
    local port=$3
    local module_created=${4:-false}
    local semanage_added=${5:-false}
    local state_dir
    local tmp_file

    state_dir=$(dirname "$state_file")
    ensure_nginx_selinux_state_dir "$state_dir" || return 1

    tmp_file=$(mktemp "$state_dir/.selinux-port-${port}.XXXXXX") || return 1
    {
        if [ -n "$module_name" ]; then
            echo "module=$module_name"
        fi
        echo "port=$port"
        echo "owner=remote-installer"
        echo "module_created=$module_created"
        if [ "$semanage_added" = "true" ]; then
            echo "semanage=added"
        fi
    } > "$tmp_file" || {
        rm -f "$tmp_file"
        return 1
    }

    chmod 600 "$tmp_file" 2>/dev/null || {
        rm -f "$tmp_file"
        return 1
    }

    mv -f "$tmp_file" "$state_file" || {
        rm -f "$tmp_file"
        return 1
    }
}

nginx_selinux_state_file_matches_module() {
    local state_file=$1
    local module_name=$2
    local port=$3
    local state_dir
    local owner_id
    local permissions

    state_dir=$(dirname "$state_file")
    ensure_nginx_selinux_state_dir "$state_dir" || return 1
    [ -f "$state_file" ] || return 1
    [ ! -L "$state_file" ] || return 1

    owner_id=$(stat -c '%u' "$state_file" 2>/dev/null || echo "")
    permissions=$(stat -c '%a' "$state_file" 2>/dev/null || echo "")
    [ "$owner_id" = "0" ] || return 1
    [ -n "$permissions" ] && [ $((8#$permissions & 022)) -eq 0 ] || return 1
    grep -Fxq "owner=remote-installer" "$state_file" || return 1
    grep -Fxq "module=$module_name" "$state_file" || return 1
    grep -Fxq "port=$port" "$state_file" || return 1
    grep -Fxq "module_created=true" "$state_file" || return 1
}

install_nginx_port_selinux_policy_module() {
    local port=$1
    local module_name="remote_installer_nginx_port_${port}"
    local state_file="/var/lib/remote-installer/nginx/selinux-port-${port}.state"
    local work_dir=""

    if [ "$port" -lt 1024 ]; then
        echo "警告：SELinux 已启用但端口 $port 不是非保留端口，无法使用本地策略模块替代 semanage port"
        return 1
    fi

    if ! command -v semodule >/dev/null 2>&1; then
        echo "警告：SELinux 已启用但缺少 semodule，无法加载 Nginx 端口策略模块"
        return 1
    fi

    if semodule -l 2>/dev/null | awk '{print $1}' | grep -qx "$module_name"; then
        if nginx_selinux_state_file_matches_module "$state_file" "$module_name" "$port"; then
            return 0
        fi

        echo "错误：SELinux 策略模块 $module_name 已存在但缺少可信状态文件，拒绝接管"
        return 1
    fi

    work_dir=$(mktemp -d "/tmp/${module_name}.XXXXXX") || return 1
    chmod 700 "$work_dir" 2>/dev/null || true

    if install_nginx_port_selinux_policy_module_from_cil "$module_name" "$port" "$work_dir" || \
        install_nginx_port_selinux_policy_module_from_te "$module_name" "$port" "$work_dir"; then
        rm -rf "$work_dir"
        if ! write_nginx_selinux_state_file "$state_file" "$module_name" "$port" true; then
            semodule -r "$module_name" 2>/dev/null || true
            echo "警告：记录 Nginx SELinux 端口策略状态失败，已回滚策略模块"
            return 1
        fi
        echo "已加载 Nginx SELinux 端口策略模块：$module_name"
        return 0
    fi

    rm -rf "$work_dir"
    echo "警告：加载 Nginx SELinux 端口策略模块失败，服务启动可能被 SELinux 拒绝"
    return 1
}

record_nginx_semanage_port_mapping() {
    local port=$1
    local state_file="/var/lib/remote-installer/nginx/selinux-port-${port}.state"

    write_nginx_selinux_state_file "$state_file" "" "$port" false true
}

selinux_port_is_http_type() {
    local port=$1

    semanage port -l 2>/dev/null | awk -v port="$port" '
        $1 == "http_port_t" && $2 == "tcp" {
            for (index = 3; index <= NF; index++) {
                gsub(/,/, "", $index)
                split($index, bounds, "-")
                if ((bounds[2] == "" && bounds[1] == port) || (bounds[2] != "" && port >= bounds[1] && port <= bounds[2])) {
                    found = 1
                }
            }
        }
        END { exit found ? 0 : 1 }
    '
}

selinux_port_has_any_type() {
    local port=$1

    semanage port -l 2>/dev/null | awk -v port="$port" '
        $2 == "tcp" {
            for (index = 3; index <= NF; index++) {
                gsub(/,/, "", $index)
                split($index, bounds, "-")
                if ((bounds[2] == "" && bounds[1] == port) || (bounds[2] != "" && port >= bounds[1] && port <= bounds[2])) {
                    found = 1
                }
            }
        }
        END { exit found ? 0 : 1 }
    '
}

apply_nginx_port_security_context() {
    local port=$1
    local selinux_status=""

    if ! command -v getenforce >/dev/null 2>&1; then
        return 0
    fi

    selinux_status=$(getenforce 2>/dev/null || true)
    if [ "$selinux_status" != "Enforcing" ] && [ "$selinux_status" != "Permissive" ]; then
        return 0
    fi

    if ! command -v semanage >/dev/null 2>&1; then
        echo "警告：SELinux 已启用但缺少 semanage，无法持久授权 Nginx 端口 $port，正在尝试加载端口级 Nginx 策略模块"
        if ! install_nginx_port_selinux_policy_module "$port" && [ "$selinux_status" = "Enforcing" ]; then
            echo "错误：SELinux Enforcing 下无法授权 Nginx 端口 $port"
            exit 1
        fi
        return 0
    fi

    if selinux_port_is_http_type "$port"; then
        return 0
    fi

    if selinux_port_has_any_type "$port"; then
        echo "错误：SELinux 端口 $port 已被其他类型占用，拒绝改写现有策略"
        exit 1
    fi

    if semanage port -a -t http_port_t -p tcp "$port" 2>/dev/null; then
        if ! record_nginx_semanage_port_mapping "$port"; then
            semanage port -d -p tcp "$port" 2>/dev/null || true
            echo "错误：记录 Nginx SELinux 端口状态失败，已回滚端口授权"
            exit 1
        fi
        return 0
    fi

    if ! install_nginx_port_selinux_policy_module "$port" && [ "$selinux_status" = "Enforcing" ]; then
        echo "错误：SELinux Enforcing 下无法授权 Nginx 端口 $port"
        exit 1
    fi
}

get_debian_dependency_search_directories() {
    local package_root=$1

    if [ -d "$package_root/deps" ]; then
        echo "$package_root/deps"
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

    if [[ "$version_id" == 24* ]]; then
        cat <<EOF
iproute2
libssl3t64
libc6
libcrypt1
libpcre2-8-0
zlib1g
EOF
    else
        cat <<EOF
adduser
lsb-base
libssl3
libc6
libcrypt1
libpcre2-8-0
zlib1g
EOF
    fi
}

is_debian_package_installed() {
    local package_name=$1
    dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null | grep -q 'install ok installed'
}

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
        echo "错误：离线目录缺少以下 Nginx Debian 依赖包："
        printf '%s\n' "${missing_packages[@]}"
        echo "请将对应 .deb 文件放入目录：$package_root/deps（兼容旧布局时也可放在根目录）"
        exit 1
    fi

    if [ ${#install_files[@]} -eq 0 ]; then
        echo "Nginx 离线依赖已满足，无需额外安装。"
        return 0
    fi

    echo "正在安装缺失的 Nginx Debian 离线依赖包..."
    DEBIAN_FRONTEND=noninteractive dpkg --force-confdef --force-confold -i "${install_files[@]}"
}

install_debian_offline() {
    local -a deb_files=("$@")
    local -a dependency_debs=()
    local -a common_debs=()
    local -a main_debs=()
    local file_name

    if [ ${#deb_files[@]} -eq 0 ]; then
        echo "错误：未找到可安装的 .deb 文件"
        exit 1
    fi

    for file_path in "${deb_files[@]}"; do
        file_name=$(basename "$file_path")
        case "$file_name" in
            nginx-common_*.deb)
                common_debs+=("$file_path")
                ;;
            nginx_*.deb|nginx-core_*.deb|nginx-full_*.deb|nginx-light_*.deb|nginx-extras_*.deb)
                main_debs+=("$file_path")
                ;;
            *)
                dependency_debs+=("$file_path")
                ;;
        esac
    done

    local version_id=${VERSION_ID:-}
    if [ -z "$version_id" ] && [ -f /etc/os-release ]; then
        version_id=$(grep '^VERSION_ID=' /etc/os-release | head -n 1 | cut -d'=' -f2 | tr -d '"')
    fi

    if [[ "$version_id" == 24* ]] && [ ${#common_debs[@]} -eq 0 ]; then
        echo "错误：离线目录中缺少 nginx-common_*.deb"
        exit 1
    fi

    if [ ${#main_debs[@]} -eq 0 ]; then
        echo "错误：离线目录中未找到 Nginx 主 .deb 包"
        exit 1
    fi

    echo "检测到 .deb 文件数：${#deb_files[@]}"
    echo "依赖包数：${#dependency_debs[@]}，公共包数：${#common_debs[@]}，主包数：${#main_debs[@]}"

    local -a dpkg_opts=(--force-confdef --force-confold)

    if [ ${#dependency_debs[@]} -gt 0 ]; then
        echo "先安装依赖 .deb 包..."
        DEBIAN_FRONTEND=noninteractive dpkg "${dpkg_opts[@]}" -i "${dependency_debs[@]}"
    fi

    if [ ${#common_debs[@]} -gt 0 ]; then
        echo "安装 nginx-common .deb 包..."
        DEBIAN_FRONTEND=noninteractive dpkg "${dpkg_opts[@]}" -i "${common_debs[@]}"
    fi

    echo "安装 Nginx 主 .deb 包..."
    DEBIAN_FRONTEND=noninteractive dpkg "${dpkg_opts[@]}" -i "${main_debs[@]}"

    if ! is_debian_nginx_installed; then
        echo "错误：Nginx Debian 离线安装失败，请检查离线包依赖是否完整"
        exit 1
    fi
}

run_redhat_localinstall() {
    if command -v dnf >/dev/null 2>&1; then
        dnf --disablerepo='*' localinstall -y "$@"
    else
        yum --disablerepo='*' localinstall -y "$@"
    fi
}

validate_redhat_offline_dependencies() {
    local package_root=$1
    local -a required_packages=("pcre2" "openssl-libs" "glibc" "procps-ng" "shadow-utils" "systemd")
    local -a rpm_names=()
    local -a missing_packages=()
    local package_name

    mapfile -t rpm_names < <(find "$package_root" -maxdepth 2 -type f -name "*.rpm" -printf '%f\n' | sort -u)

    for package_name in "${required_packages[@]}"; do
        if rpm -q "$package_name" >/dev/null 2>&1; then
            continue
        fi

        if ! printf '%s\n' "${rpm_names[@]}" | grep -q "^${package_name}-"; then
            missing_packages+=("$package_name")
        fi
    done

    if [ ${#missing_packages[@]} -gt 0 ]; then
        echo "错误：离线目录缺少以下 Nginx RPM 依赖包：${missing_packages[*]}"
        exit 1
    fi
}

get_rpm_package_name() {
    local file_path=$1

    rpm -qp --queryformat '%{NAME}' "$file_path" 2>/dev/null
}

validate_nginx_main_rpm_package() {
    local file_path=$1
    local rpm_name

    if ! rpm_name=$(get_rpm_package_name "$file_path"); then
        echo "错误：无法读取 Nginx 主 RPM 包名：$file_path"
        exit 1
    fi

    if [ "$rpm_name" != "nginx" ]; then
        echo "错误：Nginx 主 RPM 包元数据不匹配：$file_path"
        exit 1
    fi
}

select_redhat_offline_rpms() {
    local -a rpm_files=("$@")
    local file_path
    local file_name
    local rpm_name
    local main_found=false

    SELECTED_REDHAT_RPM_FILES=()

    for file_path in "${rpm_files[@]}"; do
        file_name=$(basename "$file_path")
        case "$file_name" in
            nginx-[0-9]*.rpm)
                validate_nginx_main_rpm_package "$file_path"
                main_found=true
                SELECTED_REDHAT_RPM_FILES+=("$file_path")
                ;;
            *)
                if ! rpm_name=$(get_rpm_package_name "$file_path"); then
                    echo "错误：无法读取 RPM 包名：$file_path"
                    exit 1
                fi

                if rpm -q "$rpm_name" >/dev/null 2>&1; then
                    echo "跳过已安装的 Nginx 离线依赖 RPM，避免触发系统包升级：$rpm_name"
                    continue
                fi

                SELECTED_REDHAT_RPM_FILES+=("$file_path")
                ;;
        esac
    done

    if [ "$main_found" != "true" ]; then
        echo "错误：离线目录中未找到 Nginx 主 .rpm 包"
        exit 1
    fi
}

install_redhat_offline() {
    local -a rpm_files=("$@")

    if [ ${#rpm_files[@]} -eq 0 ]; then
        echo "错误：未找到可安装的 .rpm 文件"
        exit 1
    fi

    select_redhat_offline_rpms "${rpm_files[@]}"

    echo "检测到 .rpm 文件数：${#rpm_files[@]}，本次安装事务文件数：${#SELECTED_REDHAT_RPM_FILES[@]}"
    run_redhat_localinstall "${SELECTED_REDHAT_RPM_FILES[@]}"

    if ! is_rpm_nginx_installed; then
        echo "错误：Nginx RPM 离线安装失败，请检查离线包依赖是否完整"
        exit 1
    fi
}

install_nginx() {
    echo "PROGRESS:Installing:20"

    if [ "$OS" = "Debian" ]; then
        if [ "$PACKAGE_IS_DIRECTORY" = "true" ]; then
            prepare_debian_offline_dependencies "$PACKAGE_PATH"
            mapfile -t deb_files < <(find "$PACKAGE_PATH" -maxdepth 1 -type f -name "*.deb" | sort)
            install_debian_offline "${deb_files[@]}"
            return
        fi

        if [ "$PACKAGE_IS_FILE" = "true" ]; then
            case "$PACKAGE_PATH" in
                *.deb)
                    prepare_debian_offline_dependencies "$(dirname "$PACKAGE_PATH")"
                    mapfile -t deb_files < <(find "$(dirname "$PACKAGE_PATH")" -maxdepth 1 -type f -name "*.deb" | sort)
                    install_debian_offline "${deb_files[@]}"
                    return
                    ;;
                *)
                    echo "错误：Debian 离线安装仅支持 .deb 文件或目录"
                    exit 1
                    ;;
            esac
        fi

        echo "错误：Nginx Linux 严格离线安装要求显式提供 PACKAGE_PATH，且目录中必须包含依赖包以及当前发行版要求的 Nginx 主包/公共包。"
        exit 1
    fi

    if [ "$PACKAGE_IS_DIRECTORY" = "true" ]; then
        validate_redhat_offline_dependencies "$PACKAGE_PATH"
        mapfile -t rpm_files < <(find "$PACKAGE_PATH" -maxdepth 2 -type f -name "*.rpm" | sort)
        install_redhat_offline "${rpm_files[@]}"
        return
    fi

    if [ "$PACKAGE_IS_FILE" = "true" ]; then
        case "$PACKAGE_PATH" in
            *.rpm)
                validate_redhat_offline_dependencies "$(dirname "$PACKAGE_PATH")"
                mapfile -t rpm_files < <(find "$(dirname "$PACKAGE_PATH")" -maxdepth 2 -type f -name "*.rpm" | sort)
                install_redhat_offline "${rpm_files[@]}"
                return
                ;;
            *)
                echo "错误：RedHat 离线安装仅支持 .rpm 文件或目录"
                exit 1
                ;;
        esac
    fi

    echo "错误：Nginx Linux 严格离线安装要求显式提供 PACKAGE_PATH，且目录中必须包含依赖包和主包。"
    exit 1
}

get_config_candidates() {
    local -a files=()
    local file

    for file in \
        /etc/nginx/sites-available/default \
        /etc/nginx/sites-enabled/default \
        /etc/nginx/conf.d/default.conf \
        /etc/nginx/conf.d/*.conf \
        /etc/nginx/nginx.conf \
        /usr/local/nginx/conf/nginx.conf; do
        if [ -f "$file" ]; then
            files+=("$file")
        fi
    done

    printf '%s\n' "${files[@]}"
}

update_port_in_file() {
    local file=$1
    local before after

    before=$(cksum < "$file")
    sed -i -E "/^[[:space:]]*listen[[:space:]]+/ {
        s/\[::\]:80([[:space:];])/[::]:$PORT\1/g
        s/\*:80([[:space:];])/*:$PORT\1/g
        s/:80([[:space:];])/:$PORT\1/g
        s/listen[[:space:]]+80([[:space:];])/listen $PORT\1/g
    }" "$file"
    after=$(cksum < "$file")

    [ "$before" != "$after" ]
}

configure_port() {
    echo "PROGRESS:Configuring:60"

    if [ "$PORT" = "80" ]; then
        echo "端口为默认 80，跳过监听端口修改"
        return
    fi

    local changed=false
    local candidate
    local -a candidates=()

    mapfile -t candidates < <(get_config_candidates)

    if [ ${#candidates[@]} -eq 0 ]; then
        echo "错误：未找到可修改的 Nginx 配置文件"
        exit 1
    fi

    for candidate in "${candidates[@]}"; do
        if grep -Eq '^[[:space:]]*listen[[:space:]]+.*80' "$candidate"; then
            echo "正在更新监听端口：$candidate"
            if update_port_in_file "$candidate"; then
                changed=true
            fi
        fi
    done

    if [ "$changed" != "true" ]; then
        echo "错误：未在现有配置中找到可替换的 listen 80 配置，请检查离线包默认配置"
        exit 1
    fi
}

write_nginx_firewall_state_file() {
    local backend=$1
    local port=$2
    local state_dir="/var/lib/remote-installer/nginx"
    local state_file="$state_dir/firewall-port-${port}.state"
    local tmp_file

    ensure_nginx_selinux_state_dir "$state_dir" || return 1

    tmp_file=$(mktemp "$state_dir/.firewall-port-${port}.XXXXXX") || return 1
    {
        echo "owner=remote-installer"
        echo "backend=$backend"
        echo "port=$port"
    } > "$tmp_file" || {
        rm -f "$tmp_file"
        return 1
    }

    chmod 600 "$tmp_file" 2>/dev/null || {
        rm -f "$tmp_file"
        return 1
    }

    mv -f "$tmp_file" "$state_file" || {
        rm -f "$tmp_file"
        return 1
    }
}

firewalld_port_exists() {
    local port=$1

    firewall-cmd --permanent --query-port=${port}/tcp >/dev/null 2>&1 || firewall-cmd --query-port=${port}/tcp >/dev/null 2>&1
}

ufw_port_exists() {
    local port=$1

    ufw status 2>/dev/null | awk -v port="$port" '$1 ~ "^" port "/tcp$" && $2 == "ALLOW" { found=1 } END { exit found ? 0 : 1 }'
}

configure_firewall() {
    if command -v firewall-cmd >/dev/null 2>&1; then
        if firewalld_port_exists "$PORT"; then
            echo "firewalld 已存在 $PORT/tcp 规则，视为管理员规则，不记录安装器状态"
            return 0
        fi

        echo "正在配置 firewalld 开放 $PORT 端口..."
        if firewall-cmd --permanent --add-port=${PORT}/tcp 2>/dev/null; then
            firewall-cmd --reload 2>/dev/null || true
            write_nginx_firewall_state_file "firewalld" "$PORT" || echo "警告：记录 Nginx firewalld 状态失败，卸载时将保留该端口规则"
        fi
    elif command -v ufw >/dev/null 2>&1; then
        if ufw_port_exists "$PORT"; then
            echo "ufw 已存在 $PORT/tcp 规则，视为管理员规则，不记录安装器状态"
            return 0
        fi

        echo "正在配置 ufw 开放 $PORT 端口..."
        if ufw allow ${PORT}/tcp 2>/dev/null; then
            write_nginx_firewall_state_file "ufw" "$PORT" || echo "警告：记录 Nginx ufw 状态失败，卸载时将保留该端口规则"
        fi
    fi
}

configure_nginx_runtime_directory() {
    local runtime_dir="/run/nginx"
    local pid_file="$runtime_dir/nginx.pid"
    local config_file="/etc/nginx/nginx.conf"
    local selinux_status=""
    local owner_id
    local group_id
    local permissions
    local runtime_owner="root"
    local runtime_group="root"
    local expected_owner_id
    local expected_group_id

    if id nginx >/dev/null 2>&1; then
        runtime_owner="nginx"
        runtime_group="nginx"
    elif id www-data >/dev/null 2>&1; then
        runtime_owner="www-data"
        runtime_group="www-data"
    fi

    if [ -L "$runtime_dir" ]; then
        echo "错误：Nginx 运行目录不可信：$runtime_dir"
        exit 1
    fi

    install -d -o "$runtime_owner" -g "$runtime_group" -m 755 "$runtime_dir"

    if [ -L "$runtime_dir" ] || [ ! -d "$runtime_dir" ]; then
        echo "错误：Nginx 运行目录不可信：$runtime_dir"
        exit 1
    fi

    expected_owner_id=$(id -u "$runtime_owner" 2>/dev/null || echo "0")
    expected_group_id=$(getent group "$runtime_group" 2>/dev/null | cut -d: -f3)
    [ -n "$expected_group_id" ] || expected_group_id="0"
    owner_id=$(stat -c '%u' "$runtime_dir" 2>/dev/null || echo "")
    group_id=$(stat -c '%g' "$runtime_dir" 2>/dev/null || echo "")
    permissions=$(stat -c '%a' "$runtime_dir" 2>/dev/null || echo "")
    if [ "$owner_id" != "$expected_owner_id" ] || [ "$group_id" != "$expected_group_id" ] || [ "$permissions" != "755" ]; then
        echo "错误：Nginx 运行目录权限不可信：$runtime_dir"
        exit 1
    fi

    if [ -f "$config_file" ]; then
        if grep -Eq '^[[:space:]]*pid[[:space:]]+' "$config_file"; then
            sed -i -E "s|^[[:space:]]*pid[[:space:]]+[^;]+;|pid $pid_file;|" "$config_file"
        else
            sed -i "1ipid $pid_file;" "$config_file"
        fi
    fi

    if command -v getenforce >/dev/null 2>&1; then
        selinux_status=$(getenforce 2>/dev/null || true)
        if { [ "$selinux_status" = "Enforcing" ] || [ "$selinux_status" = "Permissive" ]; } && command -v chcon >/dev/null 2>&1; then
            chcon -t httpd_var_run_t "$runtime_dir" 2>/dev/null || true
        fi
    fi

    if command -v systemctl >/dev/null 2>&1; then
        local override_dir="/etc/systemd/system/nginx.service.d"
        local override_file="$override_dir/remote-installer-pid.conf"

        mkdir -p "$override_dir"
        cat > "$override_file" <<EOF
[Service]
PIDFile=$pid_file
EOF
        systemctl daemon-reload || true
    fi
}

start_service() {
    echo "PROGRESS:Starting:80"

    if ! command -v nginx >/dev/null 2>&1; then
        echo "错误：安装后未找到 nginx 命令"
        exit 1
    fi

    nginx -t

    if command -v systemctl >/dev/null 2>&1; then
        systemctl enable nginx
        systemctl restart nginx
    else
        service nginx restart
    fi

    if ! is_port_listening "$PORT"; then
        echo "错误：Nginx 启动后端口 $PORT 未监听"
        exit 1
    fi
}

validate_nginx_port "$PORT"
install_nginx
configure_port
configure_nginx_runtime_directory
apply_nginx_port_security_context "$PORT"
configure_firewall
start_service

echo "PROGRESS:Finishing:95"
echo "Nginx 安装完成，端口: $PORT"
echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: true"
echo "RUNNING: true"
echo "PORT: $PORT"
echo "STAGE:SUCCESS"
echo "------------------------"
echo "STAGE:SUCCESS"
