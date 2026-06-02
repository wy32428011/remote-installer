#!/bin/bash
set -e

# 通用 Linux 兼容入口：按目标发行版转交给系统专属安装脚本。
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f /etc/os-release ]; then
    . /etc/os-release
fi

case "${ID:-}" in
    ubuntu)
        exec bash "$SCRIPT_DIR/install_ubuntu.sh" "$@"
        ;;
    centos|rhel|rocky|almalinux)
        exec bash "$SCRIPT_DIR/install_centos.sh" "$@"
        ;;
esac

if [ -f /etc/redhat-release ]; then
    exec bash "$SCRIPT_DIR/install_centos.sh" "$@"
fi

echo "错误：当前 Linux 发行版暂不支持：${ID:-未知}"
exit 1
