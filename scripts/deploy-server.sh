#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXPORT_PATH="$REPO_ROOT/dev-game/Export/PreAlpha/0.0.2"
REMOTE_DIR="~/server"

# Load secrets
# shellcheck source=../dev-game/.env
set -a; source "$REPO_ROOT/dev-game/.env"; set +a
REMOTE="godotadmin@${SERVER_IP}"

# Prompt for sudo password once; feed it to remote `sudo -S` for every command.
# Skip the prompt when SUDO_PW is already provided in the env (e.g. invoked by server-frontend).
if [ -z "${SUDO_PW:-}" ]; then
    read -rsp "Sudo password for $REMOTE: " SUDO_PW
    echo
fi
export SUDO_PW

remote_sudo() {
    # -S reads password from stdin; -p '' suppresses the prompt so it doesn't
    # mix with command output.
    ssh "$REMOTE" "sudo -S -p '' $1" <<<"$SUDO_PW"
}

echo "==> Stopping godot-server on remote..."
remote_sudo "systemctl stop godot-server"

echo "==> Exporting server build..."
${GODOT_BIN:-godot} --headless --path "$REPO_ROOT/dev-game" \
    --export-release "Server Build" "$EXPORT_PATH/server.x86_64"

echo "==> Uploading to $REMOTE..."
ssh "$REMOTE" "mkdir -p $REMOTE_DIR"
scp "$EXPORT_PATH/server.x86_64" "$EXPORT_PATH/server.pck" "$REMOTE:$REMOTE_DIR/"
scp -r "$EXPORT_PATH/data_dev-game_linuxbsd_x86_64" "$REMOTE:$REMOTE_DIR/"

echo "==> Restarting godot-server service..."
remote_sudo "bash -c 'chmod +x $REMOTE_DIR/server.x86_64 && systemctl restart godot-server'"

echo "==> Done. Server is live."
