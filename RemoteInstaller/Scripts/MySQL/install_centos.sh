#!/bin/bash
set -e

# CentOS/RHEL 专属安装入口：只负责系统校验、冒烟检查和公共执行体加载。
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="$(basename "$SCRIPT_DIR")"
if [ -f /etc/os-release ]; then
    . /etc/os-release
fi

case "${ID:-}" in
    centos|rhel|rocky|almalinux)
        ;;
    *)
        if [ ! -f /etc/redhat-release ]; then
            echo "错误：$APP_NAME 的 CentOS 安装脚本只能在 CentOS/RHEL 系上执行，当前系统：${ID:-未知}"
            exit 1
        fi
        ;;
esac

if [ ! -f "$SCRIPT_DIR/install_common.sh" ]; then
    echo "错误：缺少公共安装执行体：$SCRIPT_DIR/install_common.sh"
    exit 1
fi

if [ "${REMOTE_INSTALLER_SMOKE_TEST:-false}" = "true" ]; then
    bash -n "$SCRIPT_DIR/install_common.sh"
    echo "SMOKE:$APP_NAME:centos:ok"
    exit 0
fi

export REMOTE_INSTALLER_TARGET_OS="centos"
exec bash "$SCRIPT_DIR/install_common.sh" "$@"
