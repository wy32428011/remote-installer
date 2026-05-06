#!/bin/bash
set -e

JDK_VERSION_LABEL="JDK 8"
INSTALL_DIR="${INSTALL_DIR:-/opt/jdk8}"
PROFILE_FILE="/etc/profile.d/jdk8.sh"
JAVA_BIN_LINK="/usr/local/bin/java"
JAVAC_BIN_LINK="/usr/local/bin/javac"

write_progress() {
    echo "PROGRESS:$1:$2"
}

write_progress "Initializing" 5
echo "${JDK_VERSION_LABEL} Linux 卸载脚本开始..."

if [ "$EUID" -ne 0 ]; then
    echo "错误：请使用 root 权限运行此脚本"
    exit 1
fi

write_progress "Cleaning" 45
rm -rf "$INSTALL_DIR"
rm -f "$PROFILE_FILE"
if [ -L "$JAVA_BIN_LINK" ] && [ "$(readlink -f "$JAVA_BIN_LINK")" = "$INSTALL_DIR/bin/java" ]; then
    rm -f "$JAVA_BIN_LINK"
fi
if [ -L "$JAVAC_BIN_LINK" ] && [ "$(readlink -f "$JAVAC_BIN_LINK")" = "$INSTALL_DIR/bin/javac" ]; then
    rm -f "$JAVAC_BIN_LINK"
fi

write_progress "Finalizing" 90
installed=false
if [ -x "$INSTALL_DIR/bin/java" ] || [ -d "$INSTALL_DIR" ]; then
    installed=true
fi

echo "PROGRESS:Complete:100"
echo "INSTALLED:$installed"
echo "VERSION:removed"
echo "RUNNING:inactive"