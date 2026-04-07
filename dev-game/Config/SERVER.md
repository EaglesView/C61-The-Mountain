# The Mountain — Documentation Serveur

## Aperçu

Le serveur utilise **ENet** comme transport (via `GodotENetProvider`), orchestré par le singleton autoload `NetworkManager`. Il peut tourner en mode dédié headless ou être lancé manuellement via des arguments de ligne de commande.

---

## Lancer le serveur

### Mode headless (recommandé pour production)

```bash
./the-mountain.x86_64 --headless
```

Le moteur Godot démarre sans fenêtre ni rendu. `NetworkManager` détecte automatiquement ce mode via `DisplayServer.GetName() == "headless"` ou `OS.HasFeature("dedicated_server")`.

### Depuis un export normal ou l'éditeur

```bash
./the-mountain.x86_64 --server
```

Même comportement que headless, mais avec la fenêtre Godot ouverte. Utile pour déboguer le serveur localement.

---

## Se connecter

```bash
# Connexion à localhost (127.0.0.1)
./the-mountain.x86_64 --connect

# Connexion à une adresse spécifique
./the-mountain.x86_64 --connect=192.168.1.42
./the-mountain.x86_64 --connect=monserveur.example.com
```

---

## Paramètres réseau

| Paramètre | Valeur |
|---|---|
| Port | `7777` (UDP) |
| Max joueurs | `16` |
| Tick rate client | 20 Hz (50 ms/snapshot) |
| Transport | ENet (`UnreliableOrdered` pour les états, `Reliable` pour spawn/despawn) |
| Peer ID serveur | `1` (convention ENet Godot) |

---

## Sécurité et validation

Le serveur applique une validation minimale côté serveur sur chaque paquet reçu :

- **Anti-téléportation :** tout paquet dont le déplacement dépasse **20 unités par tick** est rejeté silencieusement
- Les paquets invalides (trop courts, malformés) sont ignorés sans déconnecter le pair

Le serveur relaie ensuite les paquets validés à tous les autres pairs (`BroadcastUnreliable`, en excluant l'émetteur).

---

## Format des paquets (`PlayerNetState`)

Paquet binaire de **52 octets**, sérialisé avec `BinaryWriter` en little-endian :

| Offset | Champ | Type | Octets | Notes |
|---|---|---|---|---|
| 0 | `PacketType` | `byte` | 1 | `0x01` = StateUpdate, `0x02` = SpawnReq, `0x03` = DespawnNotify |
| 1 | `PeerId` | `int32` | 4 | ID unique du pair ENet |
| 5 | `Position` | `Vector3` | 12 | XYZ en `float32` |
| 17 | `Velocity` | `Vector3` | 12 | XYZ en `float32` |
| 29 | `BodyYaw` | `float32` | 4 | Rotation Y du corps (radians) |
| 33 | `HeadPitch` | `float32` | 4 | Angle X de la tête (radians) |
| 37 | `ArmPointDir` | `Vector3` | 12 | Direction de pointage du bras (world space) |
| 49 | `MoveState` | `byte` | 1 | Enum `MovementState` |
| 50 | `EmoteState` | `byte` | 1 | Enum `EmoteState` |
| 51 | `Flags` | `byte` | 1 | Bit 0 = Aiming, Bit 1 = ArmsUp |

---

## Architecture — couche réseau

```
NetworkManager  (autoload singleton, Node)
 └─ GodotENetProvider  (INetworkProvider, Node enfant)
```

`INetworkProvider` abstrait le transport. `GodotENetProvider` est l'implémentation ENet actuelle. Un provider Steam (`GodotSteamProvider`) existe également mais n'est pas encore actif.

### Flux de données (client → serveur → autres clients)

```
[Client]  _Process() 20Hz
    └─ SnapshotState() → PlayerNetState.Serialize()
        └─ SendUnreliable(peerId=1, data)
            └─ [Serveur] OnPacketReceived()
                ├─ Validation anti-téléportation
                ├─ BroadcastUnreliable(data, excludePeerId=émetteur)
                └─ StateReceived?.Invoke(state) → World.ApplyNetworkState()
```

---

## Événements exposés par `NetworkManager`

| Événement | Signature | Description |
|---|---|---|
| `PeerJoined` | `Action<int>` | Un pair vient de se connecter |
| `PeerLeft` | `Action<int>` | Un pair vient de se déconnecter |
| `StateReceived` | `Action<PlayerNetState>` | Un snapshot d'état joueur a été reçu et validé |
| `LocalConnected` | `Action` | La connexion locale au serveur est confirmée (client seulement) |
