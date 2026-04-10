#!/bin/bash
set -e

# 参数定义
KEEP_DATA=false
if [ "$1" == "--keep-data" ]; then
    KEEP_DATA=true
fi

# 日志设置
LOG_FILE="uninstall.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "MySQL 卸载脚本开始..."
echo "保留数据模式: $KEEP_DATA"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

echo "PROGRESS:Stopping:20"
# 1. 停止服务
echo "正在停止 MySQL 服务..."
systemctl stop mysql || systemctl stop mysqld || true
systemctl disable mysql || systemctl disable mysqld || true

# 确保进程已关闭
echo "检查残留进程..."
if pgrep mysql > /dev/null 2>&1; then
    echo "发现残留进程，正在强制停止..."
    pkill -9 mysql || true
    sleep 2
fi

echo "PROGRESS:Uninstalling:40"
# 2. 卸载软件包
if [ -f /etc/debian_version ]; then
    echo "检测到 Debian/Ubuntu 系统，正在彻底卸载 MySQL..."
    # 匹配所有可能与 MySQL/MariaDB 服务端和客户端相关的包
    MYSQL_PKGS=$(dpkg -l | grep -Ei 'mysql-server|mysql-client|mysql-common|mysql-community|mysql-apt-config|mariadb-server|mariadb-client' | awk '{print $2}' | tr '\n' ' ')
    if [ -n "$MYSQL_PKGS" ]; then
        echo "发现相关包: $MYSQL_PKGS"
        if [ "$KEEP_DATA" = true ]; then
            DEBIAN_FRONTEND=noninteractive apt-get remove -y $MYSQL_PKGS || true
        else
            DEBIAN_FRONTEND=noninteractive apt-get purge -y $MYSQL_PKGS || true
            apt-get autoremove -y || true
            apt-get autoclean || true
        fi
    else
        echo "未发现通过 apt 安装的 MySQL 相关包"
    fi
elif [ -f /etc/redhat-release ]; then
    echo "检测到 CentOS/RedHat 系统，正在彻底卸载 MySQL..."
    # 匹配所有 MySQL/MariaDB 相关包，包括社区版
    MYSQL_PKGS=$(rpm -qa | grep -Ei 'mysql-community|mysql-server|mysql-client|mysql-libs|mariadb-server|mariadb-client' | tr '\n' ' ')
    if [ -n "$MYSQL_PKGS" ]; then
        echo "发现相关包: $MYSQL_PKGS"
        yum remove -y $MYSQL_PKGS || rpm -e --nodeps $MYSQL_PKGS || true
        yum clean all || true
    else
        echo "未发现通过 rpm/yum 安装的 MySQL 相关包"
    fi
else
    echo "警告：不支持的操作系统，尝试直接清理文件"
fi

echo "PROGRESS:Cleaning:70"
# 3. 清理残留文件
if [ "$KEEP_DATA" = false ]; then
    echo "正在彻底清理配置、数据和日志目录..."
    
    # 核心路径列表
    REMAINING_PATHS=(
        "/etc/mysql"
        "/var/lib/mysql"
        "/var/lib/mysql-files"
        "/var/lib/mysql-keyring"
        "/var/log/mysql"
        "/var/run/mysqld"
        "/run/mysqld"
        "/usr/local/mysql"
        "/etc/my.cnf.d"
        "/usr/include/mysql"
        "/usr/lib/mysql"
        "/usr/lib64/mysql"
    )
    
    for path in "${REMAINING_PATHS[@]}"; do
        if [ -e "$path" ]; then
            echo "删除: $path"
            rm -rf "$path"
        fi
    done

    # 核心文件列表
    REMAINING_FILES=(
        "/etc/my.cnf"
        "/var/log/mysqld.log"
        "/etc/systemd/system/mysqld.service"
        "/etc/systemd/system/mysql.service"
        "/lib/systemd/system/mysqld.service"
        "/lib/systemd/system/mysql.service"
        "/tmp/mysql.sock"
        "/tmp/mysql.sock.lock"
        "/tmp/mysql.sock.bak"
    )

    for file in "${REMAINING_FILES[@]}"; do
        if [ -f "$file" ]; then
            echo "删除文件: $file"
            rm -f "$file"
        fi
    done

    # 清理软链接及二进制文件
    echo "清理二进制文件及软链接..."
    rm -f /usr/bin/mysql* /usr/sbin/mysqld /usr/bin/mysqldump /usr/bin/mysqladmin /usr/bin/mysqlcheck /usr/bin/mysqlshow /usr/bin/mysqlimport

    # 移除用户和组 (可选，有时建议保留，但为了完全卸载通常选择移除)
    if id "mysql" >/dev/null 2>&1; then
        echo "移除 mysql 用户和组..."
        userdel -r mysql 2>/dev/null || userdel mysql 2>/dev/null || true
        groupdel mysql 2>/dev/null || true
    fi
    
    # 清理安装时可能产生的临时解压目录
    rm -rf /tmp/mysql_extract_*

    echo "残留文件清理完成"
else
    echo "保留数据模式已开启，仅清理配置文件和二进制链接..."
    rm -rf /etc/mysql /etc/my.cnf /etc/my.cnf.d
    rm -f /usr/bin/mysql /usr/bin/mysqladmin /usr/bin/mysqld /usr/sbin/mysqld
    echo "已跳过数据目录 /var/lib/mysql 和日志清理"
fi

# 4. 刷新系统服务
echo "正在刷新系统服务..."
systemctl daemon-reload
systemctl reset-failed 2>/dev/null || true

echo "PROGRESS:Complete:100"

# 最终验证
echo ""
echo "最终验证..."

FAILED=0

# 验证进程（最多重试3次）
for retry in 1 2 3; do
    if ! pgrep mysql > /dev/null 2>&1; then
        break
    fi
    if [ $retry -lt 3 ]; then
        echo "发现 MySQL 进程残留，强制终止后重试 ($retry/3)..."
        pkill -9 mysql 2>/dev/null || true
        sleep 2
    fi
done

if pgrep mysql > /dev/null 2>&1; then
    echo "警告：仍有 MySQL 进程运行"
    FAILED=1
else
    echo "MySQL 进程：已停止"
fi

# 验证服务
if command -v systemctl &> /dev/null; then
    if systemctl is-active --quiet mysql 2>/dev/null || systemctl is-active --quiet mysqld 2>/dev/null; then
        echo "警告：MySQL 服务仍在运行"
        FAILED=1
    else
        echo "MySQL 服务：已停止"
    fi
fi

# 验证端口 3306
if command -v ss &> /dev/null; then
    if ss -tuln 2>/dev/null | grep -q ':3306[[:space:]]'; then
        echo "警告：端口 3306 仍在监听"
        FAILED=1
    else
        echo "端口 3306：已释放"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln 2>/dev/null | grep -q ':3306[[:space:]]'; then
        echo "警告：端口 3306 仍在监听"
        FAILED=1
    else
        echo "端口 3306：已释放"
    fi
fi

# 验证 mysql 命令
if command -v mysql &> /dev/null; then
    echo "警告：mysql 命令仍存在"
    FAILED=1
else
    echo "mysql 命令：已清理"
fi

echo "MySQL 卸载完成！"

# 输出机器可读的状态信息 (供 InstallerService 解析)
echo ""
echo "--- MACHINE READABLE ---"
if [ "$FAILED" = 0 ]; then
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "STAGE:SUCCESS"
else
    echo "INSTALLED: false"
    echo "RUNNING: false"
    echo "STAGE:PARTIAL"
fi
echo "------------------------"
