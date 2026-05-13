#!/bin/bash
set -e

KEEP_DATA=false
if [ "$1" == "--keep-data" ]; then
    KEEP_DATA=true
fi

LOG_FILE="uninstall.log"
exec > >(tee -a "$LOG_FILE") 2>&1

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

get_mysql_port() {
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
echo "MySQL 卸载脚本开始..."
echo "保留数据模式: $KEEP_DATA"

if [ "$(id -u)" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

MYSQL_PORT=$(get_mysql_port)
echo "检测到 MySQL 端口: $MYSQL_PORT"

echo "PROGRESS:Stopping:20"
echo "正在停止 MySQL 服务..."

if command -v systemctl >/dev/null 2>&1; then
    for svc in mysql mysqld mariadb; do
        if systemctl list-unit-files 2>/dev/null | grep -q "^${svc}\.service"; then
            systemctl stop "$svc" 2>/dev/null || true
            systemctl disable "$svc" 2>/dev/null || true
        fi
    done
fi

for proc in mysqld mysqld_safe mariadbd; do
    if pgrep -x "$proc" >/dev/null 2>&1; then
        pkill -15 -x "$proc" 2>/dev/null || true
    fi
done
sleep 2
for proc in mysqld mysqld_safe mariadbd; do
    if pgrep -x "$proc" >/dev/null 2>&1; then
        pkill -9 -x "$proc" 2>/dev/null || true
    fi
done

echo "PROGRESS:Uninstalling:40"
if [ -f /etc/debian_version ]; then
    echo "检测到 Debian/Ubuntu 系统，正在卸载 MySQL..."
    MYSQL_PKGS=$(dpkg -l 2>/dev/null | grep -Ei 'mysql-server|mysql-client|mysql-common|mysql-community|mysql-apt-config|libmysqlclient|mariadb-server|mariadb-client' | awk '{print $2}' | tr '\n' ' ')
    if [ -n "$MYSQL_PKGS" ]; then
        if [ "$KEEP_DATA" = true ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y $MYSQL_PKGS 2>/dev/null || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y $MYSQL_PKGS 2>/dev/null || true
        fi
    fi
elif [ -f /etc/redhat-release ]; then
    echo "检测到 CentOS/RedHat 系统，正在卸载 MySQL..."
    MYSQL_PKGS=$(rpm -qa 2>/dev/null | grep -Ei 'mysql-community|mysql-server|mysql-client|mysql-libs|mysql-common|mariadb-server|mariadb-client' | tr '\n' ' ')
    if [ -n "$MYSQL_PKGS" ]; then
        yum remove -y $MYSQL_PKGS 2>/dev/null || rpm -e --nodeps $MYSQL_PKGS 2>/dev/null || true
        yum clean all 2>/dev/null || true
    fi
fi

echo "PROGRESS:Cleaning:70"
if [ "$KEEP_DATA" = false ]; then
    echo "清理配置、数据和日志目录..."
    for path in \
        /etc/mysql \
        /etc/my.cnf.d \
        /var/lib/mysql \
        /var/lib/mysql-files \
        /var/lib/mysql-keyring \
        /var/log/mysql \
        /var/run/mysqld \
        /run/mysqld \
        /usr/local/mysql \
        /usr/include/mysql \
        /usr/lib/mysql \
        /usr/lib64/mysql; do
        if [ -e "$path" ]; then
            rm -rf "$path"
        fi
    done

    for file in \
        /etc/my.cnf \
        /var/log/mysqld.log \
        /tmp/mysql.sock \
        /tmp/mysql.sock.lock \
        /tmp/mysql.sock.bak; do
        if [ -e "$file" ]; then
            rm -rf "$file"
        fi
    done
else
    echo "保留数据模式已开启，仅清理配置和二进制链接..."
    rm -rf /etc/mysql /etc/my.cnf /etc/my.cnf.d 2>/dev/null || true
fi

for path in \
    /etc/systemd/system/mysqld.service \
    /etc/systemd/system/mysql.service \
    /etc/systemd/system/mariadb.service \
    /etc/systemd/system/mysqld.service.d \
    /etc/systemd/system/mysql.service.d \
    /etc/systemd/system/mariadb.service.d \
    /lib/systemd/system/mysqld.service \
    /lib/systemd/system/mysql.service \
    /lib/systemd/system/mariadb.service \
    /usr/lib/systemd/system/mysqld.service \
    /usr/lib/systemd/system/mysql.service \
    /usr/lib/systemd/system/mariadb.service; do
    if [ -e "$path" ]; then
        rm -rf "$path"
    fi
done

SYSTEMD_SERVICE_GLOBS=(
    "/etc/systemd/system/*.wants/mysql.service"
    "/etc/systemd/system/*.wants/mysqld.service"
    "/etc/systemd/system/*.wants/mariadb.service"
    "/run/systemd/generator*/mysql.service"
    "/run/systemd/generator*/mysqld.service"
    "/run/systemd/generator*/mariadb.service"
)

for pattern in "${SYSTEMD_SERVICE_GLOBS[@]}"; do
    for path in $pattern; do
        if [ -e "$path" ] || [ -L "$path" ]; then
            rm -f "$path"
        fi
    done
done

INIT_SCRIPTS=(
    "/etc/init.d/mysql"
    "/etc/init.d/mysqld"
    "/etc/init.d/mariadb"
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

rm -f /usr/bin/mysql* /usr/sbin/mysqld /usr/local/bin/mysql* /usr/local/mysql/bin/mysql* 2>/dev/null || true
rm -rf /tmp/mysql_extract_* 2>/dev/null || true

if [ "$KEEP_DATA" = false ]; then
    if id mysql >/dev/null 2>&1; then
        userdel -r mysql 2>/dev/null || userdel mysql 2>/dev/null || true
    fi
    if getent group mysql >/dev/null 2>&1; then
        groupdel mysql 2>/dev/null || true
    fi
fi

echo "PROGRESS:Finalizing:90"
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl reset-failed 2>/dev/null || true
fi

if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --remove-port=${MYSQL_PORT}/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
fi

if command -v ufw >/dev/null 2>&1; then
    ufw delete allow ${MYSQL_PORT}/tcp 2>/dev/null || true
fi

echo "PROGRESS:Complete:100"
echo "MySQL 卸载完成！"

echo ""
echo "最终验证..."
FAILED=0

for retry in 1 2 3; do
    if ! pgrep -x mysqld >/dev/null 2>&1 && ! pgrep -x mysqld_safe >/dev/null 2>&1 && ! pgrep -x mariadbd >/dev/null 2>&1; then
        break
    fi
    if [ $retry -lt 3 ]; then
        pkill -9 -x mysqld 2>/dev/null || true
        pkill -9 -x mysqld_safe 2>/dev/null || true
        pkill -9 -x mariadbd 2>/dev/null || true
        sleep 2
    fi
done

if pgrep -x mysqld >/dev/null 2>&1 || pgrep -x mysqld_safe >/dev/null 2>&1 || pgrep -x mariadbd >/dev/null 2>&1; then
    echo "警告：仍有 MySQL 进程运行"
    FAILED=1
else
    echo "MySQL 进程：已停止"
fi

if command -v systemctl >/dev/null 2>&1; then
    if systemctl is-active --quiet mysql 2>/dev/null || systemctl is-active --quiet mysqld 2>/dev/null || systemctl is-active --quiet mariadb 2>/dev/null; then
        echo "警告：MySQL 服务仍在运行"
        FAILED=1
    else
        echo "MySQL 服务：已停止"
    fi

    if systemctl list-unit-files 2>/dev/null | grep -qE '^(mysql|mysqld|mariadb)\.service'; then
        echo "警告：MySQL systemd 服务定义仍存在"
        FAILED=1
    else
        echo "MySQL systemd 服务定义：已清理"
    fi
fi

if is_port_listening "$MYSQL_PORT"; then
    echo "警告：端口 $MYSQL_PORT 仍在监听"
    FAILED=1
else
    echo "端口 $MYSQL_PORT：已释放"
fi

if command -v mysql >/dev/null 2>&1; then
    echo "警告：mysql 命令仍存在"
    FAILED=1
else
    echo "mysql 命令：已清理"
fi

if [ "$KEEP_DATA" = false ]; then
    for path in /etc/mysql /etc/my.cnf /var/lib/mysql /var/log/mysql /usr/local/mysql; do
        if [ -e "$path" ]; then
            echo "警告：残留路径仍存在：$path"
            FAILED=1
        fi
    done
fi

echo ""
echo "--- MACHINE READABLE ---"
if [ "$FAILED" = 0 ]; then
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "PORT: $MYSQL_PORT"
    echo "STAGE:SUCCESS"
else
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "PORT: $MYSQL_PORT"
    echo "STAGE:PARTIAL"
fi
echo "------------------------"
