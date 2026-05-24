using System;
using Godot;

namespace Core.Network.Providers;

/// <summary>
/// Implémentation ENet du transport réseau. Doit être ajouté comme enfant
/// du <see cref="NetworkManager"/> avant utilisation, pour que <c>Multiplayer</c>
/// soit dans l'arbre de scène lors des appels à <see cref="StartServer"/> et <see cref="ConnectToServer"/>.
/// L'I/O de paquets bruts passe par <c>SceneMultiplayer</c> (SendBytes / signal PeerPacket),
/// qui est le type concret derrière <c>Node.Multiplayer</c> en Godot 4 standard.
/// </summary>
public partial class GodotENetProvider : Node, INetworkProvider
{
    private ENetMultiplayerPeer? _peer;
    private SceneMultiplayer? _sm;
    private NetworkRole _role = NetworkRole.None;

    /// <summary>Rôle actuel de ce pair dans la session.</summary>
    public NetworkRole Role => _role;

    /// <summary>Identifiant unique de ce pair attribué par ENet.</summary>
    public int LocalPeerId => Multiplayer.GetUniqueId();

    /// <summary><c>true</c> si le peer ENet est actif et un rôle est assigné.</summary>
    public bool IsRunning => _peer != null && _role != NetworkRole.None;

    /// <inheritdoc/>
    public event Action<int>? PeerConnected;
    /// <inheritdoc/>
    public event Action<int>? PeerDisconnected;
    /// <inheritdoc/>
    public event Action<int, byte[]>? PacketReceived;
    /// <inheritdoc/>
    public event Action? ServerStarted;
    /// <inheritdoc/>
    public event Action<string>? ConnectionFailed;
    /// <inheritdoc/>
    public event Action? ServerDisconnected;

    // Explicit interface impl so non-nullable interface contract is satisfied
    event Action<int> INetworkProvider.PeerConnected { add => PeerConnected += value; remove => PeerConnected -= value; }
    event Action<int> INetworkProvider.PeerDisconnected { add => PeerDisconnected += value; remove => PeerDisconnected -= value; }
    event Action<int, byte[]> INetworkProvider.PacketReceived { add => PacketReceived += value; remove => PacketReceived -= value; }
    event Action INetworkProvider.ServerStarted { add => ServerStarted += value; remove => ServerStarted -= value; }
    event Action<string> INetworkProvider.ConnectionFailed { add => ConnectionFailed += value; remove => ConnectionFailed -= value; }
    event Action INetworkProvider.ServerDisconnected { add => ServerDisconnected += value; remove => ServerDisconnected -= value; }

    /// <summary>
    /// Démarre un serveur ENet sur le port donné.
    /// </summary>
    /// <param name="port">Le port UDP d'écoute.</param>
    /// <param name="maxPeers">Le nombre maximum de clients simultanés.</param>
    public void StartServer(int port, int maxPeers)
    {
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateServer(port, maxPeers);
        if (err != Error.Ok)
        {
            ConnectionFailed?.Invoke($"Server creation failed: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        _role = NetworkRole.Server;
        ServerStarted?.Invoke();
    }

    /// <summary>
    /// Connecte ce client à un serveur ENet distant.
    /// </summary>
    /// <param name="address">L'adresse IP ou le nom de domaine du serveur.</param>
    /// <param name="port">Le port UDP du serveur.</param>
    public void ConnectToServer(string address, int port)
    {
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            ConnectionFailed?.Invoke($"Client connection failed: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        _role = NetworkRole.Client;
    }

    /// <summary>Ferme le peer ENet et réinitialise le rôle à <see cref="NetworkRole.None"/>.</summary>
    public void Disconnect()
    {
        _peer?.Close();
        _peer = null;
        Multiplayer.MultiplayerPeer = null;
        _role = NetworkRole.None;
    }

    /// <inheritdoc/>
    public void SendReliable(int toPeerId, byte[] data)
        => _sm?.SendBytes(data, toPeerId, MultiplayerPeer.TransferModeEnum.Reliable);

    /// <inheritdoc/>
    public void SendUnreliable(int toPeerId, byte[] data)
        => _sm?.SendBytes(data, toPeerId, MultiplayerPeer.TransferModeEnum.UnreliableOrdered);

    /// <inheritdoc/>
    public void BroadcastUnreliable(byte[] data, int excludePeerId = -1)
    {
        if (_sm == null) return;
        foreach (int id in Multiplayer.GetPeers())
        {
            if (id != excludePeerId)
                _sm.SendBytes(data, id, MultiplayerPeer.TransferModeEnum.UnreliableOrdered);
        }
    }

    /// <inheritdoc/>
    public void BroadcastReliable(byte[] data, int excludePeerId = -1)
    {
        if (_sm == null) return;
        foreach (int id in Multiplayer.GetPeers())
        {
            if (id != excludePeerId)
                _sm.SendBytes(data, id, MultiplayerPeer.TransferModeEnum.Reliable);
        }
    }

    public override void _Ready()
    {
        _sm = Multiplayer as SceneMultiplayer;
        if (_sm == null)
            GD.PrintErr("[GodotENetProvider] Multiplayer is not SceneMultiplayer — raw packets unavailable.");

        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        if (_sm != null) _sm.PeerPacket += OnPeerPacket;
    }

    public override void _ExitTree()
    {
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        if (_sm != null) _sm.PeerPacket -= OnPeerPacket;
    }

    private void OnConnectedToServer() => PeerConnected?.Invoke(Multiplayer.GetUniqueId());
    private void OnConnectionFailed() => ConnectionFailed?.Invoke("Connection to server failed");
    private void OnServerDisconnected() => ServerDisconnected?.Invoke();
    private void OnPeerConnected(long id)
    {
        // On clients, peer 1 is the server — already handled by ConnectedToServer; skip it.
        if (_role == NetworkRole.Client && id == 1) return;
        PeerConnected?.Invoke((int)id);
    }
    private void OnPeerDisconnected(long id) => PeerDisconnected?.Invoke((int)id);
    private void OnPeerPacket(long fromId, byte[] packet) => PacketReceived?.Invoke((int)fromId, packet);
}
