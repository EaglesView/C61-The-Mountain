# The Mountain — Documentation Serveur

## Aperçu

Le serveur utilise **ENet** comme transport (via `GodotENetProvider`), orchestré par le singleton autoload `NetworkManager`. Il tourne en mode dédié headless sur le VPS. La connexion des clients passe par le lobby Firebase (matchmaking) puis par un handshake RPC une fois la scène World chargée.

---

## Lancer le serveur

### Mode headless (production — VPS)

```bash
./the-mountain.x86_64 --headless
```

Godot démarre sans fenêtre ni rendu. `NetworkManager` détecte ce mode via `DisplayServer.GetName() == "headless"` ou `OS.HasFeature("dedicated_server")`.

### Depuis un export normal ou l'éditeur (debug local)

```bash
./the-mountain.x86_64 --server
```

Même comportement que headless, mais avec la fenêtre Godot ouverte.

---

## Flux de connexion client

1. Client se connecte via le lobby Firebase (menu principal → « Play Online »)
2. Le lobby stocke l'IP du serveur (`SERVER_IP` env var, fallback `Room.HardcodedServerIp`)
3. Quand la partie démarre depuis le lobby, `LobbyScene` appelle `NetworkManager.ConnectToServer(ip, 7777)`
4. Sur connexion confirmée (`LocalConnected`), la scène `world.tscn` est chargée
5. `World._Ready()` envoie un RPC `ClientReady` au serveur
6. Le serveur reçoit `ClientReady`, appelle `ServerSpawnPeer(peerId)` → `MultiplayerSpawner.Spawn()`
7. Le spawn est répliqué à tous les pairs connectés

---

## Paramètres réseau

| Paramètre | Valeur |
|---|---|
| Port | `7777` (UDP) |
| Max joueurs | `16` |
| Tick rate client | 20 Hz (50 ms/snapshot) |
| Transport | ENet (`UnreliableOrdered` pour les états, `Reliable` pour spawn/ClientReady) |
| Peer ID serveur | `1` (convention ENet Godot) |

---

## Sécurité et validation

- **Anti-téléportation :** tout paquet dont le déplacement dépasse **20 unités par tick** est rejeté et une correction est renvoyée au client fautif (`PositionCorrect`)
- **Guard ClientReady :** le RPC de spawn vérifie `Multiplayer.IsServer()` et `GetRemoteSenderId()` — ne peut pas être usurpé par un autre client
- Les paquets invalides (trop courts, malformés) sont ignorés sans déconnecter le pair

---

## Format des paquets (`PlayerNetState`)

Paquet binaire de **52 octets**, sérialisé avec `BinaryWriter` en little-endian.

### Mode normal (hors ragdoll)

| Offset | Champ | Type | Octets | Notes |
|---|---|---|---|---|
| 0 | `PacketType` | `byte` | 1 | `0x01` = StateUpdate, `0x04` = PositionCorrect |
| 1 | `PeerId` | `int32` | 4 | ID unique du pair ENet |
| 5 | `Position` | `Vector3` | 12 | Position monde du `CharacterBody3D` |
| 17 | `Velocity` | `Vector3` | 12 | Vélocité du `CharacterBody3D` |
| 29 | `BodyYaw` | `float32` | 4 | Rotation Y du corps (radians) |
| 33 | `HeadPitch` | `float32` | 4 | Angle X de la tête relatif au corps (radians) |
| 37 | `ArmPointDir` | `Vector3` | 12 | Direction de pointage du bras (world space) |
| 49 | `MoveState` | `byte` | 1 | Enum `MovementState` |
| 50 | `EmoteState` | `byte` | 1 | Enum `EmoteState` |
| 51 | `Flags` | `byte` | 1 | Bit 0 = Aiming, Bit 1 = ArmsUp, Bit 2 = Recovering |

### Mode ragdoll (`MoveState == Ragdolling && !Recovering`)

Les champs `Position`, `Velocity`, `BodyYaw` et `HeadPitch` sont réutilisés pour transmettre les données du squelette physique. La taille du paquet reste **52 octets**.

| Offset | Champ réutilisé | Contenu réel |
|---|---|---|
| 5 | `Position` | Position monde de l'os physique `Spine.001` |
| 17 | `Velocity` | Vélocité linéaire de l'os physique `Spine.001` |
| 29 | `BodyYaw` | Yaw monde de l'os physique `Head.001` (radians) |
| 33 | `HeadPitch` | Pitch monde de l'os physique `Head.001` (radians) |

Les clients distants utilisent ces données pour :
- Ancrer leur simulation ragdoll locale à la position autoritaire de la colonne
- Appliquer une impulsion initiale sur tous les os au démarrage du ragdoll
- Corriger doucement la position de la colonne et l'orientation de la tête chaque tick (ressort faible, `SpineCorrectK = 15`, `HeadCorrectK = 8`)

### Flag `Recovering` (bit 2 de `Flags`)

Levé pendant `CharacterState.Recovering` (après ragdoll, avant que le joueur reprenne le contrôle). Les clients distants utilisent ce flag pour quitter le ragdoll local et interpoler vers la position de récupération reçue.

---

## Architecture — couche réseau

```
NetworkManager  (autoload singleton, Node)
 └─ GodotENetProvider  (INetworkProvider, Node enfant)
```

`INetworkProvider` abstrait le transport. `GodotENetProvider` est l'implémentation ENet. Un provider Steam (`GodotSteamProvider`) existe mais n'est pas encore actif.

### Flux de données (client → serveur → autres clients)

```
[Client]  _Process() 20Hz
    └─ Character.SnapshotState() → PlayerNetState.Serialize()
        └─ NetworkManager.SendUnreliable(peerId=1, data)
            └─ [Serveur] OnPacketReceived()
                ├─ Validation anti-téléportation (hors ragdoll)
                ├─ BroadcastUnreliable(data, excludePeerId=émetteur)
                └─ StateReceived?.Invoke(state)
                    └─ World.OnStateReceived()
                        └─ Player.PushSnapshot() → RemotePhysicsProcess()
```

### Flux de spawn

```
[Client] World._Ready()
    └─ Rpc("ClientReady") → [Serveur]
        └─ World.ClientReady()
            └─ ServerSpawnPeer(peerId)
                └─ MultiplayerSpawner.Spawn(data)
                    └─ [Tous les pairs] SpawnPlayerNode(data)
                        └─ Player._Ready() → IsMultiplayerAuthority() ?
                            ├─ true  → SetLocalPlayer, caméra, inputs
                            └─ false → RemotePhysicsProcess, correction ragdoll
```

---

## Événements exposés par `NetworkManager`

| Événement | Signature | Description |
|---|---|---|
| `StateReceived` | `Action<PlayerNetState>` | Snapshot d'état joueur reçu et validé |
| `LocalConnected` | `Action<int>` | Connexion locale au serveur confirmée (client seulement) |
| `ConnectionFailed` | `Action<string>` | Échec de connexion au serveur |
