using System;
using Godot;

namespace Core.Network.Providers;

/// <summary>
/// ENet transport. Must be added as a child of NetworkManager before use
/// so Multiplayer is in-tree when StartServer/ConnectToServer are called.
/// Raw packet I/O uses SceneMultiplayer (SendBytes / PeerPacket signal),
/// which is the concrete type behind Node.Multiplayer in standard Godot 4.
/// </summary>
public partial class GodotENetProvider : Node, INetworkProvider
{
    private ENetMultiplayerPeer?  _peer;
    private SceneMultiplayer?     _sm;
    private NetworkRole           _role = NetworkRole.None;

    public NetworkRole Role     => _role;
    public int LocalPeerId      => Multiplayer.GetUniqueId();
    public bool IsRunning       => _peer != null && _role != NetworkRole.None;

    public event Action<int>?         PeerConnected;
    public event Action<int>?         PeerDisconnected;
    public event Action<int, byte[]>? PacketReceived;
    public event Action?              ServerStarted;
    public event Action<string>?      ConnectionFailed;

    // Explicit interface impl so non-nullable interface contract is satisfied
    event Action<int>         INetworkProvider.PeerConnected    { add => PeerConnected    += value; remove => PeerConnected    -= value; }
    event Action<int>         INetworkProvider.PeerDisconnected { add => PeerDisconnected += value; remove => PeerDisconnected -= value; }
    event Action<int, byte[]> INetworkProvider.PacketReceived   { add => PacketReceived   += value; remove => PacketReceived   -= value; }
    event Action              INetworkProvider.ServerStarted    { add => ServerStarted    += value; remove => ServerStarted    -= value; }
    event Action<string>      INetworkProvider.ConnectionFailed { add => ConnectionFailed += value; remove => ConnectionFailed -= value; }

    public override void _Ready()
    {
        _sm = Multiplayer as SceneMultiplayer;
        if (_sm == null)
            GD.PrintErr("[GodotENetProvider] Multiplayer is not SceneMultiplayer — raw packets unavailable.");

        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed  += OnConnectionFailed;
        Multiplayer.PeerConnected     += OnPeerConnected;
        Multiplayer.PeerDisconnected  += OnPeerDisconnected;
        if (_sm != null) _sm.PeerPacket += OnPeerPacket;
    }

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

    public void Disconnect()
    {
        _peer?.Close();
        _peer = null;
        _role = NetworkRole.None;
    }

    public void SendReliable(int toPeerId, byte[] data)
        => _sm?.SendBytes(data, toPeerId, MultiplayerPeer.TransferModeEnum.Reliable);

    public void SendUnreliable(int toPeerId, byte[] data)
        => _sm?.SendBytes(data, toPeerId, MultiplayerPeer.TransferModeEnum.UnreliableOrdered);

    public void BroadcastUnreliable(byte[] data, int excludePeerId = -1)
    {
        if (_sm == null) return;
        foreach (int id in Multiplayer.GetPeers())
        {
            if (id != excludePeerId)
                _sm.SendBytes(data, id, MultiplayerPeer.TransferModeEnum.UnreliableOrdered);
        }
    }

    public void BroadcastReliable(byte[] data, int excludePeerId = -1)
    {
        if (_sm == null) return;
        foreach (int id in Multiplayer.GetPeers())
        {
            if (id != excludePeerId)
                _sm.SendBytes(data, id, MultiplayerPeer.TransferModeEnum.Reliable);
        }
    }

    public override void _ExitTree()
    {
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed  -= OnConnectionFailed;
        Multiplayer.PeerConnected     -= OnPeerConnected;
        Multiplayer.PeerDisconnected  -= OnPeerDisconnected;
        if (_sm != null) _sm.PeerPacket -= OnPeerPacket;
    }

    private void OnConnectedToServer()       => PeerConnected?.Invoke(Multiplayer.GetUniqueId());
    private void OnConnectionFailed()        => ConnectionFailed?.Invoke("Connection to server failed");
    private void OnPeerConnected(long id)    => PeerConnected?.Invoke((int)id);
    private void OnPeerDisconnected(long id) => PeerDisconnected?.Invoke((int)id);
    private void OnPeerPacket(long fromId, byte[] packet) => PacketReceived?.Invoke((int)fromId, packet);
}
