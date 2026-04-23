using Godot;
using Core.Network;
using Core.Network.Rooms;

public partial class World : Node3D
{
	[Export] private PackedScene _playerScene = null!;

	private MultiplayerSpawner _spawner = null!;
	private PlayerSpawner _playerSpawner = null!;

	public override void _Ready()
	{
		_spawner = GetNode<MultiplayerSpawner>("MultiplayerSpawner");
		_playerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
		_spawner.SpawnFunction = Callable.From<Variant, GodotObject>(SpawnPlayerNode);

		LobbyState.Clear();

		var net = NetworkManager.Instance;
		net.StateReceived += OnStateReceived;

		if (net.IsServer)
		{
			GD.Print("[World] Dedicated server — waiting for peers.");
			Multiplayer.PeerDisconnected += id => ServerDespawnPeer((int)id);
			return;
		}

		if (net.IsClient)
		{
			GD.Print("[World] Online client — requesting spawn from server.");
			Rpc(MethodName.ClientReady);
			return;
		}

		GD.Print("[World] Offline — spawning local player directly.");
		SpawnOffline();
	}

	// Called on ALL peers by MultiplayerSpawner when server calls Spawn()
	private GodotObject SpawnPlayerNode(Variant data)
	{
		var dict = data.As<Godot.Collections.Dictionary>();
		int peerId = dict["id"].As<int>();
		Vector3 pos = dict["pos"].As<Vector3>();

		var player = _playerScene.Instantiate<Player>();
		player.Name = peerId.ToString();
		player.PeerId = peerId;
		player.SetMultiplayerAuthority(peerId);
		player.SpawnPosition = pos;

		GD.Print($"[World] SpawnPlayerNode: peerId={peerId}, pos={pos}, isLocal={player.IsMultiplayerAuthority()}");
		return player;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientReady()
	{
		if (!Multiplayer.IsServer()) return;
		int peerId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[World] ClientReady from peer {peerId}");
		ServerSpawnPeer(peerId);
	}

	private void ServerSpawnPeer(int peerId)
	{
		Vector3 spawnPos = _playerSpawner.GetNextSpawnPoint();
		var data = new Godot.Collections.Dictionary { ["id"] = peerId, ["pos"] = spawnPos };
		_spawner.Spawn(data);
		GD.Print($"[World] Server spawned peer {peerId} at {spawnPos}");
	}

	private void ServerDespawnPeer(int peerId)
	{
		var players = GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull(peerId.ToString());
		if (player != null)
		{
			((Node)player).QueueFree();
			GD.Print($"[World] Server despawned peer {peerId}");
		}
	}

	private void SpawnOffline()
	{
		var player = _playerScene.Instantiate<Player>();
		player.Name = "1";
		player.PeerId = 1;
		player.SpawnPosition = _playerSpawner.GetNextSpawnPoint();
		GetNode("Players").AddChild(player);
	}

	private void OnStateReceived(PlayerNetState state)
	{
		var players = GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull<Player>(state.PeerId.ToString());
		if (player == null || player.IsMultiplayerAuthority()) return;
		player.PushSnapshot(state, Time.GetTicksMsec());
	}

	public override void _ExitTree()
	{
		GD.Print("[World] _ExitTree called!");
	}
}
