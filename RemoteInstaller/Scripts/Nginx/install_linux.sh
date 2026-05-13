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

is_debian_nginx_installed() {
    dpkg-query -W -f='${Status}' nginx 2>/dev/null | grep -q 'install ok installed'
}

is_rpm_nginx_installed() {
    rpm -q nginx >/dev/null 2>&1
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
    local -a required_prefixes=("pcre2-" "openssl-libs-" "glibc-" "procps-ng-" "shadow-utils-" "systemd-")
    local -a rpm_names=()
    local -a missing_prefixes=()
    local rpm_name

    mapfile -t rpm_names < <(find "$package_root" -maxdepth 2 -type f -name "*.rpm" -printf '%f\n' | sort -u)

    for prefix in "${required_prefixes[@]}"; do
        if ! printf '%s\n' "${rpm_names[@]}" | grep -q "^${prefix}"; then
            missing_prefixes+=("$prefix")
        fi
    done

    if [ ${#missing_prefixes[@]} -gt 0 ]; then
        echo "错误：离线目录缺少以下 Nginx RPM 依赖包：${missing_prefixes[*]}"
        exit 1
    fi
}

install_redhat_offline() {
    local -a rpm_files=("$@")
    local main_found=false
    local file_name

    if [ ${#rpm_files[@]} -eq 0 ]; then
        echo "错误：未找到可安装的 .rpm 文件"
        exit 1
    fi

    for file_path in "${rpm_files[@]}"; do
        file_name=$(basename "$file_path")
        case "$file_name" in
            nginx-[0-9]*.rpm)
                main_found=true
                ;;
        esac
    done

    if [ "$main_found" != "true" ]; then
        echo "错误：离线目录中未找到 Nginx 主 .rpm 包"
        exit 1
    fi

    echo "检测到 .rpm 文件数：${#rpm_files[@]}"
    run_redhat_localinstall "${rpm_files[@]}"

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

configure_firewall() {
    if command -v firewall-cmd >/dev/null 2>&1; then
        echo "正在配置 firewalld 开放 $PORT 端口..."
        firewall-cmd --permanent --add-port=${PORT}/tcp 2>/dev/null || true
        firewall-cmd --reload 2>/dev/null || true
    elif command -v ufw >/dev/null 2>&1; then
        echo "正在配置 ufw 开放 $PORT 端口..."
        ufw allow ${PORT}/tcp 2>/dev/null || true
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

install_nginx
configure_port
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
