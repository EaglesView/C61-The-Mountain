using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Core.Network.Providers;

namespace Core.Network;

/// <summary>
/// Autoload singleton. Owns the transport provider, runs 20Hz client tick,
/// relays state on the server. World subscribes to PeerJoined/Left/StateReceived.
/// </summary>
public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    private INetworkProvider? _provider;
    private Character?        _localPlayer;

    private float       _tickAccum  = 0f;
    private const float TickInterval = 1f / 20f; // 50ms

    // Per-peer last known position for server-side sanity check (20 units/tick max)
    private readonly Dictionary<int, Vector3> _lastKnownPos = new();

    public bool IsServer        => _provider?.Role == NetworkRole.Server;
    public bool IsClient        => _provider?.Role == NetworkRole.Client;
    public bool IsRunning       => _provider?.IsRunning ?? false;
    public int  LocalPeerId     => _provider?.LocalPeerId ?? 1;
    public bool IsAutoConnecting { get; private set; }

    public event Action<int>?            PeerJoined;
    public event Action<int>?            PeerLeft;
    public event Action<PlayerNetState>? StateReceived;
    /// <summary>Fires once when our own client connection to the server is confirmed.</summary>
    public event Action?                 LocalConnected;

    public override void _Ready()
    {
        Instance = this;

        var enet = new GodotENetProvider();
        AddChild(enet);
        _provider = enet;

        _provider.PeerConnected    += OnProviderPeerConnected;
        _provider.PeerDisconnected += id  => { _lastKnownPos.Remove(id); PeerLeft?.Invoke(id); };
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
        PeerJoined?.Invoke(id);
        // Fire LocalConnected when our own client connection is confirmed
        if (_provider?.Role == NetworkRole.Client && id == _provider.LocalPeerId)
            LocalConnected?.Invoke();
    }

    /// <summary>Called by World after spawning the local Player.</summary>
    public void SetLocalPlayer(Character player) => _localPlayer = player;

    public void StartServer(int port = 7777, int maxPeers = 16)
        => _provider?.StartServer(port, maxPeers);

    public void ConnectToServer(string address, int port = 7777)
        => _provider?.ConnectToServer(address, port);

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

        var (type, state) = PlayerNetState.Deserialize(data);

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
