#!/bin/bash
set -e

JDK_VERSION_LABEL="JDK 11"
VERSION_HINT="11"
INSTALL_DIR="${INSTALL_DIR:-/opt/jdk11}"
SET_AS_DEFAULT="${SET_AS_DEFAULT:-true}"
PACKAGE_PATH="${PACKAGE_PATH:-}"
PROFILE_FILE="/etc/profile.d/jdk11.sh"
JAVA_BIN_LINK="/usr/local/bin/java"
JAVAC_BIN_LINK="/usr/local/bin/javac"

write_progress() {
    echo "PROGRESS:$1:$2"
}

get_java_version() {
    local java_cmd="$1"
    "$java_cmd" -version 2>&1 | head -n 1 | grep -oE '([0-9]+\.)+[0-9_]+' | head -n 1 || true
}

resolve_java_home_from_directory() {
    local source_dir="$1"
    local direct_java="$source_dir/bin/java"
    if [ -x "$direct_java" ]; then
        printf '%s' "$source_dir"
        return 0
    fi

    local nested_java
    nested_java=$(find "$source_dir" -type f -path '*/bin/java' | head -n 1)
    if [ -n "$nested_java" ]; then
        dirname "$(dirname "$nested_java")"
        return 0
    fi

    return 1
}

write_progress "Initializing" 5
echo "${JDK_VERSION_LABEL} Linux 安装脚本开始..."

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

if [ -z "$PACKAGE_PATH" ] || [ ! -e "$PACKAGE_PATH" ]; then
    echo "错误：未提供 ${JDK_VERSION_LABEL} 离线安装资源"
    exit 1
fi

write_progress "Preparing" 15
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

tmp_dir=$(mktemp -d)
trap 'rm -rf "$tmp_dir"' EXIT

write_progress "Extracting" 35
if [ -d "$PACKAGE_PATH" ]; then
    extracted_dir=$(resolve_java_home_from_directory "$PACKAGE_PATH" || true)
else
    case "$PACKAGE_PATH" in
        *.tar.gz|*.tgz)
            tar -xzf "$PACKAGE_PATH" -C "$tmp_dir"
            ;;
        *.tar.xz)
            tar -xJf "$PACKAGE_PATH" -C "$tmp_dir"
            ;;
        *)
            echo "错误：不支持的 ${JDK_VERSION_LABEL} Linux 离线包格式：$PACKAGE_PATH"
            exit 1
            ;;
    esac

    extracted_dir=$(resolve_java_home_from_directory "$tmp_dir" || true)
fi

if [ -z "$extracted_dir" ]; then
    echo "错误：未能解析 ${JDK_VERSION_LABEL} 安装目录"
    exit 1
fi

cp -a "$extracted_dir"/. "$INSTALL_DIR"/
chmod -R 755 "$INSTALL_DIR"

write_progress "Configuring" 60
cat > "$PROFILE_FILE" <<EOF
export JAVA_HOME=${INSTALL_DIR}
export PATH=\$JAVA_HOME/bin:\$PATH
EOF
chmod 644 "$PROFILE_FILE"

if [ "$SET_AS_DEFAULT" = "true" ]; then
    ln -sfn "$INSTALL_DIR/bin/java" "$JAVA_BIN_LINK"
    ln -sfn "$INSTALL_DIR/bin/javac" "$JAVAC_BIN_LINK"
fi

write_progress "Verifying" 85
if [ ! -x "$INSTALL_DIR/bin/java" ]; then
    echo "错误：${JDK_VERSION_LABEL} 安装后未找到 java 可执行文件"
    exit 1
fi

installed_version=$(get_java_version "$INSTALL_DIR/bin/java")
if ! echo "$installed_version" | grep -q "$VERSION_HINT"; then
    echo "错误：检测到的 Java 版本与 ${JDK_VERSION_LABEL} 不匹配：$installed_version"
    exit 1
fi

write_progress "Complete" 100
echo "${JDK_VERSION_LABEL} 安装完成：$INSTALL_DIR"
echo "INSTALLED:true"
echo "VERSION:$installed_version"
echo "RUNNING:inactive"
echo "JAVA_HOME:$INSTALL_DIR"
