#!/usr/bin/env bash
# =============================================================================
# setup-dev.sh — Configure .vscode et .zed pour The Mountain (Linux)
# Exécuter depuis la racine du projet : bash scripts/setup-dev.sh
# =============================================================================
set -e

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo -e "${CYAN}╔══════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║  Setup Dev — The Mountain                    ║${NC}"
echo -e "${CYAN}╚══════════════════════════════════════════════╝${NC}"
echo "Racine du projet : $PROJECT_ROOT"
echo ""

# =============================================================================
# Détection de Godot .NET
# =============================================================================

# Vérifie que l'exécutable trouvé est bien Godot 4.x
check_godot4() {
    local path="$1"
    # Flatpak : on ne peut pas vérifier la version facilement, on fait confiance
    [[ "$path" == flatpak* ]] && return 0
    "$path" --version 2>&1 | grep -qE "^4\.[0-9]"
}

find_godot() {
    # 1. Noms courants dans le PATH
    for name in godot godot-mono godot4; do
        local p
        p=$(command -v "$name" 2>/dev/null) || continue
        check_godot4 "$p" && echo "$p" && return 0
    done

    # 2. Binaires dans ~/.local/bin et ~/Applications
    for dir in "$HOME/.local/bin" "$HOME/Applications" "$HOME/bin"; do
        [ -d "$dir" ] || continue
        while IFS= read -r -d '' f; do
            check_godot4 "$f" && echo "$f" && return 0
        done < <(find "$dir" -maxdepth 1 -name "Godot*" -type f -executable -print0 2>/dev/null)
    done

    # 3. Flatpak
    if command -v flatpak &>/dev/null; then
        for id in org.godotengine.GodotSharp org.godotengine.Godot; do
            flatpak info "$id" &>/dev/null 2>&1 && echo "flatpak run $id" && return 0
        done
    fi

    # 4. /opt et /usr/local
    for dir in /opt/godot /opt/Godot /usr/local/bin; do
        [ -d "$dir" ] || continue
        while IFS= read -r -d '' f; do
            check_godot4 "$f" && echo "$f" && return 0
        done < <(find "$dir" -maxdepth 2 -name "Godot*" -type f -executable -print0 2>/dev/null)
    done

    # 5. Recherche générale dans $HOME (profondeur 5)
    local found
    found=$(find "$HOME" -maxdepth 5 -name "Godot_v4*" -type f -executable 2>/dev/null | head -1)
    [ -n "$found" ] && check_godot4 "$found" && echo "$found" && return 0

    return 1
}

echo -e "Recherche de Godot .NET (v4)..."
GODOT_PATH=""
if GODOT_PATH=$(find_godot 2>/dev/null); then
    echo -e "${GREEN}✓ Godot trouvé :${NC} $GODOT_PATH"

    # Avertissement Flatpak : le Godot Tools VS Code extension ne supporte pas Flatpak
    if [[ "$GODOT_PATH" == flatpak* ]]; then
        echo -e "${YELLOW}⚠  Godot via Flatpak détecté. L'extension VS Code 'Godot Tools' nécessite"
        echo -e "   un exécutable direct. Envisagez d'installer Godot hors Flatpak pour le debug.${NC}"
    fi
else
    echo -e "${YELLOW}Godot introuvable automatiquement.${NC}"
    read -rp "Chemin complet vers l'exécutable Godot : " GODOT_PATH
    if [[ "$GODOT_PATH" != flatpak* ]] && [ ! -x "$GODOT_PATH" ]; then
        echo -e "${RED}Erreur : '$GODOT_PATH' n'est pas exécutable.${NC}"; exit 1
    fi
fi

echo ""

# =============================================================================
# Création des répertoires
# =============================================================================
mkdir -p "$PROJECT_ROOT/.vscode"
mkdir -p "$PROJECT_ROOT/.zed"

# =============================================================================
# .vscode/settings.json  (gitignored — chemin Godot local)
# =============================================================================
cat > "$PROJECT_ROOT/.vscode/settings.json" << EOF
{
    "godot_tools.editor_path": "$GODOT_PATH",
    "godot_tools.gdscript_lsp_server_port": 6005,
    "dotnet.defaultSolution": "src/*.sln",
    "editor.formatOnSave": true,
    "[csharp]": {
        "editor.defaultFormatter": "ms-dotnettools.csharp"
    }
}
EOF
echo -e "${GREEN}✓${NC} .vscode/settings.json"

# =============================================================================
# .vscode/tasks.json  (commité — chemin lu depuis settings via ${config:...})
# =============================================================================
cat > "$PROJECT_ROOT/.vscode/tasks.json" << 'EOF'
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "type": "shell",
            "command": "dotnet build ${workspaceFolder}/src",
            "group": { "kind": "build", "isDefault": true },
            "problemMatcher": "$msCompile",
            "presentation": { "reveal": "silent", "panel": "shared" }
        },
        {
            "label": "Ouvrir l'éditeur Godot",
            "type": "shell",
            "command": "${config:godot_tools.editor_path}",
            "args": ["--editor", "--path", "${workspaceFolder}/src"],
            "group": "none",
            "presentation": { "reveal": "silent", "panel": "shared" }
        },
        {
            "label": "Lancer le jeu",
            "type": "shell",
            "command": "${config:godot_tools.editor_path}",
            "args": ["--path", "${workspaceFolder}/src"],
            "group": "test",
            "presentation": { "reveal": "always", "panel": "new" },
            "dependsOn": "build"
        }
    ]
}
EOF
echo -e "${GREEN}✓${NC} .vscode/tasks.json"

# =============================================================================
# .vscode/launch.json  (commité)
# =============================================================================
cat > "$PROJECT_ROOT/.vscode/launch.json" << 'EOF'
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Jouer dans l'éditeur Godot",
            "type": "godot",
            "request": "launch",
            "project": "${workspaceFolder}/src",
            "port": 6007,
            "address": "127.0.0.1",
            "launch_game_instance": true,
            "launch_scene": false
        },
        {
            "name": "Attacher au processus Godot (C#)",
            "type": "coreclr",
            "request": "attach",
            "processId": "${command:pickProcess}"
        }
    ]
}
EOF
echo -e "${GREEN}✓${NC} .vscode/launch.json"

# =============================================================================
# .vscode/extensions.json  (commité)
# =============================================================================
cat > "$PROJECT_ROOT/.vscode/extensions.json" << 'EOF'
{
    "recommendations": [
        "geequlim.godot-tools",
        "ms-dotnettools.csharp",
        "ms-dotnettools.csdevkit"
    ]
}
EOF
echo -e "${GREEN}✓${NC} .vscode/extensions.json"

# =============================================================================
# .zed/tasks.json  (gitignored — chemin Godot intégré directement)
# =============================================================================
cat > "$PROJECT_ROOT/.zed/tasks.json" << EOF
[
    {
        "label": "Build",
        "command": "dotnet",
        "args": ["build", "src/"],
        "cwd": "\${ZED_WORKTREE_ROOT}"
    },
    {
        "label": "Ouvrir l'éditeur Godot",
        "command": "$GODOT_PATH",
        "args": ["--editor", "--path", "\${ZED_WORKTREE_ROOT}/src"],
        "cwd": "\${ZED_WORKTREE_ROOT}"
    },
    {
        "label": "Lancer le jeu",
        "command": "$GODOT_PATH",
        "args": ["--path", "\${ZED_WORKTREE_ROOT}/src"],
        "cwd": "\${ZED_WORKTREE_ROOT}"
    }
]
EOF
echo -e "${GREEN}✓${NC} .zed/tasks.json"

# =============================================================================
# .zed/settings.json  (gitignored)
# =============================================================================
cat > "$PROJECT_ROOT/.zed/settings.json" << 'EOF'
{
    "languages": {
        "C#": {
            "language_servers": ["omnisharp"],
            "format_on_save": "on"
        }
    }
}
EOF
echo -e "${GREEN}✓${NC} .zed/settings.json"

# =============================================================================
# .gitignore — ajout des entrées IDE si absentes
# =============================================================================
GITIGNORE="$PROJECT_ROOT/.gitignore"
add_if_absent() {
    grep -qxF "$1" "$GITIGNORE" 2>/dev/null || echo "$1" >> "$GITIGNORE"
}
add_if_absent ""
add_if_absent "# Configs IDE locales (chemin Godot spécifique à la machine)"
add_if_absent ".vscode/settings.json"
add_if_absent ".zed/"
echo -e "${GREEN}✓${NC} .gitignore mis à jour"

echo ""
echo -e "${GREEN}╔══════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║  Setup terminé !                             ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════╝${NC}"
echo "VS Code → Ctrl+Shift+P → 'Show Recommended Extensions' pour installer les extensions."
