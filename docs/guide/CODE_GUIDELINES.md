# Guidelines de code

## Introduction

On veut du beau code, on veut également pouvoir franchir des limites de temps donné. C'est pourquoi ce guide a été créé, permettant d'aider n'importe quel collaborateur (humain ou IA) à uniformiser la base de code.

Nous utilisons, dans `dev-game.csproj`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>` qui génère des avertissements pour les manques à la documentation. Cela veut dire que toute variable ou méthode **publique** sans documentation XML lancera un warning au compilateur.

---

## Bases C\#

### Header de fichier

Pour la propreté de chaque fichier créé, un header devra être placé dans chaque fichier ayant une importance dans le projet (Player, Networking, UI, etc.).

```csharp
/*
+=============================================================+
|    _____ _          __  __              _        _          |
|   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
|     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
|     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
|                                                             |
|  ---------------------------------------------------------  |
|  Fichier:               Fichier.cs                          |
|  Auteur:                Auteur                              |
|  Fonction: Fonction du fichier en question                  |
|  ---------------------------------------------------------  |
|                                                             |
|                                                             |
|                                                             |
|                                                             |
+==============================================================+
*/
```

### Conventions de nommage

| Élément | Convention | Exemple |
|---|---|---|
| Classe | `PascalCase` | `PlayerSpawner` |
| Méthode publique | `PascalCase` | `SpawnPlayer()` |
| Méthode privée | `PascalCase` | `GetNextSpawnPoint()` |
| Champ privé | `_camelCase` | `_playerCamera` |
| Propriété publique | `PascalCase` | `CamType` |
| Paramètre de méthode | `InPascalCase` | `InPeerId`, `InCamType` |
| Variable locale | `camelCase` | `headPos`, `spawnPos` |
| Constante | `SCREAMING_SNAKE_CASE` | `MAX_PEERS` |
| Enum | `PascalCase` (type et membres) | `CameraType.FirstPerson` |

> Le préfixe `In` sur les paramètres signale explicitement à l'appelant que la valeur est une entrée pure — elle ne sera pas modifiée par la méthode.

### Ordre des membres dans une classe

```
1. Champs [Export] (groupés par ExportGroup)
2. Champs privés
3. Propriétés publiques
4. Méthodes publiques
5. Méthodes privées
6. Méthodes de lifecycle Godot, dans cet ordre :
   _Ready() → _Input() → _PhysicsProcess() → _Process()
```

### Mottos de programmation

- Si une fonction peut être statique et réutilisable, la déplacer dans un fichier utilitaire dans `dev-game/Core/Utils/`.
- Les méthodes de lifecycle Godot (`_Ready`, `_Input`, `_PhysicsProcess`, `_Process`) sont **toujours à la fin** du fichier, dans l'ordre indiqué ci-dessus.
- Préférer des méthodes courtes et ciblées. Si une méthode dépasse ~30 lignes, envisager de la décomposer.
- Ne pas laisser de code commenté dans le dépôt. Utiliser `git` pour récupérer l'historique si nécessaire.

---

## Documentation XML

Toute la documentation XML est rédigée en **français canadien**.

### Membres publics — obligatoire

```csharp
/// <summary>
/// Spawne un personnage pour le peer donné.
/// </summary>
/// <param name="InPeerId">L'identifiant ENet du pair à spawner.</param>
/// <param name="InIsLocal"><c>true</c> pour le joueur local.</param>
/// <returns>Le personnage instancié, ou <c>null</c> si le peerId est déjà spawné.</returns>
public Character? SpawnPlayer(int InPeerId, bool InIsLocal) { ... }
```

### Membres privés — optionnel mais encouragé

Pour les champs privés non triviaux, utiliser un `<summary>` court. Préfixer avec `PRIVÉ -` pour distinguer visuellement dans les survols d'IDE :

```csharp
/// <summary>PRIVÉ - Distance de la caméra TP en unités Godot.</summary>
[Export] private float _cameraDistanceTP = 10.0f;
```

### Héritage d'interface

Utiliser `/// <inheritdoc/>` sur les implémentations d'interface pour éviter la duplication :

```csharp
/// <inheritdoc/>
public void Connect(string InAddress, int InPort) { ... }
```

### Balises utiles

| Balise | Usage |
|---|---|
| `<summary>` | Description principale |
| `<param name="...">` | Description d'un paramètre |
| `<returns>` | Valeur de retour |
| `<remarks>` | Notes supplémentaires, contexte architectural |
| `<see cref="...">` | Référence vers un autre type ou membre |
| `<c>...</c>` | Code inline (`true`, `null`, nom de méthode) |
| `<para>` | Paragraphe à l'intérieur d'un `<summary>` long |

---

## Godot

### Exports

Grouper les exports par responsabilité avec `[ExportGroup]` et `[ExportSubgroup]` :

```csharp
[ExportGroup("Nodes")]
[Export] private Camera3D _playerCamera;
[Export] private Player _player;

[ExportGroup("Camera Properties")]
[ExportSubgroup("First Person")]
[Export(PropertyHint.Range, "0.0f,50.0f,1.0f")] private float _camDamping = 0.2f;
```

Toujours fournir des `PropertyHint.Range` sur les floats/ints exposés pour éviter les valeurs accidentelles dans l'Inspector.

### Scènes et nodes

- Un `.tscn` par concept fonctionnel. Ne pas imbriquer des scènes non reliées.
- Les scripts sont attachés à la racine de leur scène respective.
- Les `node_paths` utilisés dans les scripts sont toujours exposés via `[Export]` — ne pas utiliser `GetNode("chemin/hardcodé")` dans le code de production.

### Lifecycle et ordre d'exécution

Godot exécute les frames dans l'ordre suivant : `_PhysicsProcess` → `_Process`. En conséquence :
- Lire les données physiques (positions de bones ragdoll, collisions) dans `_Process`, pas `_PhysicsProcess`, pour obtenir les valeurs finalisées de la frame.
- Appliquer les forces ou modifier `Velocity` dans `_PhysicsProcess`.

---

## Réseau (ENet)

Le réseau utilise un système de paquets binaires bruts via ENet — **pas** les RPCs haut niveau de Godot.

### Règles d'architecture

- **Le serveur a autorité.** Aucun client ne doit spawner, despawner ou valider sa propre position.
- Les paquets entrants du client sont considérés non fiables. Toujours valider côté serveur (ex. : vérification anti-téléportation dans `NetworkManager`).
- Les nouvelles actions réseau passent par un nouveau `PacketType` dans `PlayerNetState.cs`.

### Format des paquets

Documenter chaque champ de paquet avec son offset et sa taille dans les commentaires XML de `PlayerNetState`. Voir `SERVER.md` dans `dev-game/Config/` pour la table complète des offsets.

### Nommage des événements réseau

Les événements du `NetworkManager` suivent la convention `On + Sujet + Action` :

```csharp
public event Action<int> OnPeerConnected;
public event Action<int> OnPeerDisconnected;
public event Action<PlayerNetState> OnStateReceived;
```
