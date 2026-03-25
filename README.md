# C61 — The Mountain
Projet synthèse dans le cadre du programme de technique de l'informatique du cégep du Vieux-Montréal.

## Membres de l'équipe

| Nom | Rôle principal |
|---|---|
| Samuel Bergeron-Lagacé | — |
| Jean-Marc Bouchard | — |

---

## Démarrage rapide

La source du projet Godot se situe dans `./dev-game/`. Pour démarrer sans configuration avancée :

1. Ouvrir Godot et charger le projet depuis `./dev-game/`
2. Ouvrir l'IDE à la **racine du dépôt** (pas dans `./dev-game/`)
3. Le projet est prêt à tester directement

---

## Configuration avancée (LSP / Autocomplétion)

Pour avoir l'autocomplétion et les diagnostics Roslyn dans Zed ou VS Code, il faut que l'IDE trouve l'exécutable Godot via la variable d'environnement `GODOT_BIN`.

**Windows (PowerShell) :**
```powershell
[Environment]::SetEnvironmentVariable("GODOT_BIN", "C:\DDJV\core\Godot_v4.6.1-stable_mono_win64.exe", "User")
```

**Linux / macOS (bash/zsh) :**
```bash
export GODOT_BIN="/usr/bin/godot/Godot_v4.6.1-stable_mono_linux_x86_64"
# Ajuster le chemin selon votre installation
```

**Linux (fish) :** Ajouter dans `~/.config/fish/config.fish` :
```fish
set -gx GODOT_BIN /path/to/godot
```

La config de l'IDE référence `$env(GODOT_BIN)`, donc ça fonctionne *out of the box* une fois la variable définie.

---

## Serveur

Pour tout ce qui concerne le serveur (lancement, connexion, architecture réseau, format des paquets), voir la documentation dédiée :

**[`dev-game/Config/SERVER.md`](dev-game/Config/SERVER.md)**

---

## Conventions de code

- Conventions standard **.NET** et **Godot 4 C#**
- Les méthodes `_Ready()`, `_Process()`, `_PhysicsProcess()` et autres overrides Godot sont **toujours placées à la fin du fichier**
- Toutes les méthodes publiques et utilitaires doivent avoir une documentation XML (`/// <summary>`)
- Préfixe `In` pour les paramètres d'entrée (ex: `InHeadXAngle`, `InTargetSkeleton`)

---

## Structure du projet

```
C61-The-Mountain/
├── dev-game/               ← Source Godot (ouvrir ici dans Godot)
│   ├── Core/
│   │   ├── Network/        ← NetworkManager, INetworkProvider, PlayerNetState
│   │   ├── UI/             ← HUD, menus
│   │   ├── Dev/            ← Debug overlay, scènes de test
│   │   └── World/
│   │       └── CharacterModel/
│   │           ├── Camera/ ← CameraMan
│   │           └── Physics/← PhysicsSkeleton (active ragdoll)
│   └── Assets/
└── README.md
```
