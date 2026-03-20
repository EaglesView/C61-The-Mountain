using System;

namespace Core.Network.Providers;

/// <summary>
/// Steam transport stub. Compiles but throws everywhere — implement when GodotSteam is integrated.
/// </summary>
public class GodotSteamProvider : INetworkProvider
{
    public NetworkRole Role  => throw new NotImplementedException();
    public int LocalPeerId   => throw new NotImplementedException();
    public bool IsRunning    => throw new NotImplementedException();

    public event Action<int>?         PeerConnected;
    public event Action<int>?         PeerDisconnected;
    public event Action<int, byte[]>? PacketReceived;
    public event Action?              ServerStarted;
    public event Action<string>?      ConnectionFailed;

    event Action<int>         INetworkProvider.PeerConnected    { add => PeerConnected    += value; remove => PeerConnected    -= value; }
    event Action<int>         INetworkProvider.PeerDisconnected { add => PeerDisconnected += value; remove => PeerDisconnected -= value; }
    event Action<int, byte[]> INetworkProvider.PacketReceived   { add => PacketReceived   += value; remove => PacketReceived   -= value; }
    event Action              INetworkProvider.ServerStarted    { add => ServerStarted    += value; remove => ServerStarted    -= value; }
    event Action<string>      INetworkProvider.ConnectionFailed { add => ConnectionFailed += value; remove => ConnectionFailed -= value; }

    public void StartServer(int port, int maxPeers)              => throw new NotImplementedException();
    public void ConnectToServer(string address, int port)        => throw new NotImplementedException();
    public void Disconnect()                                     => throw new NotImplementedException();
    public void SendReliable(int toPeerId, byte[] data)          => throw new NotImplementedException();
    public void SendUnreliable(int toPeerId, byte[] data)        => throw new NotImplementedException();
    public void BroadcastUnreliable(byte[] data, int excludePeerId = -1) => throw new NotImplementedException();
    public void BroadcastReliable(byte[] data, int excludePeerId = -1)   => throw new NotImplementedException();
}
