# =============================================================================
# setup-dev.ps1 — Configure .vscode et .zed pour The Mountain (Windows)
# Exécuter depuis la racine du projet : .\scripts\setup-dev.ps1
# Si bloqué par la policy : Set-ExecutionPolicy -Scope Process Bypass
# =============================================================================
$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Setup Dev — The Mountain                    ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Racine du projet : $ProjectRoot"
Write-Host ""

# =============================================================================
# Détection de Godot .NET
# =============================================================================

function Test-Godot4 {
    param([string]$Path)
    try {
        $version = & $Path --version 2>&1 | Select-Object -First 1
        return $version -match "^4\.[0-9]"
    } catch { return $false }
}

function Find-Godot {
    # 1. PATH
    foreach ($name in @("godot", "godot-mono", "godot4")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd -and (Test-Godot4 $cmd.Source)) { return $cmd.Source }
    }

    # 2. Scoop
    $scoopRoot = "$env:USERPROFILE\scoop\apps"
    foreach ($dir in @("godot-mono", "godot4-mono", "godot")) {
        $current = Join-Path $scoopRoot "$dir\current"
        if (Test-Path $current) {
            $exe = Get-ChildItem $current -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($exe -and (Test-Godot4 $exe.FullName)) { return $exe.FullName }
        }
    }

    # 3. Winget / dossiers Program Files et LocalAppData
    $pfDirs = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        "$env:LOCALAPPDATA\Programs",
        "$env:LOCALAPPDATA\Godot",
        "C:\Godot"
    )
    foreach ($dir in $pfDirs) {
        if (-not $dir -or -not (Test-Path $dir)) { continue }
        $exe = Get-ChildItem $dir -Filter "Godot_v4*.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($exe -and (Test-Godot4 $exe.FullName)) { return $exe.FullName }
    }

    # 4. Recherche dans les emplacements courants de l'utilisateur
    $searchDirs = @(
        "$env:USERPROFILE\Downloads",
        "$env:USERPROFILE\Desktop",
        "$env:USERPROFILE\Documents\Godot",
        "$env:USERPROFILE\AppData\Local\Programs"
    )
    foreach ($dir in $searchDirs) {
        if (-not (Test-Path $dir)) { continue }
        $exe = Get-ChildItem $dir -Filter "Godot_v4*.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($exe -and (Test-Godot4 $exe.FullName)) { return $exe.FullName }
    }

    return $null
}

Write-Host "Recherche de Godot .NET (v4)..."
$GodotPath = Find-Godot

if ($GodotPath) {
    Write-Host "✓ Godot trouvé : $GodotPath" -ForegroundColor Green
} else {
    Write-Host "Godot introuvable automatiquement." -ForegroundColor Yellow
    $GodotPath = Read-Host "Chemin complet vers l'exécutable Godot (.exe)"
    if (-not (Test-Path $GodotPath)) {
        Write-Host "Erreur : '$GodotPath' introuvable." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""

# JSON nécessite des backslashes échappés
$GodotPathJson = $GodotPath -replace '\\', '\\'

# =============================================================================
# Création des répertoires
# =============================================================================
New-Item -ItemType Directory -Force -Path "$ProjectRoot\.vscode" | Out-Null
New-Item -ItemType Directory -Force -Path "$ProjectRoot\.zed"    | Out-Null

# =============================================================================
# .vscode/settings.json  (gitignored — chemin Godot local)
# =============================================================================
@"
{
    "godot_tools.editor_path": "$GodotPathJson",
    "godot_tools.gdscript_lsp_server_port": 6005,
    "dotnet.defaultSolution": "src/*.sln",
    "editor.formatOnSave": true,
    "[csharp]": {
        "editor.defaultFormatter": "ms-dotnettools.csharp"
    }
}
"@ | Set-Content -Encoding UTF8 "$ProjectRoot\.vscode\settings.json"
Write-Host "✓ .vscode/settings.json" -ForegroundColor Green

# =============================================================================
# .vscode/tasks.json  (commité — chemin lu depuis settings via ${config:...})
# =============================================================================
@'
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
'@ | Set-Content -Encoding UTF8 "$ProjectRoot\.vscode\tasks.json"
Write-Host "✓ .vscode/tasks.json" -ForegroundColor Green

# =============================================================================
# .vscode/launch.json  (commité)
# =============================================================================
@'
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
'@ | Set-Content -Encoding UTF8 "$ProjectRoot\.vscode\launch.json"
Write-Host "✓ .vscode/launch.json" -ForegroundColor Green

# =============================================================================
# .vscode/extensions.json  (commité)
# =============================================================================
@'
{
    "recommendations": [
        "geequlim.godot-tools",
        "ms-dotnettools.csharp",
        "ms-dotnettools.csdevkit"
    ]
}
'@ | Set-Content -Encoding UTF8 "$ProjectRoot\.vscode\extensions.json"
Write-Host "✓ .vscode/extensions.json" -ForegroundColor Green

# =============================================================================
# .zed/tasks.json  (gitignored — chemin Godot intégré directement)
# =============================================================================
@"
[
    {
        "label": "Build",
        "command": "dotnet",
        "args": ["build", "src/"],
        "cwd": "`${ZED_WORKTREE_ROOT}"
    },
    {
        "label": "Ouvrir l'éditeur Godot",
        "command": "$GodotPathJson",
        "args": ["--editor", "--path", "`${ZED_WORKTREE_ROOT}/src"],
        "cwd": "`${ZED_WORKTREE_ROOT}"
    },
    {
        "label": "Lancer le jeu",
        "command": "$GodotPathJson",
        "args": ["--path", "`${ZED_WORKTREE_ROOT}/src"],
        "cwd": "`${ZED_WORKTREE_ROOT}"
    }
]
"@ | Set-Content -Encoding UTF8 "$ProjectRoot\.zed\tasks.json"
Write-Host "✓ .zed/tasks.json" -ForegroundColor Green

# =============================================================================
# .zed/settings.json  (gitignored)
# =============================================================================
@'
{
    "languages": {
        "C#": {
            "language_servers": ["omnisharp"],
            "format_on_save": "on"
        }
    }
}
'@ | Set-Content -Encoding UTF8 "$ProjectRoot\.zed\settings.json"
Write-Host "✓ .zed/settings.json" -ForegroundColor Green

# =============================================================================
# .gitignore — ajout des entrées IDE si absentes
# =============================================================================
$GitIgnorePath = "$ProjectRoot\.gitignore"
$GitIgnoreContent = if (Test-Path $GitIgnorePath) { Get-Content $GitIgnorePath -Raw } else { "" }

$entries = @(
    "",
    "# Configs IDE locales (chemin Godot spécifique à la machine)",
    ".vscode/settings.json",
    ".zed/"
)
foreach ($entry in $entries) {
    if ($GitIgnoreContent -notmatch "(?m)^$([regex]::Escape($entry))$") {
        Add-Content -Path $GitIgnorePath -Value $entry
    }
}
Write-Host "✓ .gitignore mis à jour" -ForegroundColor Green

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  Setup terminé !                             ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host "VS Code → Ctrl+Shift+P → 'Show Recommended Extensions' pour installer les extensions."
