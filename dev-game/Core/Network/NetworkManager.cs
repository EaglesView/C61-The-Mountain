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
/// Le <c>World</c> utilise <c>MultiplayerSpawner</c> pour le spawn et s'abonne à
/// <see cref="StateReceived"/> pour mettre à jour les personnages distants.
/// </summary>
public partial class NetworkManager : Node
{
    /// <summary>Instance unique du singleton, disponible dès <c>_Ready</c>.</summary>
    public static NetworkManager Instance { get; private set; } = null!;

    private INetworkProvider? _provider;
    private Character?        _localPlayer;

    private float       _tickAccum  = 0f;
    private const float TickInterval = 1f / 20f;

    // Per-peer last known full state for server sanity check and newcomer catch-up
    private readonly Dictionary<int, PlayerNetState> _lastKnownState = new();

    /// <summary><c>true</c> si ce pair est le serveur de la session.</summary>
    public bool IsServer        => _provider?.Role == NetworkRole.Server;

    /// <summary><c>true</c> si ce pair est un client connecté à un serveur.</summary>
    public bool IsClient        => _provider?.Role == NetworkRole.Client;

    /// <summary><c>true</c> si le transport est actif et opérationnel.</summary>
    public bool IsRunning       => _provider?.IsRunning ?? false;

    /// <summary>Identifiant unique de ce pair dans la session ENet.</summary>
    public int  LocalPeerId     => _provider?.LocalPeerId ?? 1;

    /// <summary>
    /// Déclenché à la réception d'un <see cref="PlayerNetState"/> validé.
    /// Côté serveur, ce snapshot a déjà été relayé aux autres pairs avant d'être émis ici.
    /// </summary>
    public event Action<PlayerNetState>? StateReceived;

    /// <summary>Déclenché une seule fois lorsque la connexion locale au serveur est confirmée (client seulement).</summary>
    public event Action<int>?            LocalConnected;

    /// <summary>Déclenché si la connexion au serveur échoue.</summary>
    public event Action<string>?         ConnectionFailed;

    /// <summary>
    /// Enregistre le personnage local pour que le tick client puisse sérialiser son état.
    /// </summary>
    public void SetLocalPlayer(Character player) => _localPlayer = player;

    /// <summary>Démarre manuellement un serveur.</summary>
    public void StartServer(int port = 7777, int maxPeers = 16)
        => _provider?.StartServer(port, maxPeers);

    /// <summary>Connecte manuellement ce client à un serveur distant.</summary>
    public void ConnectToServer(string address, int port = 7777)
        => _provider?.ConnectToServer(address, port);

    public override void _Ready()
    {
        Instance = this;

        var enet = new GodotENetProvider();
        AddChild(enet);
        _provider = enet;

        _provider.PeerConnected    += OnProviderPeerConnected;
        _provider.PeerDisconnected += id =>
        {
            GD.Print($"[NetworkManager] Peer {id} disconnected (localPeerId={LocalPeerId}).");
            _lastKnownState.Remove(id);
        };
        _provider.PacketReceived   += OnPacketReceived;
        _provider.ServerStarted    += ()  => GD.Print("[NetworkManager] Server started on port 7777.");
        _provider.ConnectionFailed += msg =>
        {
            GD.PrintErr($"[NetworkManager] {msg}");
            ConnectionFailed?.Invoke(msg);
        };

        string[] args = OS.GetCmdlineArgs();

        bool isHeadless = DisplayServer.GetName() == "headless"
                       || OS.HasFeature("dedicated_server")
                       || args.Contains("--server");

        if (isHeadless)
        {
            GD.Print("[NetworkManager] Headless mode — starting dedicated server.");
            _provider.StartServer(7777, 16);
        }
    }

    private void OnProviderPeerConnected(int id)
    {
        if (_provider?.Role == NetworkRole.Server)
        {
            GD.Print($"[NetworkManager] Player {id} connected.");
            // Send each existing peer's last known state to the newcomer so their
            // ring buffer starts at the correct position (not 0,0,0)
            foreach (int existingId in Multiplayer.GetPeers())
            {
                if (existingId == id) continue;
                if (_lastKnownState.TryGetValue(existingId, out var existingState))
                    _provider.SendReliable(id, PlayerNetState.Serialize(PacketType.StateUpdate, existingState));
            }
        }
        else
        {
            GD.Print($"[NetworkManager] Connected to server. Local peer ID: {id}");
        }

        if (_provider?.Role == NetworkRole.Client && id == _provider.LocalPeerId)
            LocalConnected?.Invoke(id);
    }

    public override void _Process(double delta)
    {
        if (_provider?.Role != NetworkRole.Client || _localPlayer == null) return;
        if (!GodotObject.IsInstanceValid(_localPlayer)) { _localPlayer = null; return; }
        if (Multiplayer.MultiplayerPeer?.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;

        _tickAccum += (float)delta;
        if (_tickAccum < TickInterval) return;
        _tickAccum -= TickInterval;

        if (!_localPlayer.IsInsideTree()) { GD.Print("[NetworkManager] ERROR: _localPlayer not in tree!"); return; }
        var state  = _localPlayer.SnapshotState();
        var packet = PlayerNetState.Serialize(PacketType.StateUpdate, state);
        _provider.SendUnreliable(1, packet);
    }

    private void OnPacketReceived(int fromPeerId, byte[] data)
    {
        if (data.Length < 1) return;

        var type = (PacketType)data[0];

        if (type == PacketType.PositionCorrect)
        {
            if (_provider?.Role == NetworkRole.Client && data.Length >= 17 && _localPlayer != null)
            {
                int corrPeerId = System.BitConverter.ToInt32(data, 1);
                if (corrPeerId == _provider.LocalPeerId)
                {
                    var correctedPos = new Vector3(
                        System.BitConverter.ToSingle(data, 5),
                        System.BitConverter.ToSingle(data, 9),
                        System.BitConverter.ToSingle(data, 13)
                    );
                    GD.Print($"[NetworkManager] Server correction applied: {correctedPos}");
                    _localPlayer.GlobalPosition = correctedPos;
                    _localPlayer.Velocity = Vector3.Zero;
                }
            }
            return;
        }

        var (_, state) = PlayerNetState.Deserialize(data);

        if (_provider?.Role == NetworkRole.Server)
        {
            if (_lastKnownState.TryGetValue(fromPeerId, out var lastState))
            {
                float dist = lastState.Position.DistanceTo(state.Position);
                if (dist > 20f)
                {
                    GD.Print($"[NetworkManager] Dropped packet from {fromPeerId}: delta {dist:F1} > 20 — sending correction");
                    var correction = PlayerNetState.SerializeCorrection(fromPeerId, lastState.Position);
                    _provider.SendReliable(fromPeerId, correction);
                    return;
                }
            }
            _lastKnownState[fromPeerId] = state;
            _provider.BroadcastUnreliable(data, excludePeerId: fromPeerId);
            StateReceived?.Invoke(state);
        }
        else
        {
            StateReceived?.Invoke(state);
        }
    }
}
