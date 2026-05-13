#!/bin/bash
set -e

KEEP_DATA=false
if [ "${1:-}" == "--keep-data" ]; then
    KEEP_DATA=true
fi

LOG_FILE="uninstall.log"
exec > >(tee -a "$LOG_FILE") 2>&1

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

get_mariadb_port() {
    local file
    local port

    while IFS= read -r file; do
        port=$(grep -E '^[[:space:]]*port([[:space:]]*=|[[:space:]]+)[0-9]+' "$file" 2>/dev/null | head -n 1 | grep -oE '[0-9]+' | head -n 1)
        if [ -n "$port" ]; then
            echo "$port"
            return 0
        fi
    done < <(get_config_candidates)

    echo "3306"
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

echo "PROGRESS:Initializing:5"
echo "MariaDB 卸载脚本开始..."
echo "保留数据模式: $KEEP_DATA"

if [ "$(id -u)" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

MARIADB_PORT=$(get_mariadb_port)

echo "PROGRESS:Stopping:20"
if command -v systemctl >/dev/null 2>&1; then
    for svc in mariadb mysql mysqld; do
        if systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\.service"; then
            systemctl stop "$svc" 2>/dev/null || true
            systemctl disable "$svc" 2>/dev/null || true
        fi
    done
fi

for proc in mariadbd mysqld mysqld_safe; do
    if pgrep -x "$proc" >/dev/null 2>&1; then
        pkill -15 -x "$proc" 2>/dev/null || true
    fi
done
sleep 2
for proc in mariadbd mysqld mysqld_safe; do
    if pgrep -x "$proc" >/dev/null 2>&1; then
        pkill -9 -x "$proc" 2>/dev/null || true
    fi
done

echo "PROGRESS:Uninstalling:40"
if [ -f /etc/debian_version ]; then
    MARIADB_PKGS=$(dpkg -l 2>/dev/null | grep -Ei 'mariadb-server|mariadb-client|mariadb-common|galera' | awk '{print $2}' | tr '\n' ' ')
    if [ -n "$MARIADB_PKGS" ]; then
        if [ "$KEEP_DATA" = true ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y $MARIADB_PKGS 2>/dev/null || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y $MARIADB_PKGS 2>/dev/null || true
        fi
    fi
elif [ -f /etc/redhat-release ]; then
    MARIADB_PKGS=$(rpm -qa 2>/dev/null | grep -Ei 'MariaDB|mariadb|galera' | tr '\n' ' ')
    if [ -n "$MARIADB_PKGS" ]; then
        yum remove -y $MARIADB_PKGS 2>/dev/null || rpm -e --nodeps $MARIADB_PKGS 2>/dev/null || true
        yum clean all 2>/dev/null || true
    fi
fi

echo "PROGRESS:Cleaning:70"
if [ "$KEEP_DATA" = false ]; then
    for path in \
        /etc/mysql \
        /etc/my.cnf.d \
        /var/lib/mysql \
        /var/lib/mysql-files \
        /var/log/mysql \
        /var/run/mysqld \
        /run/mysqld; do
        if [ -e "$path" ]; then
            rm -rf "$path"
        fi
    done

    for file in /etc/my.cnf /var/log/mysqld.log; do
        if [ -e "$file" ]; then
            rm -rf "$file"
        fi
    done
else
    rm -rf /etc/mysql /etc/my.cnf /etc/my.cnf.d 2>/dev/null || true
fi

for path in \
    /etc/systemd/system/mariadb.service \
    /etc/systemd/system/mysql.service \
    /etc/systemd/system/mysqld.service \
    /etc/systemd/system/mariadb.service.d \
    /etc/systemd/system/mysql.service.d \
    /etc/systemd/system/mysqld.service.d \
    /lib/systemd/system/mariadb.service \
    /lib/systemd/system/mysql.service \
    /lib/systemd/system/mysqld.service \
    /usr/lib/systemd/system/mariadb.service \
    /usr/lib/systemd/system/mysql.service \
    /usr/lib/systemd/system/mysqld.service; do
    if [ -e "$path" ]; then
        rm -rf "$path"
    fi
done

SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/mariadb.service"
    "/etc/systemd/system/*.wants/mysql.service"
    "/etc/systemd/system/*.wants/mysqld.service"
    "/run/systemd/generator*/mariadb.service"
    "/run/systemd/generator*/mysql.service"
    "/run/systemd/generator*/mysqld.service"
)

for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for path in $pattern; do
        if [ -e "$path" ] || [ -L "$path" ]; then
            rm -f "$path"
        fi
    done
done

INIT_SCRIPTS=(
    "/etc/init.d/mariadb"
    "/etc/init.d/mysql"
    "/etc/init.d/mysqld"
)

for init_script in "${INIT_SCRIPTS[@]}"; do
    service_name=$(basename "$init_script")
    if command -v update-rc.d >/dev/null 2>&1; then
        update-rc.d -f "$service_name" remove 2>/dev/null || true
    fi
    if command -v chkconfig >/dev/null 2>&1; then
        chkconfig --del "$service_name" 2>/dev/null || true
    fi
    rm -f "$init_script" 2>/dev/null || true
done

rm -f /usr/bin/mariadb* /usr/sbin/mariadbd /usr/local/bin/mariadb* 2>/dev/null || true

echo "PROGRESS:Finalizing:90"
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl reset-failed 2>/dev/null || true
fi

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --remove-port=${MARIADB_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

echo "PROGRESS:Complete:100"
echo "MariaDB 卸载完成！"

echo ""
echo "--- MACHINE READABLE ---"
if pgrep -x mariadbd >/dev/null 2>&1 || systemctl is-active --quiet mariadb 2>/dev/null || is_port_listening "$MARIADB_PORT"; then
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "PORT: $MARIADB_PORT"
    echo "STAGE:PARTIAL"
else
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "PORT: $MARIADB_PORT"
    echo "STAGE:SUCCESS"
fi
echo "------------------------"
