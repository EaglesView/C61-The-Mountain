using System;

namespace Core.Network.Providers;

/// <summary>
/// Implémentation Steam du transport réseau (stub).
/// Compile sans erreur mais lève <see cref="NotImplementedException"/> partout.
/// À implémenter lorsque GodotSteam sera intégré au projet.
/// </summary>
public class GodotSteamProvider : INetworkProvider
{
    /// <inheritdoc/>
    public NetworkRole Role => throw new NotImplementedException();

    /// <inheritdoc/>
    public int LocalPeerId => throw new NotImplementedException();

    /// <inheritdoc/>
    public bool IsRunning => throw new NotImplementedException();

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

    public event Action? ServerDisconnected;

    // TODO: Binder avec les callbacks Steam appropriés.
    event Action<int> INetworkProvider.PeerConnected { add => PeerConnected += value; remove => PeerConnected -= value; }
    event Action<int> INetworkProvider.PeerDisconnected { add => PeerDisconnected += value; remove => PeerDisconnected -= value; }
    event Action<int, byte[]> INetworkProvider.PacketReceived { add => PacketReceived += value; remove => PacketReceived -= value; }
    event Action INetworkProvider.ServerStarted { add => ServerStarted += value; remove => ServerStarted -= value; }
    event Action<string> INetworkProvider.ConnectionFailed { add => ConnectionFailed += value; remove => ConnectionFailed -= value; }
    event Action INetworkProvider.ServerDisconnected { add => ServerDisconnected += value; remove => ServerDisconnected -= value; }

    /// <inheritdoc/>
    public void StartServer(int port, int maxPeers) => throw new NotImplementedException();
    /// <inheritdoc/>
    public void ConnectToServer(string address, int port) => throw new NotImplementedException();
    /// <inheritdoc/>
    public void Disconnect() => throw new NotImplementedException();
    /// <inheritdoc/>
    public void SendReliable(int toPeerId, byte[] data) => throw new NotImplementedException();
    /// <inheritdoc/>
    public void SendUnreliable(int toPeerId, byte[] data) => throw new NotImplementedException();
    /// <inheritdoc/>
    public void BroadcastUnreliable(byte[] data, int excludePeerId = -1) => throw new NotImplementedException();
    /// <inheritdoc/>
    public void BroadcastReliable(byte[] data, int excludePeerId = -1) => throw new NotImplementedException();
}
