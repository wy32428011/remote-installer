#!/bin/bash
set -euo pipefail

LOG_FILE="mosquitto_install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

MQTT_PORT="${MQTT_PORT:-1883}"
USERNAME="${USERNAME:-}"
PASSWORD_FILE_INPUT="${PASSWORD_FILE:-}"
PASSWORD=""
PACKAGE_PATH="${PACKAGE_PATH:-}"
SERVICE_NAME="mosquitto"
CONFIG_DIR="/etc/mosquitto"
MAIN_CONFIG_FILE="/etc/mosquitto/mosquitto.conf"
REMOTE_CONFIG_FILE="/etc/mosquitto/conf.d/remote-installer.conf"
PASSWORD_FILE="/etc/mosquitto/passwd"

write_progress() {
    echo "PROGRESS:$1:$2"
}

fail() {
    echo "错误：$1"
    exit 1
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

validate_port() {
    local name="$1"
    local value="$2"

    if ! [[ "$value" =~ ^[0-9]+$ ]] || [ "$value" -lt 1 ] || [ "$value" -gt 65535 ]; then
        fail "$name 必须是 1-65535 之间的整数"
    fi
}

is_port_listening() {
    local port="$1"
    if command_exists ss; then
        ss -tln 2>/dev/null | grep -Eq "[\:\.]${port}[[:space:]]"
    elif command_exists netstat; then
        netstat -tln 2>/dev/null | grep -Eq "[\:\.]${port}[[:space:]]"
    else
        return 1
    fi
}

require_root() {
    if [ "$EUID" -ne 0 ]; then
        fail "请使用 root 权限运行此脚本"
    fi
}

require_package_path() {
    if [ -z "$PACKAGE_PATH" ]; then
        fail "Mosquitto 仅支持离线安装，必须显式提供 PACKAGE_PATH"
    fi

    if [ ! -e "$PACKAGE_PATH" ]; then
        fail "PACKAGE_PATH 不存在：$PACKAGE_PATH"
    fi
}

load_password() {
    if [ -n "$PASSWORD_FILE_INPUT" ]; then
        [ -f "$PASSWORD_FILE_INPUT" ] || fail "密码临时文件不存在：$PASSWORD_FILE_INPUT"
        PASSWORD="$(head -n 1 "$PASSWORD_FILE_INPUT" | tr -d '\r')"
        rm -f "$PASSWORD_FILE_INPUT"
    fi
}

validate_credentials() {
    local has_username=0
    local has_password=0

    if [[ "$USERNAME" =~ [^[:space:]] ]]; then
        has_username=1
    fi

    if [[ "$PASSWORD" =~ [^[:space:]] ]]; then
        has_password=1
    fi

    if [ "$has_username" -ne "$has_password" ]; then
        fail "Mosquitto 用户名和密码必须同时提供，或同时留空以启用匿名访问"
    fi
}

resolve_package_dir() {
    if [ -d "$PACKAGE_PATH" ]; then
        printf '%s\n' "$PACKAGE_PATH"
    else
        dirname "$PACKAGE_PATH"
    fi
}

normalize_arch() {
    local arch="${1:-}"
    arch="$(printf '%s' "$arch" | tr '[:upper:]' '[:lower:]')"

    case "$arch" in
        x86_64|x64|amd64)
            printf '%s\n' "amd64"
            ;;
        aarch64|arm64)
            printf '%s\n' "arm64"
            ;;
        *)
            printf '%s\n' ""
            ;;
    esac
}

detect_arch() {
    local detected
    detected="$(normalize_arch "$(uname -m 2>/dev/null || true)")"
    [ -n "$detected" ] || fail "无法识别目标主机 CPU 架构"
    printf '%s\n' "$detected"
}

package_matches_arch() {
    local package="$1"
    local target_arch="$2"
    local file_name
    file_name="$(basename "$package" | tr '[:upper:]' '[:lower:]')"

    case "$target_arch" in
        amd64)
            [[ "$file_name" == *"_amd64"* || "$file_name" == *"-amd64"* || "$file_name" == *"x86_64"* || "$file_name" == *"_x64"* || "$file_name" == *"-x64"* ]]
            ;;
        arm64)
            [[ "$file_name" == *"_arm64"* || "$file_name" == *"-arm64"* || "$file_name" == *"aarch64"* ]]
            ;;
        *)
            return 1
            ;;
    esac
}

deb_package_matches_name() {
    local package_file="$1"
    local package_name="$2"
    local file_name
    file_name="$(basename "$package_file" | tr '[:upper:]' '[:lower:]')"
    package_name="$(printf '%s' "$package_name" | tr '[:upper:]' '[:lower:]')"

    case "$file_name" in
        "${package_name}_"*.deb|"${package_name}.deb")
            return 0
            ;;
    esac

    [[ "$file_name" == "$package_name"-[0-9]*.deb ]]
}

has_deb_package() {
    local package_name="$1"
    local package_file

    for package_file in "${ROOT_DEBS[@]}"; do
        if deb_package_matches_name "$package_file" "$package_name"; then
            return 0
        fi
    done

    return 1
}

is_deb_package_installed() {
    local package_name="$1"
    local package_status

    command_exists dpkg-query || return 1
    package_status="$(dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null || true)"
    [ "$package_status" = "install ok installed" ]
}

print_ubuntu_package_status() {
    echo "Ubuntu Mosquitto 相关包状态："
    if command_exists dpkg-query; then
        dpkg-query -W -f='${db:Status-Abbrev} ${Package} ${Version}\n' \
            mosquitto mosquitto-clients libmosquitto1 libcjson1 libmicrohttpd12 libmicrohttpd12t64 2>/dev/null || true
    else
        echo "dpkg-query 不可用，无法输出包状态"
    fi
}

get_os_info() {
    local os_release="/etc/os-release"
    [ -f "$os_release" ] || fail "未找到 /etc/os-release，无法识别操作系统"

    . "$os_release"
    TARGET_ARCH="$(detect_arch)"

    case "${ID:-}" in
        ubuntu|debian)
            case "${VERSION_ID:-}" in
                22* ) PLATFORM="ubuntu22" ;;
                24* ) PLATFORM="ubuntu24" ;;
                * ) fail "当前 Ubuntu 版本暂不支持：${VERSION_ID:-未知}" ;;
            esac
            ;;
        centos|rocky|almalinux|rhel)
            case "${VERSION_ID:-}" in
                7* ) PLATFORM="centos7" ;;
                * ) fail "当前 CentOS/EL 版本暂不支持：${VERSION_ID:-未知}" ;;
            esac
            ;;
        *)
            fail "当前系统暂不支持：${ID:-未知}"
            ;;
    esac
}

collect_packages() {
    PACKAGE_DIR="$(resolve_package_dir)"
    ROOT_DEBS=()
    ROOT_RPMS=()

    while IFS= read -r package; do
        if package_matches_arch "$package" "$TARGET_ARCH"; then
            ROOT_DEBS+=("$package")
        fi
    done < <(find "$PACKAGE_DIR" -maxdepth 1 -type f -name '*.deb' | sort)

    while IFS= read -r package; do
        if package_matches_arch "$package" "$TARGET_ARCH"; then
            ROOT_RPMS+=("$package")
        fi
    done < <(find "$PACKAGE_DIR" -maxdepth 1 -type f -name '*.rpm' | sort)
}

validate_ubuntu_packages() {
    collect_packages
    local required_packages=(mosquitto libmosquitto1 libcjson1)
    local missing_packages=()
    local package_name
    local missing_text=""

    if [ ${#ROOT_DEBS[@]} -eq 0 ]; then
        fail "Ubuntu Mosquitto 离线目录中未找到任何 .deb 包"
    fi

    case "$PLATFORM" in
        ubuntu24)
            required_packages+=(libmicrohttpd12t64)
            ;;
        ubuntu22)
            required_packages+=(libmicrohttpd12)
            ;;
    esac

    if [ -n "$USERNAME" ] && ! command_exists mosquitto_passwd; then
        required_packages+=(mosquitto-clients)
    fi

    if ! has_deb_package "mosquitto"; then
        fail "Ubuntu Mosquitto 离线目录缺少与目标架构匹配的 mosquitto-*.deb / mosquitto_*.deb 主包"
    fi

    for package_name in "${required_packages[@]}"; do
        if ! has_deb_package "$package_name" && ! is_deb_package_installed "$package_name"; then
            missing_packages+=("$package_name")
        fi
    done

    if [ ${#missing_packages[@]} -gt 0 ]; then
        for package_name in "${missing_packages[@]}"; do
            if [ -n "$missing_text" ]; then
                missing_text+=", "
            fi
            missing_text+="$package_name"
        done

        fail "Ubuntu Mosquitto 离线目录缺少依赖包：$missing_text。请将这些 .deb 依赖放到 PACKAGE_PATH 指向的目录后重试"
    fi
}

validate_centos_packages() {
    collect_packages

    if [ ${#ROOT_RPMS[@]} -eq 0 ]; then
        fail "CentOS 7 Mosquitto 离线目录中未找到任何 .rpm 包"
    fi

    if ! printf '%s\n' "${ROOT_RPMS[@]}" | grep -Eq '/mosquitto-.*\.rpm$'; then
        fail "CentOS 7 Mosquitto 离线目录缺少与目标架构匹配的 mosquitto-*.rpm 主包"
    fi
}

install_ubuntu_packages() {
    write_progress "InstallingPackages" 25
    validate_ubuntu_packages
    echo "使用 Ubuntu Mosquitto 离线目录：$PACKAGE_DIR"

    if ! dpkg -i "${ROOT_DEBS[@]}"; then
        print_ubuntu_package_status
        fail "Ubuntu Mosquitto 离线安装失败，请检查离线目录中的依赖包是否完整且版本匹配"
    fi

    if ! is_deb_package_installed "mosquitto"; then
        print_ubuntu_package_status
        fail "Ubuntu 离线安装后未检测到 Mosquitto 包"
    fi
}

install_centos_packages() {
    write_progress "InstallingPackages" 25
    validate_centos_packages
    echo "使用 CentOS 7 Mosquitto 离线目录：$PACKAGE_DIR"

    yum --disablerepo='*' -y localinstall --setopt=tsflags=test "${ROOT_RPMS[@]}" >/dev/null
    yum --disablerepo='*' -y localinstall "${ROOT_RPMS[@]}"

    if ! rpm -qa 2>/dev/null | grep -Eq '^mosquitto(-|$)'; then
        fail "CentOS 7 离线安装后未检测到 Mosquitto 包"
    fi
}

ensure_main_config() {
    mkdir -p "$CONFIG_DIR" "$CONFIG_DIR/conf.d"

    if [ ! -f "$MAIN_CONFIG_FILE" ]; then
        cat > "$MAIN_CONFIG_FILE" <<'EOF'
pid_file /run/mosquitto/mosquitto.pid
persistence true
persistence_location /var/lib/mosquitto/
log_dest file /var/log/mosquitto/mosquitto.log
include_dir /etc/mosquitto/conf.d
EOF
    fi
}

configure_mosquitto() {
    write_progress "Configuring" 50
    ensure_main_config

    if [ -n "$USERNAME" ] && [ -n "$PASSWORD" ]; then
        command_exists mosquitto_passwd || fail "启用认证模式需要 mosquitto_passwd"
        mosquitto_passwd -b -c "$PASSWORD_FILE" "$USERNAME" "$PASSWORD"
        chmod 600 "$PASSWORD_FILE"
        cat > "$REMOTE_CONFIG_FILE" <<EOF
listener ${MQTT_PORT}
allow_anonymous false
password_file ${PASSWORD_FILE}
EOF
    else
        rm -f "$PASSWORD_FILE"
        cat > "$REMOTE_CONFIG_FILE" <<EOF
listener ${MQTT_PORT}
allow_anonymous true
EOF
    fi
}

start_service() {
    write_progress "StartingService" 75
    systemctl daemon-reload || true
    systemctl enable "$SERVICE_NAME" >/dev/null 2>&1 || true
    systemctl restart "$SERVICE_NAME"
}

verify_installation() {
    write_progress "Verifying" 90

    if ! systemctl is-active --quiet "$SERVICE_NAME"; then
        journalctl -u "$SERVICE_NAME" -n 50 --no-pager 2>/dev/null || true
        fail "Mosquitto 服务未处于运行状态"
    fi

    if ! is_port_listening "$MQTT_PORT"; then
        fail "MQTT 端口未监听：$MQTT_PORT"
    fi
}

get_version() {
    local version=""
    if command_exists mosquitto; then
        version="$(mosquitto -h 2>&1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
    fi

    if [ -z "$version" ]; then
        if is_deb_package_installed "mosquitto"; then
            version="$(dpkg-query -W -f='${Version}' mosquitto 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
        elif command_exists rpm && rpm -q mosquitto >/dev/null 2>&1; then
            version="$(rpm -q --queryformat '%{VERSION}' mosquitto 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1 || true)"
        fi
    fi

    printf '%s\n' "${version:-未知}"
}

write_progress "Initializing" 5
echo "Mosquitto Linux 离线安装开始..."
echo "PACKAGE_PATH=${PACKAGE_PATH:-<未指定>}"

require_root
require_package_path
load_password
validate_port "MQTT_PORT" "$MQTT_PORT"
validate_credentials
get_os_info

case "$PLATFORM" in
    ubuntu22|ubuntu24)
        install_ubuntu_packages
        ;;
    centos7)
        install_centos_packages
        ;;
esac

configure_mosquitto
start_service
verify_installation

VERSION="$(get_version)"

write_progress "Complete" 100
echo "INSTALLED:true"
echo "VERSION:${VERSION:-未知}"
echo "RUNNING:true"
echo "PORT:${MQTT_PORT}"
