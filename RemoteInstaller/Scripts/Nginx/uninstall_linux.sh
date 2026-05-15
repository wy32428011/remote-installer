#!/bin/bash
set -e

KEEP_DATA=false
if [ "$1" == "--keep-data" ] || [ "${KEEP_DATA:-false}" == "true" ]; then
    KEEP_DATA=true
fi

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

is_selinux_state_dir_trusted() {
    local state_dir=$1
    local owner_id
    local group_id
    local permissions

    if [ -L "$state_dir" ] || [ ! -d "$state_dir" ]; then
        echo "跳过不可信 SELinux 状态目录：$state_dir"
        return 1
    fi

    owner_id=$(stat -c '%u' "$state_dir" 2>/dev/null || echo "")
    group_id=$(stat -c '%g' "$state_dir" 2>/dev/null || echo "")
    permissions=$(stat -c '%a' "$state_dir" 2>/dev/null || echo "")
    if [ "$owner_id" != "0" ] || [ "$group_id" != "0" ] || [ "$permissions" != "700" ]; then
        echo "跳过权限不可信 SELinux 状态目录：$state_dir"
        return 1
    fi
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

remove_nginx_port_selinux_resources() {
    local state_dir="/var/lib/remote-installer/nginx"
    local state_file
    local port
    local expected_state_file
    local owner_id
    local permissions
    local module_name

    if [ ! -e "$state_dir" ]; then
        return 0
    fi

    is_selinux_state_dir_trusted "$state_dir" || return 0

    for state_file in "$state_dir"/selinux-port-*.state; do
        [ -f "$state_file" ] || continue
        [ ! -L "$state_file" ] || continue
        grep -Fxq "owner=remote-installer" "$state_file" || continue

        owner_id=$(stat -c '%u' "$state_file" 2>/dev/null || echo "")
        permissions=$(stat -c '%a' "$state_file" 2>/dev/null || echo "")
        if [ "$owner_id" != "0" ] || [ -z "$permissions" ] || [ $((8#$permissions & 022)) -ne 0 ]; then
            echo "跳过不可信 SELinux 状态文件：$state_file"
            continue
        fi

        port=$(grep -E '^port=' "$state_file" 2>/dev/null | head -n 1 | cut -d= -f2)
        if [[ ! "$port" =~ ^[0-9]+$ ]] || [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
            echo "跳过无效 SELinux 端口状态文件：$state_file"
            continue
        fi

        expected_state_file="$state_dir/selinux-port-${port}.state"
        if [ "$state_file" != "$expected_state_file" ]; then
            echo "跳过端口不一致的 SELinux 状态文件：$state_file"
            continue
        fi

        module_name=$(grep -E '^module=' "$state_file" 2>/dev/null | head -n 1 | cut -d= -f2)
        if [ -n "$module_name" ] && [ "$module_name" != "remote_installer_nginx_port_${port}" ]; then
            echo "跳过非本安装器命名空间的 SELinux 模块：$module_name"
            continue
        fi

        cleanup_succeeded=true

        if grep -Fxq "semanage=added" "$state_file"; then
            echo "提示：检测到安装器曾添加 SELinux http_port_t 端口 $port。为避免误删管理员接管的端口，卸载脚本不会自动删除，请确认后手动执行：semanage port -d -p tcp $port"
            cleanup_succeeded=false
        fi

        if grep -Fxq "module_created=true" "$state_file" && [ -n "$module_name" ]; then
            if command -v semodule >/dev/null 2>&1 && semodule -l 2>/dev/null | awk '{print $1}' | grep -qx "$module_name"; then
                if ! semodule -r "$module_name" 2>/dev/null; then
                    echo "警告：删除 SELinux 策略模块 $module_name 失败，保留状态文件：$state_file"
                    cleanup_succeeded=false
                fi
            fi
        fi

        if [ "$cleanup_succeeded" = "true" ]; then
            rm -f "$state_file"
        else
            echo "保留 SELinux 状态文件以便后续人工确认：$state_file"
        fi
    done
}

remove_nginx_firewall_resources() {
    local state_dir="/var/lib/remote-installer/nginx"
    local state_file
    local port
    local expected_state_file
    local owner_id
    local permissions
    local backend

    if [ ! -e "$state_dir" ]; then
        return 0
    fi

    is_selinux_state_dir_trusted "$state_dir" || return 0

    for state_file in "$state_dir"/firewall-port-*.state; do
        [ -f "$state_file" ] || continue
        [ ! -L "$state_file" ] || continue
        grep -Fxq "owner=remote-installer" "$state_file" || continue

        owner_id=$(stat -c '%u' "$state_file" 2>/dev/null || echo "")
        permissions=$(stat -c '%a' "$state_file" 2>/dev/null || echo "")
        if [ "$owner_id" != "0" ] || [ -z "$permissions" ] || [ $((8#$permissions & 022)) -ne 0 ]; then
            echo "跳过不可信 Nginx 防火墙状态文件：$state_file"
            continue
        fi

        port=$(grep -E '^port=' "$state_file" 2>/dev/null | head -n 1 | cut -d= -f2)
        if [[ ! "$port" =~ ^[0-9]+$ ]] || [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
            echo "跳过无效 Nginx 防火墙状态文件：$state_file"
            continue
        fi

        expected_state_file="$state_dir/firewall-port-${port}.state"
        if [ "$state_file" != "$expected_state_file" ]; then
            echo "跳过端口不一致的 Nginx 防火墙状态文件：$state_file"
            continue
        fi

        backend=$(grep -E '^backend=' "$state_file" 2>/dev/null | head -n 1 | cut -d= -f2)
        case "$backend" in
            firewalld)
                if command -v firewall-cmd >/dev/null 2>&1; then
                    firewall-cmd --permanent --remove-port=${port}/tcp 2>/dev/null || true
                    firewall-cmd --reload 2>/dev/null || true
                fi
                ;;
            ufw)
                if command -v ufw >/dev/null 2>&1; then
                    ufw delete allow ${port}/tcp 2>/dev/null || true
                fi
                ;;
            *)
                echo "跳过未知 Nginx 防火墙后端状态文件：$state_file"
                continue
                ;;
        esac

        rm -f "$state_file"
    done
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

get_listen_ports() {
    local ports

    ports=$(while IFS= read -r file; do
        grep -Eho 'listen[[:space:]]+([^;]|\[[^]]+\])+' "$file" 2>/dev/null || true
    done < <(get_config_candidates) | grep -Eo '([0-9]{2,5})' | sort -u)

    if [ -n "$ports" ]; then
        echo "$ports"
    else
        printf '80\n443\n'
    fi
}

get_port_process_output() {
    local port=$1
    local result=""

    if command -v ss >/dev/null 2>&1; then
        result=$(ss -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true)
    elif command -v netstat >/dev/null 2>&1; then
        result=$(netstat -tlnp 2>/dev/null | grep -E ":${port}[[:space:]]|:${port}\>" || true)
    fi

    printf '%s' "$result"
}

is_port_listening() {
    local port=$1
    [ -n "$(get_port_process_output "$port")" ]
}

is_port_listened_by_nginx() {
    local port=$1
    get_port_process_output "$port" | grep -qi 'nginx'
}

get_debian_nginx_packages() {
    dpkg -l 2>/dev/null | awk 'NR>5 && ($1 == "ii" || $1 == "rc" || $1 == "iU" || $1 == "iF") && ($2 ~ /^(nginx|libnginx-mod|nginx-mod)/) {print $1, $2}' | awk '{print $1, $2}'
}

get_redhat_nginx_packages() {
    rpm -qa 2>/dev/null | grep -Ei '^(nginx|libnginx-mod|nginx-mod)' || true
}

echo "PROGRESS:Initializing:5"
echo -e "${YELLOW}========================================${NC}"
echo -e "${YELLOW}      Nginx 完全卸载脚本${NC}"
echo -e "${YELLOW}========================================${NC}"
echo "保留数据模式：$KEEP_DATA"

if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}错误：请使用 root 权限运行此脚本${NC}"
    exit 1
fi

mapfile -t PORTS < <(get_listen_ports)
LISTEN_PORTS=$(printf '%s ' "${PORTS[@]}" | xargs)
PORT_OUTPUT=$(printf '%s,' "${PORTS[@]}" | sed 's/,$//')
echo "检测到 Nginx 监听端口：${PORT_OUTPUT:-80,443}"

echo "PROGRESS:Stopping:15"
echo -e "${YELLOW}1. 停止 Nginx 服务...${NC}"

if command -v systemctl >/dev/null 2>&1; then
    systemctl stop nginx 2>/dev/null || true
    systemctl disable nginx 2>/dev/null || true
fi

service nginx stop 2>/dev/null || true
/etc/init.d/nginx stop 2>/dev/null || true

if command -v nginx >/dev/null 2>&1; then
    nginx -s quit 2>/dev/null || nginx -s stop 2>/dev/null || true
    nginx -c /etc/nginx/nginx.conf -s quit 2>/dev/null || nginx -c /etc/nginx/nginx.conf -s stop 2>/dev/null || true
    nginx -c /usr/local/nginx/conf/nginx.conf -s quit 2>/dev/null || nginx -c /usr/local/nginx/conf/nginx.conf -s stop 2>/dev/null || true
fi

echo "检查并清理残留进程..."
for i in 1 2 3; do
    if pgrep -x nginx >/dev/null 2>&1; then
        echo "发现 Nginx 进程，尝试优雅关闭 (尝试 $i/3)..."
        pkill -15 -x nginx 2>/dev/null || true
        sleep 2
    else
        break
    fi
done

if pgrep -x nginx >/dev/null 2>&1; then
    echo "强制终止残留 Nginx 进程..."
    pkill -9 -x nginx 2>/dev/null || true
    sleep 1
fi

if pgrep -x nginx >/dev/null 2>&1; then
    echo -e "${YELLOW}警告：仍有 Nginx 进程无法终止${NC}"
else
    echo -e "${GREEN}所有 Nginx 进程已停止${NC}"
fi

echo "PROGRESS:Uninstalling:35"
echo -e "${YELLOW}2. 卸载软件包...${NC}"

OS_TYPE="unknown"
if [ -f /etc/debian_version ]; then
    OS_TYPE="debian"
    echo "检测到 Debian/Ubuntu 系统"

    ACTIVE_DEB_PACKAGES=$(get_debian_nginx_packages | awk '$1 != "rc" {print $2}' | cut -d: -f1 | tr '\n' ' ')
    RC_DEB_PACKAGES=$(get_debian_nginx_packages | awk '$1 == "rc" {print $2}' | cut -d: -f1 | tr '\n' ' ')

    if [ -n "$ACTIVE_DEB_PACKAGES" ]; then
        echo "检测到 Nginx 相关 DEB 包：$ACTIVE_DEB_PACKAGES"
        if [ "$KEEP_DATA" = true ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y -qq $ACTIVE_DEB_PACKAGES 2>/dev/null || dpkg -P $ACTIVE_DEB_PACKAGES 2>/dev/null || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y -qq $ACTIVE_DEB_PACKAGES 2>/dev/null || dpkg -P $ACTIVE_DEB_PACKAGES 2>/dev/null || true
        fi
    else
        echo "未检测到已安装的 Nginx DEB 包"
    fi

    if [ -n "$RC_DEB_PACKAGES" ]; then
        echo "清理残留 rc 包：$RC_DEB_PACKAGES"
        dpkg -P $RC_DEB_PACKAGES 2>/dev/null || true
    fi

    rm -rf /var/cache/apt/archives/nginx* /var/cache/apt/archives/libnginx-mod* 2>/dev/null || true
elif [ -f /etc/redhat-release ]; then
    OS_TYPE="redhat"
    echo "检测到 CentOS/RedHat 系统"

    RPM_PACKAGES=$(get_redhat_nginx_packages | tr '\n' ' ')
    if [ -n "$RPM_PACKAGES" ]; then
        echo "检测到 Nginx 相关 RPM 包：$RPM_PACKAGES"
        if command -v dnf >/dev/null 2>&1; then
            dnf remove -y $RPM_PACKAGES 2>/dev/null || true
            dnf clean all 2>/dev/null || true
        elif command -v yum >/dev/null 2>&1; then
            yum remove -y $RPM_PACKAGES 2>/dev/null || true
            yum clean all 2>/dev/null || true
        fi

        for pkg in $RPM_PACKAGES; do
            rpm -e --nodeps "$pkg" 2>/dev/null || true
        done
    else
        echo "未检测到已安装的 Nginx RPM 包"
    fi
fi

echo "PROGRESS:Cleaning:60"
echo -e "${YELLOW}3. 清理残留文件和目录...${NC}"

declare -a CONFIG_PATHS=(
    "/etc/nginx.conf"
    "/usr/local/nginx/conf/nginx.conf"
    "/opt/nginx/conf/nginx.conf"
)

declare -a LOG_PATHS=(
    "/var/log/nginx"
    "/var/log/nginx.log"
    "/usr/local/nginx/logs"
    "/opt/nginx/logs"
)

declare -a DATA_PATHS=(
    "/var/lib/nginx"
    "/var/cache/nginx"
    "/usr/local/nginx/client_body_temp"
)

declare -a BINARY_PATHS=(
    "/usr/sbin/nginx"
    "/usr/local/sbin/nginx"
    "/usr/bin/nginx"
    "/usr/local/bin/nginx"
    "/opt/nginx/sbin/nginx"
)

declare -a SERVICE_PATHS=(
    "/etc/systemd/system/nginx.service"
    "/etc/systemd/system/nginx.service.d"
    "/etc/systemd/system/nginx.service.wants"
    "/etc/systemd/system/multi-user.target.wants/nginx.service"
    "/etc/systemd/system/graphical.target.wants/nginx.service"
    "/lib/systemd/system/nginx.service"
    "/usr/lib/systemd/system/nginx.service"
    "/etc/init.d/nginx"
    "/etc/rc.d/init.d/nginx"
    "/etc/default/nginx"
    "/etc/sysconfig/nginx"
    "/etc/logrotate.d/nginx"
    "/etc/tmpfiles.d/nginx.conf"
)

declare -a SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/nginx.service"
    "/run/systemd/generator*/nginx.service"
)

declare -a INIT_SCRIPTS=(
    "/etc/init.d/nginx"
    "/etc/rc.d/init.d/nginx"
)

declare -a RUNTIME_PATHS=(
    "/var/run/nginx.pid"
    "/run/nginx.pid"
    "/var/run/nginx"
    "/run/nginx"
)

declare -a SSL_CERT_PATHS=(
    "/etc/nginx/ssl"
    "/etc/ssl/nginx"
)

echo "清理配置文件..."
for path in "${CONFIG_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -rf "$path"
    fi
done

if [ "$KEEP_DATA" = false ]; then
    echo "清理日志文件..."
    for path in "${LOG_PATHS[@]}"; do
        if [ -e "$path" ]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    done
fi

if [ "$KEEP_DATA" = false ]; then
    echo "清理数据目录..."
    for path in "${DATA_PATHS[@]}"; do
        if [ -e "$path" ]; then
            echo "  删除：$path"
            rm -rf "$path"
        fi
    done
fi

echo "清理二进制文件..."
for path in "${BINARY_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -f "$path"
    fi
done

echo "清理服务单元..."
for path in "${SERVICE_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -rf "$path"
    fi
done

for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for path in $pattern; do
        if [ -e "$path" ] || [ -L "$path" ]; then
            echo "  删除：$path"
            rm -f "$path"
        fi
    done
done

for init_script in "${INIT_SCRIPTS[@]}"; do
    service_name=$(basename "$init_script")
    if command -v update-rc.d >/dev/null 2>&1; then
        update-rc.d -f "$service_name" remove 2>/dev/null || true
    fi
    if command -v chkconfig >/dev/null 2>&1; then
        chkconfig --del "$service_name" 2>/dev/null || true
    fi
    if [ -e "$init_script" ] || [ -L "$init_script" ]; then
        echo "  删除：$init_script"
        rm -f "$init_script"
    fi
done

for rc_dir in /etc/rc*.d /etc/rc.d/rc*.d; do
    if [ -d "$rc_dir" ]; then
        rm -f "$rc_dir"/S*nginx* "$rc_dir"/K*nginx* 2>/dev/null || true
    fi
done

for path in "${RUNTIME_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo "  删除：$path"
        rm -rf "$path"
    fi
done

if [ "$KEEP_DATA" = false ]; then
    echo "默认保留可能包含业务配置或证书的 Nginx 目录：/etc/nginx、/usr/share/nginx、/usr/local/nginx/html、/opt/nginx、/etc/nginx/ssl、/etc/ssl/nginx"
fi

echo "清理系统用户..."
if id nginx >/dev/null 2>&1; then
    echo "  删除用户 nginx"
    userdel -r nginx 2>/dev/null || userdel nginx 2>/dev/null || true
fi
if getent group nginx >/dev/null 2>&1; then
    echo "  删除组 nginx"
    groupdel nginx 2>/dev/null || true
fi

remove_nginx_port_selinux_resources
remove_nginx_firewall_resources

echo "PROGRESS:Finalizing:90"
echo -e "${YELLOW}4. 刷新系统服务与防火墙...${NC}"
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload 2>/dev/null || true
    systemctl reset-failed nginx 2>/dev/null || true
    systemctl reset-failed 2>/dev/null || true
fi

echo "Nginx 防火墙规则只会按 /var/lib/remote-installer/nginx/firewall-port-*.state 中的安装器状态文件清理。"

echo "PROGRESS:Complete:100"
echo -e "${YELLOW}========================================${NC}"
echo -e "${GREEN}      Nginx 完全卸载完成！${NC}"
echo -e "${YELLOW}========================================${NC}"

echo ""
echo -e "${YELLOW}最终验证:${NC}"

FAILED=0

for retry in 1 2 3; do
    if ! pgrep -x nginx >/dev/null 2>&1; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 Nginx 进程残留，强制终止后重试 ($retry/3)..."
        pkill -9 -x nginx 2>/dev/null || true
        sleep 2
    fi
done

if pgrep -x nginx >/dev/null 2>&1; then
    echo -e "${RED}警告：仍有 Nginx 进程运行${NC}"
    FAILED=1
else
    echo -e "${GREEN}Nginx 进程：已停止${NC}"
fi

if command -v nginx >/dev/null 2>&1; then
    echo -e "${RED}警告：nginx 命令仍存在${NC}"
    FAILED=1
else
    echo -e "${GREEN}nginx 命令已清理${NC}"
fi

if command -v systemctl >/dev/null 2>&1; then
    if systemctl list-unit-files 2>/dev/null | grep -q '^nginx\.service'; then
        echo -e "${RED}警告：nginx systemd 服务定义仍存在${NC}"
        FAILED=1
    else
        echo -e "${GREEN}nginx systemd 服务定义已清理${NC}"
    fi
fi

for path in "${SERVICE_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo -e "${RED}警告：服务残留路径仍存在：$path${NC}"
        FAILED=1
    fi
done

for rc_dir in /etc/rc*.d /etc/rc.d/rc*.d; do
    if [ -d "$rc_dir" ] && ls "$rc_dir"/S*nginx* "$rc_dir"/K*nginx* >/dev/null 2>&1; then
        echo -e "${RED}警告：rc 启动链接仍存在：$rc_dir${NC}"
        FAILED=1
    fi
done

if [ "$OS_TYPE" = "debian" ]; then
    REMAINING_DEB_PACKAGES=$(get_debian_nginx_packages | awk '{print $2}' | cut -d: -f1 | tr '\n' ' ')
    if [ -n "$REMAINING_DEB_PACKAGES" ]; then
        echo -e "${RED}警告：仍有 Nginx 相关 DEB 包残留：$REMAINING_DEB_PACKAGES${NC}"
        FAILED=1
    else
        echo -e "${GREEN}Nginx DEB 包：已清理${NC}"
    fi
elif [ "$OS_TYPE" = "redhat" ]; then
    REMAINING_RPM_PACKAGES=$(get_redhat_nginx_packages | tr '\n' ' ')
    if [ -n "$REMAINING_RPM_PACKAGES" ]; then
        echo -e "${RED}警告：仍有 Nginx 相关 RPM 包残留：$REMAINING_RPM_PACKAGES${NC}"
        FAILED=1
    else
        echo -e "${GREEN}Nginx RPM 包：已清理${NC}"
    fi
fi

VALIDATE_PATHS=(
    "/etc/nginx.conf"
    "/usr/local/nginx/conf/nginx.conf"
    "/opt/nginx/conf/nginx.conf"
)

if [ "$KEEP_DATA" = false ]; then
    VALIDATE_PATHS+=(
        "/var/log/nginx"
        "/var/lib/nginx"
        "/var/cache/nginx"
    )
fi

for path in "${VALIDATE_PATHS[@]}"; do
    if [ -e "$path" ]; then
        echo -e "${RED}警告：残留路径仍存在：$path${NC}"
        FAILED=1
    fi
done

if [ "$FAILED" = 0 ]; then
    echo -e "${GREEN}关键目录与服务残留：已清理${NC}"
fi

for port in "${PORTS[@]}"; do
    if is_port_listening "$port"; then
        PORT_PROCESS_OUTPUT=$(get_port_process_output "$port")
        if echo "$PORT_PROCESS_OUTPUT" | grep -qi 'nginx'; then
            echo -e "${RED}警告：端口 $port 仍由 Nginx 监听${NC}"
            echo "$PORT_PROCESS_OUTPUT"
            FAILED=1
        else
            echo -e "${YELLOW}提示：端口 $port 仍在监听，但占用者不是 Nginx，不判定为卸载失败${NC}"
            [ -n "$PORT_PROCESS_OUTPUT" ] && echo "$PORT_PROCESS_OUTPUT"
        fi
    else
        echo -e "${GREEN}端口 $port：已释放${NC}"
    fi
done

echo ""
echo "--- MACHINE READABLE ---"
echo "INSTALLED: false"
echo "RUNNING: false"
if [ "$FAILED" = 0 ]; then
    echo "STAGE:SUCCESS"
else
    echo "STAGE:PARTIAL"
fi
echo "PORT: ${PORT_OUTPUT:-80,443}"
echo "------------------------"
