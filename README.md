# C61-The-Mountain
Projet synthèse dans le cadre du programme de technique de l'informatique du cégep du Vieux-Montréal

## Membres de l'équipe

- Samuel Bergeron-Lagacé
- Jean-Marc Bouchard

## Démarrage rapide

La source du projet Godot se situe dans `./dev-game/`. Pour démarrer le projet sans configuration avancé, démarrer Godot et ouvrir le projet à la source du projet Godot. Ouvrez votre IDE a la racine du projet complet. Le projet devrait déja être initialisé et prêt à tester. Une configuration avancée est requise pour coder directement sur l'IDE.


## Configuration avancée

La configuration avancé permet de coder à l'intérieur de votre IDE sans avoir a retourner sur Godot Engine sans arrêt. Pour se faire, il faut ajouter un dossier .vscode (ou .zed) et y ajouter ces fichiers:
- `tasks.json` : Permet de définir des actions comme démarrer le projet, l'éditeur etc.
- `debug.json` : Permet de configurer le debbugeur pour csharp et le projet en soi
- `launch.json`: 

```json
[
  {
    "label": "Godot: Run Game",
    "command": "C:\\Users\\Administrateur.UTILISA-2J0T246\\Desktop\\godot\\Godot_v4.6.1-stable_mono_win64.exe",
    "args": ["--path", "$ZED_WORKTREE_ROOT/dev-game"],
    "allow_concurrent_runs": false,
    "reveal": "always",
  },
  {
    "label": "Godot: Run Game (Debug)",
    "command": "C:\\Users\\Administrateur.UTILISA-2J0T246\\Desktop\\godot\\Godot_v4.6.1-stable_mono_win64.exe",
    "args": ["--path", "$ZED_WORKTREE_ROOT/dev-game", "--verbose"],
    "allow_concurrent_runs": false,
    "reveal": "always",
  },
  {
    "label": "Godot: Open Editor",
    "command": "C:\\Users\\Administrateur.UTILISA-2J0T246\\Desktop\\godot\\Godot_v4.6.1-stable_mono_win64.exe",
    "args": ["--editor", "--path", "$ZED_WORKTREE_ROOT/dev-game"],
    "allow_concurrent_runs": false,
    "reveal": "never",
  },
  {
    "label": "C#: Build",
    "command": "dotnet",
    "args": ["build", "$ZED_WORKTREE_ROOT/dev-game/dev-game.csproj"],
    "allow_concurrent_runs": false,
    "reveal": "always",
  },
  {
    "label": "C#: Clean",
    "command": "dotnet",
    "args": ["clean", "$ZED_WORKTREE_ROOT/dev-game/dev-game.csproj"],
    "allow_concurrent_runs": false,
    "reveal": "always",
  },
]

```

```bash
# Rien encore
```
