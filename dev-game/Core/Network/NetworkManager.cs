using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Core.Network.Providers;

namespace Core.Network;

/// <summary>
/// Singleton autoload qui orchestre toute la couche réseau.
/// Possède le transport actif (<see cref="INetworkProvider"/>), envoie les snapshots
/// client à 20 Hz, et relaie les états reçus aux autres pairs côté serveur.
/// Le <c>World</c> s'abonne à <see cref="PeerJoined"/>, <see cref="PeerLeft"/>
/// et <see cref="StateReceived"/> pour gérer le spawn et la mise à jour des personnages.
/// </summary>
public partial class NetworkManager : Node
{
    /// <summary>Instance unique du singleton, disponible dès <c>_Ready</c>.</summary>
    public static NetworkManager Instance { get; private set; } = null!;

    private INetworkProvider? _provider;
    private Character?        _localPlayer;

    private readonly HashSet<int> _remotePeerIds = new();

    /// <summary>Ensemble des peer IDs distants actuellement connus (reçus via SpawnReq ou connexion directe).</summary>
    public IReadOnlyCollection<int> RemotePeerIds => _remotePeerIds;

    private float       _tickAccum  = 0f;
    private const float TickInterval = 1f / 20f; // 50ms

    // Per-peer last known position for server-side sanity check (20 units/tick max)
    private readonly Dictionary<int, Vector3> _lastKnownPos = new();

    /// <summary><c>true</c> si ce pair est le serveur de la session.</summary>
    public bool IsServer        => _provider?.Role == NetworkRole.Server;

    /// <summary><c>true</c> si ce pair est un client connecté à un serveur.</summary>
    public bool IsClient        => _provider?.Role == NetworkRole.Client;

    /// <summary><c>true</c> si le transport est actif et opérationnel.</summary>
    public bool IsRunning       => _provider?.IsRunning ?? false;

    /// <summary>Identifiant unique de ce pair dans la session ENet.</summary>
    public int  LocalPeerId     => _provider?.LocalPeerId ?? 1;

    /// <summary>
    /// <c>true</c> si une connexion automatique via <c>--connect</c> est en cours au démarrage.
    /// </summary>
    public bool IsAutoConnecting { get; private set; }

    /// <summary>Déclenché lorsqu'un nouveau pair rejoint la session. Paramètre : identifiant du pair.</summary>
    public event Action<int>?            PeerJoined;

    /// <summary>Déclenché lorsqu'un pair quitte la session. Paramètre : identifiant du pair.</summary>
    public event Action<int>?            PeerLeft;

    /// <summary>
    /// Déclenché à la réception d'un <see cref="PlayerNetState"/> validé.
    /// Côté serveur, ce snapshot a déjà été relayé aux autres pairs avant d'être émis ici.
    /// </summary>
    public event Action<PlayerNetState>? StateReceived;

    /// <summary>Déclenché une seule fois lorsque la connexion locale au serveur est confirmée (client seulement).</summary>
    public event Action?                 LocalConnected;

    /// <summary>
    /// Enregistre le personnage local pour que le tick client puisse sérialiser son état.
    /// Doit être appelé par le <c>World</c> après avoir instancié le joueur local.
    /// </summary>
    /// <param name="player">Le personnage contrôlé par ce client.</param>
    public void SetLocalPlayer(Character player) => _localPlayer = player;

    /// <summary>
    /// Démarre manuellement un serveur sur le port et le nombre de pairs donnés.
    /// </summary>
    /// <param name="port">Le port UDP d'écoute. Par défaut <c>7777</c>.</param>
    /// <param name="maxPeers">Le nombre maximum de clients simultanés. Par défaut <c>16</c>.</param>
    public void StartServer(int port = 7777, int maxPeers = 16)
        => _provider?.StartServer(port, maxPeers);

    /// <summary>
    /// Connecte manuellement ce client à un serveur distant.
    /// </summary>
    /// <param name="address">L'adresse IP ou le nom de domaine du serveur.</param>
    /// <param name="port">Le port UDP du serveur. Par défaut <c>7777</c>.</param>
    public void ConnectToServer(string address, int port = 7777)
        => _provider?.ConnectToServer(address, port);

    public override void _Ready()
    {
        Instance = this;

        var enet = new GodotENetProvider();
        AddChild(enet);
        _provider = enet;

        _provider.PeerConnected    += OnProviderPeerConnected;
        _provider.PeerDisconnected += id  =>
        {
            GD.Print($"[NetworkManager] Peer {id} disconnected. Active peers: {_lastKnownPos.Count - 1}");
            _lastKnownPos.Remove(id);

            // Notify all remaining clients that this peer is gone
            if (_provider?.Role == NetworkRole.Server)
            {
                var notify = PlayerNetState.SerializePeerNotify(PacketType.DespawnNotify, id);
                _provider.BroadcastReliable(notify);
            }

            _remotePeerIds.Remove(id);
            PeerLeft?.Invoke(id);
        };
        _provider.PacketReceived   += OnPacketReceived;
        _provider.ServerStarted    += ()  => GD.Print("[NetworkManager] Server started on port 7777.");
        _provider.ConnectionFailed += msg => GD.PrintErr($"[NetworkManager] {msg}");

        string[] args = OS.GetCmdlineArgs();

        bool isHeadless = DisplayServer.GetName() == "headless"
                       || OS.HasFeature("dedicated_server")
                       || args.Contains("--server");

        if (isHeadless)
        {
            GD.Print("[NetworkManager] Headless mode — starting dedicated server.");
            _provider.StartServer(7777, 16);
            return;
        }

        // --connect or --connect=<address>
        string? connectArg = System.Array.Find(args, a => a == "--connect" || a.StartsWith("--connect="));
        if (connectArg != null)
        {
            IsAutoConnecting = true;
            string address = connectArg.Contains('=') ? connectArg.Split('=')[1] : "127.0.0.1";
            GD.Print($"[NetworkManager] Auto-connecting to {address}:7777");
            _provider.ConnectToServer(address, 7777);
        }
    }

    private void OnProviderPeerConnected(int id)
    {
        if (_provider?.Role == NetworkRole.Server)
        {
            GD.Print($"[NetworkManager] Player {id} connected. Active peers: {Multiplayer.GetPeers().Length}");

            // Tell all already-connected clients about the newcomer
            var newNotify = PlayerNetState.SerializePeerNotify(PacketType.SpawnReq, id);
            _provider.BroadcastReliable(newNotify, excludePeerId: id);

            // Tell the newcomer about every peer already in the session
            foreach (int existingId in Multiplayer.GetPeers())
            {
                if (existingId == id) continue;
                var existingNotify = PlayerNetState.SerializePeerNotify(PacketType.SpawnReq, existingId);
                _provider.SendReliable(id, existingNotify);
            }
        }
        else
        {
            GD.Print($"[NetworkManager] Connected to server. Local peer ID: {id}");
        }

        PeerJoined?.Invoke(id);
        _remotePeerIds.Add(id);
        // Fire LocalConnected when our own client connection is confirmed
        if (_provider?.Role == NetworkRole.Client && id == _provider.LocalPeerId)
            LocalConnected?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (_provider?.Role != NetworkRole.Client || _localPlayer == null) return;

        _tickAccum += (float)delta;
        if (_tickAccum < TickInterval) return;
        _tickAccum -= TickInterval;

        var state  = _localPlayer.SnapshotState();
        var packet = PlayerNetState.Serialize(PacketType.StateUpdate, state);
        _provider.SendUnreliable(1, packet); // peer 1 = server in ENet
    }

    private void OnPacketReceived(int fromPeerId, byte[] data)
    {
        if (data.Length < 1) return;

        var type = (PacketType)data[0];

        // Lightweight peer-notify packets (5 bytes): relay PeerJoined/PeerLeft on clients
        if (type == PacketType.SpawnReq || type == PacketType.DespawnNotify)
        {
            if (_provider?.Role == NetworkRole.Client && data.Length >= 5)
            {
                int peerId = System.BitConverter.ToInt32(data, 1);
                GD.Print($"[NetworkManager] Got {type} for peer {peerId}");
                if (type == PacketType.SpawnReq)
                {
                    _remotePeerIds.Add(peerId);
                    PeerJoined?.Invoke(peerId);
                }
                else
                {
                    _remotePeerIds.Remove(peerId);
                    PeerLeft?.Invoke(peerId);
                }
            }
            return;
        }

        var (_, state) = PlayerNetState.Deserialize(data);

        if (_provider?.Role == NetworkRole.Server)
        {
            // Sanity check: reject teleports > 20 units per tick
            if (_lastKnownPos.TryGetValue(fromPeerId, out var lastPos))
            {
                float dist = lastPos.DistanceTo(state.Position);
                if (dist > 20f)
                {
                    GD.Print($"[NetworkManager] Dropped packet from {fromPeerId}: delta {dist:F1} > 20");
                    return;
                }
            }
            _lastKnownPos[fromPeerId] = state.Position;

            // Relay to all other peers
            _provider.BroadcastUnreliable(data, excludePeerId: fromPeerId);
            StateReceived?.Invoke(state);
        }
        else
        {
            StateReceived?.Invoke(state);
        }
    }
}
