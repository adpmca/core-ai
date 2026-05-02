#!/usr/bin/env bash
# start.sh — build and run DivaFsMcpServer in HTTP mode
# Usage:
#   ./start.sh                        # defaults: port 8811, Desktop allowed, writes+scripts enabled
#   PORT=9000 ./start.sh
#   ALLOWED_PATH="C:\\Users\\Admin\\Documents" ./start.sh
#   ALLOW_WRITES=false ./start.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# ── Config (override via env vars) ────────────────────────────────────────────
PORT="${PORT:-8811}"
ALLOWED_PATH="${ALLOWED_PATH:-C:\\Users\\Admin\\Desktop}"
ALLOW_WRITES="${ALLOW_WRITES:-true}"
ALLOW_SCRIPT="${ALLOW_SCRIPT:-true}"
SCRIPT_TIMEOUT="${SCRIPT_TIMEOUT:-30}"
MAX_SEARCH_RESULTS="${MAX_SEARCH_RESULTS:-200}"

PUBLISH_DIR="$SCRIPT_DIR/publish"

# ── Detect executable name ─────────────────────────────────────────────────────
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" || "$OSTYPE" == "win32" ]]; then
    EXE="$PUBLISH_DIR/diva-fs-mcp.exe"
else
    EXE="$PUBLISH_DIR/diva-fs-mcp"
fi

# ── Stop any existing instance ─────────────────────────────────────────────────
echo ">> Stopping existing diva-fs-mcp instance (if any)..."
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" || "$OSTYPE" == "win32" ]]; then
    # Windows via taskkill
    taskkill //F //IM "diva-fs-mcp.exe" 2>/dev/null && echo "   Stopped." || echo "   None running."
else
    pkill -f "diva-fs-mcp" 2>/dev/null && echo "   Stopped." || echo "   None running."
fi

# Give the process a moment to release the DLL lock
sleep 1

# ── Build & publish ────────────────────────────────────────────────────────────
echo ">> Publishing DivaFsMcpServer..."
dotnet publish "$SCRIPT_DIR/DivaFsMcpServer.csproj" \
    -c Release \
    -o "$PUBLISH_DIR" \
    --no-restore \
    -v q

echo "   Published to: $PUBLISH_DIR"

# ── Start server ───────────────────────────────────────────────────────────────
echo ""
echo ">> Starting DivaFsMcpServer on http://localhost:$PORT"
echo "   Allowed path : $ALLOWED_PATH"
echo "   Allow writes : $ALLOW_WRITES"
echo "   Allow script : $ALLOW_SCRIPT"
echo ""

export ASPNETCORE_URLS="http://localhost:$PORT"
export DIVA_FS_MCP_PORT="$PORT"
export FileSystem__AllowedBasePaths__0="$ALLOWED_PATH"
export FileSystem__AllowWrites="$ALLOW_WRITES"
export FileSystem__AllowScript="$ALLOW_SCRIPT"
export FileSystem__ScriptTimeoutSeconds="$SCRIPT_TIMEOUT"
export FileSystem__MaxSearchResults="$MAX_SEARCH_RESULTS"

exec "$EXE"
