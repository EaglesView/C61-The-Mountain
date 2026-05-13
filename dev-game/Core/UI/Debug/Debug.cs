/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:                Debug.cs                           |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG        |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;

public partial class Debug : Control
{
	/// ····································
	/// : _____  _____  ___  ___ _____ ___ :
	/// :| __\ \/ | _ \/ _ \| _ |_   _/ __|:
	/// :| _| >  <|  _| (_) |   / | | \__ \:
	/// :|___/_/\_|_|  \___/|_|_\ |_| |___/:
	/// ····································
	private Player? _player;

	[ExportCategory("Internal")]
	[Export] private Label _fpsValue;

	// Server section
	[Export] private Label _serverValue;
	[Export] private Label _responseTimeValue;
	[Export] private Label _playerAmtValue;
	[Export] private Label _pingValue;
	[Export] private Label _positionXLabel;
	[Export] private Label _positionYLabel;
	[Export] private Label _positionZLabel;
	[Export] private Label _headAngleRadLabel;
	[Export] private Label _headAngleDegLabel;
	[Export] private Label _velocityLabel;
	[Export] private Label _speedLabel;
	[Export] private Label _characterStateLabel;
	[Export] private Label _isOnFloorLabel;
	[Export] private Label _isRagdollLabel;

	// Replication section
	[Export] private Label _remoteTrackedPeerLabel;
	[Export] private Label _remotePosXLabel;
	[Export] private Label _remotePosYLabel;
	[Export] private Label _remotePosZLabel;
	[Export] private Label _replicationDeltaLabel;

	/// ···········································
	/// : _    ___ ___ ___ _____   _____ _    ___ :
	/// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
	/// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
	/// :|____|___|_| |___\___| |_| \___|____|___|:
	/// ···········································

	public override void _Ready()
	{
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		_fpsValue.Text = Engine.GetFramesPerSecond().ToString();

		if (_player == null)
		{
			var group = GetTree().GetNodesInGroup("local_player");
			if (group.Count == 0) return;
			_player = group[0] as Player;
			if (_player == null) return;
		}
		_positionXLabel.Text = _player.GlobalPosition.X.ToString("F3");
		_positionYLabel.Text = _player.GlobalPosition.Y.ToString("F3");
		_positionZLabel.Text = _player.GlobalPosition.Z.ToString("F3");
		_headAngleRadLabel.Text = (-_player.GetHeadAngle()).ToString("F3");
		_headAngleDegLabel.Text = (-_player.GetHeadAngle() * (180.0f / Mathf.Pi)).ToString("F1");
		_velocityLabel.Text = _player.Velocity.ToString();
		_speedLabel.Text = _player.Velocity.Length().ToString("F2");
		_characterStateLabel.Text = _player.GetCurrentState().ToString();
		_isOnFloorLabel.Text = _player.IsOnFloor().ToString();
		_isRagdollLabel.Text = _player.PhysicsSkelton.IsRagdoll.ToString();

		Player? remote = null;
		var players = GetTree().Root.FindChild("Players", true, false);
		if (players != null)
		{
			foreach (var child in players.GetChildren())
			{
				if (child is Player p && !p.IsMultiplayerAuthority()) { remote = p; break; }
			}
		}

		if (remote != null)
		{
			_remoteTrackedPeerLabel.Text = remote.PeerId.ToString();
			_remotePosXLabel.Text = remote.GlobalPosition.X.ToString("F3");
			_remotePosYLabel.Text = remote.GlobalPosition.Y.ToString("F3");
			_remotePosZLabel.Text = remote.GlobalPosition.Z.ToString("F3");
			_replicationDeltaLabel.Text = _player!.GlobalPosition.DistanceTo(remote.GlobalPosition).ToString("F3");
		}
		else
		{
			_remoteTrackedPeerLabel.Text = "--";
			_remotePosXLabel.Text = "--";
			_remotePosYLabel.Text = "--";
			_remotePosZLabel.Text = "--";
			_replicationDeltaLabel.Text = "--";
		}

		var peer = Multiplayer.MultiplayerPeer;
		bool connected = peer != null && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

		_serverValue.Text = connected ? (Multiplayer.IsServer() ? "Host" : "Client") : "Offline";
		_playerAmtValue.Text = connected ? (Multiplayer.GetPeers().Length + 1).ToString() : "--";
		// Pour le server debug
		if (connected && !Multiplayer.IsServer())
		{
			var enet = peer as ENetMultiplayerPeer;
			double rtt = enet?.GetPeer(1)?.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) ?? 0;
			_pingValue.Text = $"{rtt} ms";
			_responseTimeValue.Text = $"{rtt / 2} ms";
		}
		else
		{
			_pingValue.Text = "--";
			_responseTimeValue.Text = "--";
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("debug_menu"))
		{
			Visible = !Visible;
			Input.MouseMode = Visible
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}
}
