# Diagrammes UML - The Mountain

Cette arborescence regroupe les diagrammes PlantUML qui décrivent l'état réel
du projet (avril 2026). Les fichiers sont découpés par préoccupation pour
produire des schémas imprimables individuellement.

## Arborescence

```
diagrammes/
├── style.iuml                  # Styles partagés (à inclure dans chaque .puml)
├── classes/                    # Diagrammes de classes
├── etats/                      # Machines à états (state machines)
├── sequences/                  # Diagrammes de séquence
├── architecture/               # Diagrammes de composants / déploiement
├── CasUsages/                  # (existant) Cas d'usage
├── Classes/                    # (ancien) Diagrammes de classes dépassés
└── Objets Godot/               # (ancien) Vue scène Godot
```

## Classes (`classes/`)

| Fichier                   | Portée |
|---------------------------|--------|
| `auth.puml`               | Authentification : domaine, use case, Firebase, provider |
| `profile.puml`            | Profil joueur : Firestore, use case |
| `network_core.puml`       | `NetworkManager`, `INetworkProvider`, ENet, Steam (stub) |
| `network_state.puml`      | `PlayerNetState`, `PacketType`, enums partagés |
| `rooms.puml`              | `Room`, `RoomSnapshot`, `RoomRepository`, `LobbyState` |
| `character.puml`          | `Character`, `Player`, `PhysicsSkeleton`, `CameraMan` |
| `world_interaction.puml`  | Interactables, portes, fans, vents, éoliennes, spawner |
| `ui.puml`                 | Scènes UI (MainMenu, Login, Lobby, GameMenu, etc.) |
| `utils_config.puml`       | Enums globaux, `CharacterUtils`, `RayCastUtils`, `Env` |

## Machines à états (`etats/`)

| Fichier                   | Machine |
|---------------------------|---------|
| `character_fsm.puml`      | `CharacterState` (Idle, Moving, Airborne, Ragdoll, Recovering, Paused) |
| `movement_fsm.puml`       | `MovementState` (animation) |
| `emote_fsm.puml`          | `EmoteState` (Pointing, ArmsUp, ShowSign, None) |
| `camera_fsm.puml`         | `CameraType` (FP, TP, FreeMode, Death) |
| `wind_fsm.puml`           | `WindArea.WindState` (Normal, Aggressive) |
| `gamemenu_fsm.puml`       | Pause menu (Running ↔ Paused) |
| `lobby_status_fsm.puml`   | `Room.Status` (waiting, started, closed) |
| `network_role_fsm.puml`   | `NetworkRole` (None, Server, Client) |

## Séquences (`sequences/`)

| Fichier                       | Flux |
|-------------------------------|------|
| `auth_signin.puml`            | Connexion Firebase (Login) |
| `lobby_create_join.puml`      | Créer ou rejoindre le salon partagé "MAIN" |
| `lobby_start_game.puml`       | Démarrage de la partie (hôte + client) |
| `player_spawn.puml`           | RPC `ClientReady` + `MultiplayerSpawner` |
| `network_tick.puml`           | Tick 20 Hz : snapshot, validation, diffusion |
| `interaction.puml`            | Raycast d'interaction + `IActivatable` |
| `wind_cycle.puml`             | Cycle de vie d'une `WindArea` |

## Architecture (`architecture/`)

| Fichier                         | Vue |
|---------------------------------|-----|
| `overview.puml`                 | Paquets globaux + dépendances externes (Firebase / Godot) |
| `network_deployment.puml`       | Déploiement client/serveur + Firebase |
| `network_components.puml`       | Composants réseau internes |
| `character_components.puml`     | Composants de la scène `Player` |

## Conventions

- Les classes héritant d'un type Godot portent le stéréotype `<<Godot>>`.
- Les interfaces portent `<<Interface>>`, les statiques `<<Static>>`, les DTO
  `<<DTO>>`, les écrans UI `<<UI>>`.
- Les commentaires dans les fichiers sont minimaux et en français canadien.
- Tous les diagrammes incluent `../style.iuml` pour partager les skinparams.

## Générer les images

```bash
# rendu PNG de tous les diagrammes
plantuml -tpng -o rendu docs/diagrammes/**/*.puml

# rendu SVG pour l'impression vectorielle
plantuml -tsvg -o rendu docs/diagrammes/**/*.puml
```
