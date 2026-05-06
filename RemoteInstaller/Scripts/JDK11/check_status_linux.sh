#!/bin/bash
set -e

INSTALL_DIR="${INSTALL_DIR:-/opt/jdk11}"
VERSION_HINT="11"

resolve_java_cmd() {
    if [ -x "$INSTALL_DIR/bin/java" ]; then
        echo "$INSTALL_DIR/bin/java"
        return
    fi
    if command -v java >/dev/null 2>&1; then
        echo "$(command -v java)"
        return
    fi
    echo ""
}

JAVA_CMD="$(resolve_java_cmd)"
INSTALLED=false
VERSION="unknown"
RUNNING=inactive

if [ -x "$INSTALL_DIR/bin/java" ] || [ -n "$JAVA_CMD" ]; then
    if [ -x "$INSTALL_DIR/bin/java" ]; then
        INSTALLED=true
        JAVA_CMD="$INSTALL_DIR/bin/java"
    elif "$JAVA_CMD" -version 2>&1 | head -n 1 | grep -q "$VERSION_HINT"; then
        INSTALLED=true
    fi
fi

if [ "$INSTALLED" = true ]; then
    VERSION=$($JAVA_CMD -version 2>&1 | head -n 1 | grep -oE '([0-9]+\.)+[0-9_]+' | head -n 1 || true)
fi

echo "INSTALLED:$INSTALLED"
echo "VERSION:$VERSION"
echo "RUNNING:$RUNNING"
echo "JAVA_HOME:$INSTALL_DIR"