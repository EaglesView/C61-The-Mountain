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
	[Export] private Player _player;
	[ExportCategory("Internal")]
	[Export] private Label _fpsValue;

	// Server section
	[Export] private Label _serverValue;
	[Export] private Label _responseTimeValue;
	[Export] private Label _playerAmtValue;
	[Export] private Label _pingValue;
	[Export] private Label _positionXLabel;
	[Export] private Label _positionYLabel;
	[Export] private Label _headAngleRadLabel;
	[Export] private Label _headAngleDegLabel;
	[Export] private Label _velocityLabel;

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
		_positionXLabel.Text = _player.GlobalPosition.X.ToString();
		_positionYLabel.Text = _player.GlobalPosition.Y.ToString();
		_headAngleRadLabel.Text = (-_player.GetHeadAngle()).ToString();
		_headAngleDegLabel.Text = (-_player.GetHeadAngle() * (180.0f / Mathf.Pi)).ToString();
		_velocityLabel.Text = _player.Velocity.ToString();

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
