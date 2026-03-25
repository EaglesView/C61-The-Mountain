using Godot;
using System.Collections.Generic;
using Core.Network;

public partial class World : Node3D
{
	private readonly Dictionary<int, Character> _characters = new();
	private PackedScene? _remoteCharacterScene;

	public override void _Ready()
	{
		_remoteCharacterScene = GD.Load<PackedScene>("res://Core/World/CharacterModel/RemoteCharacter.tscn");

		var net = NetworkManager.Instance;

		if (net.IsServer)
		{
			GD.Print("[World] Dedicated server — no local player spawned.");
			return;
		}

		net.PeerJoined    += OnPeerJoined;
		net.PeerLeft      += OnPeerLeft;
		net.StateReceived += OnStateReceived;

		if (net.IsAutoConnecting)
			net.LocalConnected += () => SpawnLocalPlayer(net.LocalPeerId);
		else
			SpawnLocalPlayer(net.LocalPeerId);
	}

	private void SpawnLocalPlayer(int peerId)
	{
		var scene  = GD.Load<PackedScene>("res://Core/World/CharacterModel/Player.tscn");
		var player = scene.Instantiate<Player>();
		player.PeerId = peerId;
		AddChild(player);
		_characters[peerId] = player;
		NetworkManager.Instance.SetLocalPlayer(player);
	}

	private void OnPeerJoined(int peerId)
	{
		// Don't spawn a RemoteCharacter for ourselves when the connection event echoes our own ID
        var net = NetworkManager.Instance;
        if (net.IsClient && peerId == net.LocalPeerId) return;
        if (_characters.ContainsKey(peerId)) return;
        var remote = _remoteCharacterScene!.Instantiate<RemoteCharacter>();
        remote.PeerId = peerId;
        AddChild(remote);
        _characters[peerId] = remote;
    }

    private void OnPeerLeft(int peerId)
    {
        if (!_characters.TryGetValue(peerId, out var character)) return;
        character.QueueFree();
        _characters.Remove(peerId);
    }

    private void OnStateReceived(PlayerNetState state)
    {
        if (!_characters.TryGetValue(state.PeerId, out var character)) return;
        if (character is RemoteCharacter remote)
            remote.PushSnapshot(state, Time.GetTicksMsec());
    }

    public override void _Process(double delta) { }
}
