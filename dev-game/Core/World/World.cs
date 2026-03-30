using Godot;
using Core.Network;
using Core.Network.Rooms;

public partial class World : Node3D
{
	private PlayerSpawner _spawner = null!;

	public override void _Ready()
	{
		_spawner = GetNode<PlayerSpawner>("PlayerSpawner");

		var net = NetworkManager.Instance;

		if (net.IsServer)
		{
			GD.Print("[World] Dedicated server — no local player spawned.");
			return;
		}

		net.PeerJoined += OnPeerJoined;
		net.PeerLeft += OnPeerLeft;
		net.StateReceived += OnStateReceived;

		foreach (int peerId in net.RemotePeerIds)
		{
			if (peerId != net.LocalPeerId)
				OnPeerJoined(peerId);
		}

		var lobby = LobbyState.Current;
		LobbyState.Clear();

		if (lobby != null)
		{
			// Came from UI (lobby or dev quick-connect): initiate connection now
			GD.Print($"[World] Connecting to {lobby.ServerIp}:{lobby.ServerPort}...");
			net.LocalConnected += () => SpawnLocalPlayer(net.LocalPeerId);
			net.ConnectToServer(lobby.ServerIp, lobby.ServerPort);
		}
		else if (net.IsAutoConnecting && !net.IsRunning)
		{
			// --connect CLI flag, still connecting
			net.LocalConnected += () => SpawnLocalPlayer(net.LocalPeerId);
		}
		else
		{
			// Offline / local play
			SpawnLocalPlayer(net.LocalPeerId);
		}
	}

	private void SpawnLocalPlayer(int peerId)
	{
		var player = _spawner.SpawnPlayer(peerId, isLocal: true) as Player;
		if (player != null)
			NetworkManager.Instance.SetLocalPlayer(player);
	}

	private void OnPeerJoined(int peerId)
	{
		var net = NetworkManager.Instance;
		if (net.IsClient && peerId == net.LocalPeerId) return;
		_spawner.SpawnPlayer(peerId, isLocal: false);
	}

	private void OnPeerLeft(int peerId)
	{
		_spawner.DespawnPlayer(peerId);
	}

	private void OnStateReceived(PlayerNetState state)
	{
		if (!_spawner.Characters.TryGetValue(state.PeerId, out var character)) return;
		if (character is RemoteCharacter remote)
			remote.PushSnapshot(state, Time.GetTicksMsec());
	}

	public override void _Process(double delta) { }
}
