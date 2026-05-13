#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXPORT_PATH="$REPO_ROOT/dev-game/Export/PreAlpha/0.0.2"
REMOTE_DIR="~/server"

# Load secrets
# shellcheck source=../dev-game/.env
set -a; source "$REPO_ROOT/dev-game/.env"; set +a
REMOTE="godotadmin@${SERVER_IP}"

echo "==> Stopping godot-server on remote..."
ssh -t "$REMOTE" "sudo systemctl stop godot-server"

echo "==> Exporting server build..."
${GODOT_BIN:-godot} --headless --path "$REPO_ROOT/dev-game" \
    --export-release "Server Build" "$EXPORT_PATH/server.x86_64"

echo "==> Uploading to $REMOTE..."
ssh "$REMOTE" "mkdir -p $REMOTE_DIR"
scp "$EXPORT_PATH/server.x86_64" "$EXPORT_PATH/server.pck" "$REMOTE:$REMOTE_DIR/"
scp -r "$EXPORT_PATH/data_dev-game_linuxbsd_x86_64" "$REMOTE:$REMOTE_DIR/"

echo "==> Restarting godot-server service..."
ssh -t "$REMOTE" "sudo chmod +x $REMOTE_DIR/server.x86_64 && sudo systemctl restart godot-server"

echo "==> Done. Server is live."
