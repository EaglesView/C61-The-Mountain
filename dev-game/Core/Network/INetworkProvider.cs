using System;

namespace Core.Network;

public enum NetworkRole { None, Server, Client }

public interface INetworkProvider
{
    NetworkRole Role    { get; }
    int LocalPeerId     { get; }
    bool IsRunning      { get; }

    void StartServer(int port, int maxPeers);
    void ConnectToServer(string address, int port);
    void Disconnect();

    void SendReliable(int toPeerId, byte[] data);
    void SendUnreliable(int toPeerId, byte[] data);
    void BroadcastUnreliable(byte[] data, int excludePeerId = -1);
    void BroadcastReliable(byte[] data, int excludePeerId = -1);

    event Action<int>         PeerConnected;
    event Action<int>         PeerDisconnected;
    event Action<int, byte[]> PacketReceived;  // (fromPeerId, rawBytes)
    event Action              ServerStarted;
    event Action<string>      ConnectionFailed;
}
