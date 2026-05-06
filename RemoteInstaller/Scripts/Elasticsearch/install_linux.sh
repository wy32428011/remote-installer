#!/bin/bash

# 参数定义
# PACKAGE_PATH: 远程安装包路径（文件或目录）
# HTTP_PORT: 端口号 (默认 9200)
# CLUSTER_NAME: 集群名称 (默认 my-cluster)
# NODE_NAME: 节点名称 (默认 node-1)
# MEMORY_LIMIT: 内存大小 (默认 2g)

ELASTICSEARCH_OFFLINE_VERSION="8.5.3"

# 日志设置
LOG_FILE="install.log"
exec > >(tee -a "$LOG_FILE") 2>&1

echo "PROGRESS:Initializing:5"
echo "========================================"
echo "      Elasticsearch 安装脚本"
echo "========================================"
echo "当前工作目录: $(pwd)"
echo "日志文件: $(pwd)/$LOG_FILE"

# 0. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

# 检查参数
PACKAGE_IS_FILE=false
PACKAGE_IS_DIRECTORY=false

fail() {
    echo "错误：$1"
    exit 1
}

if [ -z "$PACKAGE_PATH" ]; then
    fail "Elasticsearch Linux 安装仅支持显式提供 PACKAGE_PATH，本脚本不会自动猜测当前目录中的安装包。请通过 PACKAGE_PATH 提供离线目录、.deb/.rpm 主包所在目录或 .tar 归档文件。"
fi

if [ -f "$PACKAGE_PATH" ]; then
    PACKAGE_IS_FILE=true
elif [ -d "$PACKAGE_PATH" ]; then
    PACKAGE_IS_DIRECTORY=true
else
    fail "PACKAGE_PATH 不存在：$PACKAGE_PATH"
fi

resolve_debian_main_package() {
    local package_root=$1
    find "$package_root" -maxdepth 1 -type f -name "elasticsearch-${ELASTICSEARCH_OFFLINE_VERSION}*.deb" | sort | head -n 1
}

resolve_redhat_main_package() {
    local package_root=$1
    find "$package_root" -maxdepth 1 -type f -name "elasticsearch-${ELASTICSEARCH_OFFLINE_VERSION}*.rpm" | sort | head -n 1
}

resolve_debian_dependency_dir() {
    local package_root=$1

    if [ -d "$package_root/deps" ]; then
        echo "$package_root/deps"
        return 0
    fi

    echo "$package_root"
}

get_redhat_offline_package_directories() {
    local package_root=$1

    if [ -d "$package_root/deps" ]; then
        echo "$package_root/deps"
    fi

    echo "$package_root"
}

get_debian_offline_dependency_packages() {
    if [ "$OS_TYPE" != "debian" ]; then
        return 0
    fi

    cat <<EOF
bash
lsb-base
libc6
adduser
coreutils
EOF
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
        echo "错误：离线目录缺少以下 Elasticsearch Debian 依赖包："
        printf '%s\n' "${missing_packages[@]}"
        echo "请将对应 .deb 文件放入目录：$dependency_dir"
        exit 1
    fi

    if [ ${#install_files[@]} -eq 0 ]; then
        echo "Elasticsearch 离线依赖已满足，无需额外安装。"
        return 0
    fi

    echo "正在安装缺失的 Elasticsearch Debian 离线依赖包..."
    DPKG_OPTS="--force-confdef --force-confold"
    DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "${install_files[@]}"
    DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a
}

collect_redhat_rpm_files() {
    local package_root=$1
    local package_dir
    local rpm_file

    while IFS= read -r package_dir; do
        for rpm_file in "$package_dir"/*.rpm; do
            if [ -f "$rpm_file" ]; then
                echo "$rpm_file"
            fi
        done
    done < <(get_redhat_offline_package_directories "$package_root")
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
    local output_file=$1
    shift
    local rpm_file
    local provides_output

    : > "$output_file"

    for rpm_file in "$@"; do
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

run_redhat_localinstall_test() {
    if command -v dnf >/dev/null 2>&1; then
        dnf --disablerepo='*' --setopt=tsflags=test localinstall -y "$@"
    else
        yum --disablerepo='*' --setopt=tsflags=test localinstall -y "$@"
    fi
}

run_redhat_localinstall() {
    if command -v dnf >/dev/null 2>&1; then
        dnf --disablerepo='*' localinstall -y "$@"
    else
        yum --disablerepo='*' localinstall -y "$@"
    fi
}

validate_redhat_offline_transaction() {
    local output_file
    output_file=$(mktemp)

    if ! run_redhat_localinstall_test "$@" > "$output_file" 2>&1; then
        echo "错误：严格离线模式事务预检失败，请检查以下输出："
        cat "$output_file"
        rm -f "$output_file"
        exit 1
    fi

    rm -f "$output_file"
}

validate_redhat_offline_dependencies() {
    local package_root=$1
    local provides_file
    local missing_file
    local requirements_output
    local requirement
    local normalized_requirement
    local rpm_file
    local -a rpm_files=()

    mapfile -t rpm_files < <(collect_redhat_rpm_files "$package_root")

    if [ ${#rpm_files[@]} -eq 0 ]; then
        fail "离线目录中未找到 .rpm 文件：$package_root"
    fi

    provides_file=$(mktemp)
    missing_file=$(mktemp)

    collect_local_rpm_capabilities "$provides_file" "${rpm_files[@]}"

    for rpm_file in "${rpm_files[@]}"; do
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
        echo "请将满足以上能力的 EL7 RPM 一并放入目录：$package_root"
        rm -f "$provides_file" "$missing_file"
        exit 1
    fi

    rm -f "$provides_file" "$missing_file"
}

HTTP_PORT=${HTTP_PORT:-9200}
CLUSTER_NAME=${CLUSTER_NAME:-my-cluster}
NODE_NAME=${NODE_NAME:-node-1}
MEMORY_LIMIT=${MEMORY_LIMIT:-2g}

# 1. 优化系统参数
echo "PROGRESS:ConfiguringKernel:10"
echo "配置系统内核参数..."

# Elasticsearch 8.x 要求 vm.max_map_count 至少 1048576
sysctl -w vm.max_map_count=1048576 2>/dev/null || true
if ! grep -q "vm.max_map_count" /etc/sysctl.conf 2>/dev/null; then
    echo "vm.max_map_count=1048576" >> /etc/sysctl.conf 2>/dev/null || true
fi

# 降低 TCP 重传超时 (Elasticsearch 推荐)
sysctl -w net.ipv4.tcp_retries2=5 2>/dev/null || true

# 提高文件描述符限制 (Elasticsearch 要求至少 65535)
if [ -f /etc/security/limits.conf ]; then
    if ! grep -q "elasticsearch.*nofile" /etc/security/limits.conf 2>/dev/null; then
        echo "elasticsearch soft nofile 65535" >> /etc/security/limits.conf 2>/dev/null || true
        echo "elasticsearch hard nofile 65535" >> /etc/security/limits.conf 2>/dev/null || true
    fi
    # 添加线程数限制
    if ! grep -q "elasticsearch.*nproc" /etc/security/limits.conf 2>/dev/null; then
        echo "elasticsearch soft nproc 4096" >> /etc/security/limits.conf 2>/dev/null || true
        echo "elasticsearch hard nproc 4096" >> /etc/security/limits.conf 2>/dev/null || true
    fi
fi

# 2. 检测操作系统
echo "PROGRESS:DetectingOS:12"
if [ -f /etc/debian_version ]; then
    if command -v apt-get &> /dev/null; then
        OS_TYPE="debian"
        PKG_MANAGER="apt-get"
    else
        OS_TYPE="debian"
        PKG_MANAGER="dpkg"
    fi
elif [ -f /etc/redhat-release ]; then
    if command -v dnf &> /dev/null; then
        OS_TYPE="rhel"
        PKG_MANAGER="dnf"
    else
        OS_TYPE="rhel"
        PKG_MANAGER="yum"
    fi
elif [ -f /etc/systemd/system ]; then
    # 尝试检测
    if command -v yum &> /dev/null; then
        OS_TYPE="rhel"
        PKG_MANAGER="yum"
    elif command -v apt-get &> /dev/null; then
        OS_TYPE="debian"
        PKG_MANAGER="apt-get"
    fi
fi

if [ -z "$OS_TYPE" ]; then
    echo "错误：无法确定操作系统类型"
    exit 1
fi

echo "检测到操作系统类型: $OS_TYPE (使用 $PKG_MANAGER)"

# 3. 创建安装目录和用户
echo "PROGRESS:CreatingUsers:15"
ES_INSTALL_DIR="/opt/elasticsearch"
ES_HOME="/opt/elasticsearch"
ES_DATA_DIR="/var/lib/elasticsearch"
ES_LOG_DIR="/var/log/elasticsearch"
ES_CONF_DIR="/etc/elasticsearch"
ES_RUN_DIR="/var/run/elasticsearch"

echo "创建安装目录..."
mkdir -p "$ES_INSTALL_DIR" 2>/dev/null || true
mkdir -p "$ES_DATA_DIR" 2>/dev/null || true
mkdir -p "$ES_LOG_DIR" 2>/dev/null || true
mkdir -p "$ES_CONF_DIR" 2>/dev/null || true
mkdir -p "$ES_RUN_DIR" 2>/dev/null || true

# 创建 elasticsearch 用户
if ! id -u elasticsearch &>/dev/null; then
    echo "创建 elasticsearch 用户..."
    useradd -r -s /bin/false -d "$ES_INSTALL_DIR" elasticsearch 2>/dev/null || useradd -r -s /sbin/nologin elasticsearch 2>/dev/null || true
fi

echo "设置目录权限..."
chown -R elasticsearch:elasticsearch "$ES_DATA_DIR" 2>/dev/null || true
chown -R elasticsearch:elasticsearch "$ES_LOG_DIR" 2>/dev/null || true
chown -R elasticsearch:elasticsearch "$ES_RUN_DIR" 2>/dev/null || true

# 4. 安装
echo "PROGRESS:Installing:20"
INSTALL_TYPE=""

if [ "$PACKAGE_IS_FILE" = true ] && ([[ "$PACKAGE_PATH" == *.tar.gz ]] || [[ "$PACKAGE_PATH" == *.tar ]]); then
    # tar.gz 或 tar 包 - 手动解压安装
    echo "检测到压缩包格式，开始解压安装..."
    INSTALL_TYPE="tar"

    if [[ "$PACKAGE_PATH" == *.tar.gz ]]; then
        tar -xzf "$PACKAGE_PATH" -C "$ES_INSTALL_DIR" --strip-components=1 2>/dev/null || tar -xzf "$PACKAGE_PATH" -C /tmp
    else
        tar -xf "$PACKAGE_PATH" -C "$ES_INSTALL_DIR" --strip-components=1 2>/dev/null || tar -xf "$PACKAGE_PATH" -C /tmp
    fi

    # 如果解压失败，尝试另一种方式
    if [ ! -f "$ES_INSTALL_DIR/bin/elasticsearch" ]; then
        echo "尝试备选解压方式..."
        TEMP_DIR="/tmp/es_extract_$$"
        mkdir -p "$TEMP_DIR"
        tar -xzf "$PACKAGE_PATH" -C "$TEMP_DIR" 2>/dev/null || tar -xf "$PACKAGE_PATH" -C "$TEMP_DIR" 2>/dev/null
        # 查找解压出的 elasticsearch 目录
        ES_FOUND=$(find "$TEMP_DIR" -maxdepth 2 -type d -name "elasticsearch" | head -n 1)
        if [ -n "$ES_FOUND" ] && [ -d "$ES_FOUND" ]; then
            cp -r "$ES_FOUND"/* "$ES_INSTALL_DIR"/
        elif [ -d "$TEMP_DIR/elasticsearch" ]; then
            cp -r "$TEMP_DIR/elasticsearch"/* "$ES_INSTALL_DIR"/
        else
            # 直接复制所有内容
            cp -r "$TEMP_DIR"/* "$ES_INSTALL_DIR"/
        fi
        rm -rf "$TEMP_DIR"
    fi

    # 设置权限
    chown -R elasticsearch:elasticsearch "$ES_INSTALL_DIR"
    echo "压缩包解压完成"

    CONFIG_FILE="$ES_INSTALL_DIR/config/elasticsearch.yml"
    JVM_OPTIONS_FILE="$ES_INSTALL_DIR/config/jvm.options"
    ES_BIN="$ES_INSTALL_DIR/bin/elasticsearch"

elif [ "$OS_TYPE" = "debian" ] && { [ "$PACKAGE_IS_DIRECTORY" = true ] || { [ "$PACKAGE_IS_FILE" = true ] && [[ "$PACKAGE_PATH" == *.deb ]]; }; }; then
    echo "检测到 DEB 离线资源目录..."
    INSTALL_TYPE="deb"

    PACKAGE_ROOT="$PACKAGE_PATH"
    if [ "$PACKAGE_IS_FILE" = true ]; then
        PACKAGE_ROOT=$(dirname "$PACKAGE_PATH")
    fi

    DEB_MAIN_PACKAGE=$(resolve_debian_main_package "$PACKAGE_ROOT")
    if [ -z "$DEB_MAIN_PACKAGE" ] || [ ! -f "$DEB_MAIN_PACKAGE" ]; then
        fail "离线目录中未找到 elasticsearch-${ELASTICSEARCH_OFFLINE_VERSION}*.deb 主包：$PACKAGE_ROOT"
    fi

    prepare_debian_offline_dependencies "$PACKAGE_ROOT"

    for i in {1..10}; do
        if fuser /var/lib/dpkg/lock-frontend >/dev/null 2>&1 || fuser /var/lib/dpkg/lock >/dev/null 2>&1; then
            echo "等待其他包管理进程结束... ($i/10)"
            sleep 5
        else
            break
        fi
    done

    echo "再安装 Elasticsearch 主包：$DEB_MAIN_PACKAGE"
    DPKG_OPTS="--force-confdef --force-confold"
    DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS -i "$DEB_MAIN_PACKAGE"
    DEBIAN_FRONTEND=noninteractive dpkg $DPKG_OPTS --configure -a
    sync

    if ! dpkg -l | grep -q "^ii[[:space:]]\+elasticsearch[[:space:]]"; then
        fail "Elasticsearch 离线 DEB 安装未完成：未检测到 elasticsearch 已安装"
    fi

    CONFIG_FILE="/etc/elasticsearch/elasticsearch.yml"
    JVM_OPTIONS_FILE="/etc/elasticsearch/jvm.options"
    ES_BIN="/usr/share/elasticsearch/bin/elasticsearch"

    if [ -f "$CONFIG_FILE" ]; then
        chown root:elasticsearch "$CONFIG_FILE" 2>/dev/null || true
        chmod 660 "$CONFIG_FILE" 2>/dev/null || true
    fi

elif [ "$OS_TYPE" = "rhel" ] && { [ "$PACKAGE_IS_DIRECTORY" = true ] || { [ "$PACKAGE_IS_FILE" = true ] && [[ "$PACKAGE_PATH" == *.rpm ]]; }; }; then
    echo "检测到 RPM 离线资源目录..."
    INSTALL_TYPE="rpm"

    PACKAGE_ROOT="$PACKAGE_PATH"
    if [ "$PACKAGE_IS_FILE" = true ]; then
        PACKAGE_ROOT=$(dirname "$PACKAGE_PATH")
    fi

    RPM_MAIN_PACKAGE=$(resolve_redhat_main_package "$PACKAGE_ROOT")
    if [ -z "$RPM_MAIN_PACKAGE" ] || [ ! -f "$RPM_MAIN_PACKAGE" ]; then
        fail "离线目录中未找到 elasticsearch-${ELASTICSEARCH_OFFLINE_VERSION}*.rpm 主包：$PACKAGE_ROOT"
    fi

    mapfile -t RPM_FILES < <(collect_redhat_rpm_files "$PACKAGE_ROOT")
    if [ ${#RPM_FILES[@]} -eq 0 ]; then
        fail "离线目录中未找到 .rpm 文件：$PACKAGE_ROOT"
    fi

    echo "检测到 EL7/CentOS7 离线目录，启用严格离线模式..."
    echo "正在校验离线 RPM 依赖能力..."
    validate_redhat_offline_dependencies "$PACKAGE_ROOT"
    echo "正在执行离线事务预检..."
    validate_redhat_offline_transaction "${RPM_FILES[@]}"

    echo "当前 localinstall 已禁用全部外部 repo。"
    run_redhat_localinstall "${RPM_FILES[@]}"
    sync

    if ! rpm -q elasticsearch >/dev/null 2>&1; then
        fail "Elasticsearch 离线 RPM 安装未完成：未检测到 elasticsearch 已安装"
    fi

    CONFIG_FILE="/etc/elasticsearch/elasticsearch.yml"
    JVM_OPTIONS_FILE="/etc/elasticsearch/jvm.options"
    ES_BIN="/usr/share/elasticsearch/bin/elasticsearch"

else
    echo "错误：不支持的安装包格式: $PACKAGE_PATH"
    echo "支持格式: 目录型 .deb/.rpm 离线资源，或单文件 .tar.gz/.tar"
    exit 1
fi

# 5. 等待并确认配置文件
echo "PROGRESS:Configuring:40"
echo "等待配置文件..."

WAIT_COUNT=0
while [ ! -f "$CONFIG_FILE" ] && [ $WAIT_COUNT -lt 10 ]; do
    echo "等待配置文件... ($((WAIT_COUNT+1))/10)"
    sleep 2
    WAIT_COUNT=$((WAIT_COUNT+1))
done

if [ ! -f "$CONFIG_FILE" ]; then
    if [ "$INSTALL_TYPE" = "tar" ]; then
        echo "警告：配置文件不存在，尝试创建..."
        mkdir -p "$(dirname "$CONFIG_FILE")"
        touch "$CONFIG_FILE"
    else
        echo "错误：安装完成后未找到配置文件：$CONFIG_FILE"
        exit 1
    fi
fi

# 备份原配置
if [ -f "$CONFIG_FILE" ]; then
    cp "$CONFIG_FILE" "${CONFIG_FILE}.bak"
fi

# 辅助函数：删除配置项（含注释/空白/冒号前空格/单双引号键）
remove_config_key() {
    local key="$1"
    local escaped_key
    escaped_key=$(echo "$key" | sed 's/\./\\./g')
    sed -i -E "/^[[:space:]]*#?[[:space:]]*(${escaped_key}|\"${escaped_key}\"|'${escaped_key}')[[:space:]]*:/d" "$CONFIG_FILE" 2>/dev/null || true
}

# 辅助函数：删除块配置（例如 key: 后跟多行 - item）
remove_config_block() {
    local key="$1"
    local escaped_key
    escaped_key=$(echo "$key" | sed 's/\./\\./g')

    awk -v k="$escaped_key" '
    BEGIN { skip=0 }
    {
        if ($0 ~ "^[[:space:]]*#?[[:space:]]*(" k "|\"" k "\"|\x27" k "\x27)[[:space:]]*:") { skip=1; next }
        if (skip==1) {
            if ($0 ~ "^[[:space:]]*-[[:space:]]" || $0 ~ "^[[:space:]]+") { next }
            skip=0
        }
        print
    }
    ' "$CONFIG_FILE" > "${CONFIG_FILE}.tmp" 2>/dev/null && mv "${CONFIG_FILE}.tmp" "$CONFIG_FILE" || true
}

# 辅助函数：更新配置项
update_config() {
    local key="$1"
    local value="$2"

    # 确保配置文件存在
    if [ ! -f "$CONFIG_FILE" ]; then
        echo "  警告：配置文件 $CONFIG_FILE 不存在，跳过配置 $key"
        return 1
    fi

    # 检查是否有写入权限
    if [ ! -w "$CONFIG_FILE" ]; then
        echo "  警告：配置文件 $CONFIG_FILE 不可写，尝试修改权限"
        chmod u+w "$CONFIG_FILE" 2>/dev/null || true
    fi

    # 删除注释和未注释的该配置项（单行与块）
    remove_config_key "$key"
    remove_config_block "$key"

    # 追加新配置
    echo "${key}: ${value}" >> "$CONFIG_FILE"
    echo "  已配置: ${key}: ${value}"
}

echo "配置集群名称: $CLUSTER_NAME"
update_config "cluster.name" "$CLUSTER_NAME"

echo "配置节点名称: $NODE_NAME"
update_config "node.name" "$NODE_NAME"

echo "配置 HTTP 端口: $HTTP_PORT"
update_config "http.port" "$HTTP_PORT"

echo "配置网络绑定: 0.0.0.0"
update_config "network.host" "0.0.0.0"

echo "清理与单节点模式冲突的配置项"
remove_config_key "cluster.initial_master_nodes"
remove_config_block "cluster.initial_master_nodes"
remove_config_key "discovery.seed_hosts"
remove_config_block "discovery.seed_hosts"

# 兜底：再次按关键字强制清理（兼容异常历史格式）
sed -i -E "/cluster\.initial_master_nodes[[:space:]]*:/Id" "$CONFIG_FILE" 2>/dev/null || true
sed -i -E "/discovery\.seed_hosts[[:space:]]*:/Id" "$CONFIG_FILE" 2>/dev/null || true

echo "配置发现类型: single-node"
update_config "discovery.type" "single-node"

echo "关闭安全功能"
update_config "xpack.security.enabled" "false"
update_config "xpack.security.enrollment.enabled" "false"
update_config "xpack.security.http.ssl.enabled" "false"
update_config "xpack.security.transport.ssl.enabled" "false"

# 配置数据目录和日志目录
if [ "$INSTALL_TYPE" = "tar" ]; then
    echo "配置数据目录: $ES_DATA_DIR"
    mkdir -p "$ES_DATA_DIR"
    chown -R elasticsearch:elasticsearch "$ES_DATA_DIR"
    update_config "path.data" "$ES_DATA_DIR"

    echo "配置日志目录: $ES_LOG_DIR"
    mkdir -p "$ES_LOG_DIR"
    chown -R elasticsearch:elasticsearch "$ES_LOG_DIR"
    update_config "path.logs" "$ES_LOG_DIR"
fi

# 6. 配置 JVM 内存
echo "PROGRESS:ConfiguringJVM:60"
echo "配置 JVM 内存 ($MEMORY_LIMIT)..."

if [ -f "$JVM_OPTIONS_FILE" ]; then
    cp "$JVM_OPTIONS_FILE" "${JVM_OPTIONS_FILE}.bak"

    # 替换现有的 -Xms 和 -Xmx
    sed -i "s/^-Xms[0-9a-zA-Z]*.*/-Xms${MEMORY_LIMIT}/" "$JVM_OPTIONS_FILE" 2>/dev/null || true
    sed -i "s/^-Xmx[0-9a-zA-Z]*.*/-Xmx${MEMORY_LIMIT}/" "$JVM_OPTIONS_FILE" 2>/dev/null || true

    # 如果没有找到并替换，则追加
    if ! grep -q "^-Xms${MEMORY_LIMIT}" "$JVM_OPTIONS_FILE"; then
        echo "-Xms${MEMORY_LIMIT}" >> "$JVM_OPTIONS_FILE"
    fi
    if ! grep -q "^-Xmx${MEMORY_LIMIT}" "$JVM_OPTIONS_FILE"; then
        echo "-Xmx${MEMORY_LIMIT}" >> "$JVM_OPTIONS_FILE"
    fi

    echo "JVM 内存配置已更新"
else
    echo "警告：未找到 jvm.options 文件"
fi

# 设置文件权限
if [ "$INSTALL_TYPE" = "tar" ]; then
    chown -R elasticsearch:elasticsearch "$ES_INSTALL_DIR"
fi

# 7. 创建 systemd 服务（tar 包安装）
echo "PROGRESS:CreatingService:75"
if [ "$INSTALL_TYPE" = "tar" ]; then
    echo "创建 systemd 服务..."

    # 确保 ES_BIN 可执行
    chmod +x "$ES_BIN" 2>/dev/null || true

    cat > /etc/systemd/system/elasticsearch.service << EOF
[Unit]
Description=Elasticsearch
Documentation=https://www.elastic.co
After=network.target

[Service]
Type=simple
LimitNOFILE=65535
LimitNPROC=4096
LimitAS=infinity
LimitMEMLOCK=infinity
User=elasticsearch
Group=elasticsearch
ExecStart=${ES_BIN} --foreground --pidfile=${ES_RUN_DIR}/elasticsearch.pid
ExecStop=kill -SIGTERM \$(cat ${ES_RUN_DIR}/elasticsearch.pid 2>/dev/null) 2>/dev/null || true
PIDFile=${ES_RUN_DIR}/elasticsearch.pid
Restart=on-failure
RestartSec=10
Environment="ES_JAVA_OPTS=-Xms${MEMORY_LIMIT} -Xmx${MEMORY_LIMIT}"
WorkingDirectory=${ES_INSTALL_DIR}

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable elasticsearch 2>/dev/null || true
    echo "systemd 服务已创建"
fi

# 7.5 处理 ES keystore 与权限（按安装类型）
echo "检查并处理 keystore 与权限..."
if [ "$INSTALL_TYPE" = "tar" ]; then
    if [ -n "$ES_BIN" ] && [ -f "$ES_BIN" ]; then
        # tar 安装场景：清理并重建本地 config 下的 keystore
        rm -f "$ES_HOME/elasticsearch.keystore" 2>/dev/null || true
        rm -f "$ES_INSTALL_DIR/config/elasticsearch.keystore" 2>/dev/null || true

        KEYSTORE_BIN="$ES_INSTALL_DIR/bin/elasticsearch-keystore"
        if [ -f "$KEYSTORE_BIN" ]; then
            echo "尝试重新创建 keystore (路径: $KEYSTORE_BIN)..."
            if id -u elasticsearch &>/dev/null; then
                chown elasticsearch:elasticsearch "$ES_INSTALL_DIR/config/" 2>/dev/null || true
                su -s /bin/bash elasticsearch -c "$KEYSTORE_BIN create 2>/dev/null" || true
            else
                $KEYSTORE_BIN create 2>/dev/null || true
            fi
        fi
    fi
elif [ "$INSTALL_TYPE" = "deb" ]; then
    echo "检测到 DEB 安装，跳过 keystore 重建，执行权限校正..."

    if [ -d "/etc/elasticsearch" ]; then
        chown root:elasticsearch /etc/elasticsearch 2>/dev/null || true
        chmod 2750 /etc/elasticsearch 2>/dev/null || chmod 750 /etc/elasticsearch 2>/dev/null || true
    fi

    if [ -f "/etc/elasticsearch/elasticsearch.yml" ]; then
        chown root:elasticsearch /etc/elasticsearch/elasticsearch.yml 2>/dev/null || true
        chmod 660 /etc/elasticsearch/elasticsearch.yml 2>/dev/null || true
    fi

    if [ -f "/etc/elasticsearch/jvm.options" ]; then
        chown root:elasticsearch /etc/elasticsearch/jvm.options 2>/dev/null || true
        chmod 660 /etc/elasticsearch/jvm.options 2>/dev/null || true
    fi

    if [ -f "/etc/elasticsearch/elasticsearch.keystore" ]; then
        chown root:elasticsearch /etc/elasticsearch/elasticsearch.keystore 2>/dev/null || true
        chmod 660 /etc/elasticsearch/elasticsearch.keystore 2>/dev/null || true
    fi

    if [ -d "/etc/elasticsearch/certs" ]; then
        chown -R root:elasticsearch /etc/elasticsearch/certs 2>/dev/null || true
        find /etc/elasticsearch/certs -type d -exec chmod 750 {} \; 2>/dev/null || true
        find /etc/elasticsearch/certs -type f -exec chmod 640 {} \; 2>/dev/null || true
    fi

    chown -R elasticsearch:elasticsearch "$ES_DATA_DIR" 2>/dev/null || true
    chown -R elasticsearch:elasticsearch "$ES_LOG_DIR" 2>/dev/null || true
fi

# 8. 启动服务
echo "PROGRESS:Starting:80"
echo "正在启动服务..."

# ES 8.x 禁止以 root 运行，必须使用 elasticsearch 用户
if [ "$EUID" -eq 0 ]; then
    echo "检测到以 root 运行，将切换到 elasticsearch 用户..."
fi

if [ "$INSTALL_TYPE" = "tar" ]; then
    # tar 包安装，确保使用 elasticsearch 用户启动
    # 先确保目录权限正确
    chown -R elasticsearch:elasticsearch "$ES_INSTALL_DIR" 2>/dev/null || true
    chown -R elasticsearch:elasticsearch "$ES_DATA_DIR" 2>/dev/null || true
    chown -R elasticsearch:elasticsearch "$ES_LOG_DIR" 2>/dev/null || true
    chown -R elasticsearch:elasticsearch "$ES_RUN_DIR" 2>/dev/null || true
    chmod -R 755 "$ES_INSTALL_DIR" 2>/dev/null || true

    # 使用 systemctl 启动（推荐）
    systemctl daemon-reload 2>/dev/null || true
    systemctl enable elasticsearch 2>/dev/null || true
    systemctl stop elasticsearch 2>/dev/null || true
    sleep 2

    # 先尝试 systemctl 启动，并捕获退出码
    SYSTEMCTL_OUTPUT=$(systemctl start elasticsearch 2>&1)
    SYSTEMCTL_EXIT=$?

    if [ $SYSTEMCTL_EXIT -eq 0 ]; then
        echo "systemctl 启动成功"
        echo "$SYSTEMCTL_OUTPUT" | tee -a "$LOG_FILE" || true
    else
        echo "systemctl 启动失败，尝试直接以 elasticsearch 用户启动..."
        echo "$SYSTEMCTL_OUTPUT" | tee -a "$LOG_FILE" || true
        # 直接以 elasticsearch 用户启动
        rm -f "$ES_RUN_DIR/elasticsearch.pid" 2>/dev/null || true
        su -s /bin/bash elasticsearch -c "ES_JAVA_OPTS='-Xms${MEMORY_LIMIT} -Xmx${MEMORY_LIMIT}' $ES_BIN --foreground --pidfile=$ES_RUN_DIR/elasticsearch.pid" &
        echo "ES 进程已启动 (PID: $!)"
    fi
else
    # 包管理器安装，使用 systemctl
    systemctl daemon-reload 2>/dev/null || true
    systemctl enable elasticsearch 2>/dev/null || true
    systemctl restart elasticsearch 2>/dev/null || systemctl start elasticsearch 2>/dev/null || true
fi

# 9. 等待启动并验证
echo "PROGRESS:Verifying:85"
echo "等待服务启动..."

SUCCESS=false
COUNT=0
while [ $COUNT -lt 40 ]; do
    # 尝试 HTTP API 检测
    ES_RESP=""
    if command -v curl &>/dev/null; then
        ES_RESP=$(curl -s --connect-timeout 5 "http://localhost:$HTTP_PORT" 2>/dev/null || echo "")
    fi

    if [ -n "$ES_RESP" ] && echo "$ES_RESP" | grep -q "cluster_name\|version\|status"; then
        echo "Elasticsearch 服务已成功启动"
        SUCCESS=true
        break
    fi

    # 检查进程
    if pgrep -f "elasticsearch" &>/dev/null; then
        echo "Elasticsearch 进程已启动，等待 API 响应... ($((COUNT+1))/40)"
    else
        echo "等待 Elasticsearch 进程启动... ($((COUNT+1))/40)"
    fi

    sleep 3
    COUNT=$((COUNT+1))
done

# 10. 验证安装
echo "PROGRESS:Verifying:95"
if [ "$SUCCESS" = true ]; then
    echo ""
    echo "========================================"
    echo "      Elasticsearch 安装完成！"
    echo "========================================"
    echo "安装类型: $INSTALL_TYPE"
    echo "安装目录: $ES_INSTALL_DIR"
    echo "HTTP 端口: $HTTP_PORT"
    echo "访问地址: http://localhost:$HTTP_PORT"
    echo ""
    echo "--- MACHINE READABLE ---"
    echo "INSTALLED: true"
    echo "RUNNING: true"
    # ES 8.x JSON: {"version":{"number":"8.x.x",...}}
    ES_VERSION=$(curl -s http://localhost:$HTTP_PORT 2>/dev/null | grep -oE '"number"[[:space:]]*:[[:space:]]*"[0-9]+\.[0-9]+\.[0-9]+"' | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
    if [ -z "$ES_VERSION" ]; then
        # 备选：尝试直接从响应中提取版本号格式
        ES_VERSION=$(curl -s http://localhost:$HTTP_PORT 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
    fi
    echo "VERSION: ${ES_VERSION:-未知}"
    echo "PORT: $HTTP_PORT"
    echo "------------------------"
    echo "STAGE:SUCCESS"
else
    echo ""
    echo "警告：Elasticsearch 在 120 秒内未能在端口 $HTTP_PORT 启动"

    echo "权限快照（目录）:"
    ls -ld /etc/elasticsearch /var/lib/elasticsearch /var/log/elasticsearch 2>/dev/null || true

    echo "权限快照（关键文件）:"
    [ -f /etc/elasticsearch/elasticsearch.yml ] && ls -l /etc/elasticsearch/elasticsearch.yml 2>/dev/null || true
    [ -f /etc/elasticsearch/jvm.options ] && ls -l /etc/elasticsearch/jvm.options 2>/dev/null || true
    [ -f /etc/elasticsearch/elasticsearch.keystore ] && ls -l /etc/elasticsearch/elasticsearch.keystore 2>/dev/null || true

    if [ -d "$ES_LOG_DIR" ]; then
        echo "最后 30 行日志:"
        tail -n 30 "$ES_LOG_DIR"/*.log 2>/dev/null | head -50 || true
    fi
    echo ""
    echo "--- MACHINE READABLE ---"
    echo "INSTALLED: true"
    echo "RUNNING: false"
    echo "VERSION: 未知"
    echo "PORT: $HTTP_PORT"
    echo "------------------------"
    echo "STAGE:WARNING"
fi