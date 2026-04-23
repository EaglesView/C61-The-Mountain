using Godot;
using Core.Network.Rooms;

public partial class LobbyScene : Control
{
    private Label         _codeValue    = null!;
    private Label         _serverValue  = null!;
    private Label         _statusValue  = null!;
    private Label         _playersTitle = null!;
    private VBoxContainer _playersList  = null!;
    private Button        _leaveButton  = null!;
    private Button        _startButton  = null!;

    private bool _leaving = false;
    private bool _wasHost = false;
    private const string Root         = "PanelContainer/MarginContainer/VBoxContainer";
    private const float  PollInterval = 4.0f;

    public override void _Ready()
    {
        _codeValue    = GetNode<Label>($"{Root}/CodeBox/CodeValue");
        _serverValue  = GetNode<Label>($"{Root}/ServerBox/ServerValue");
        _statusValue  = GetNode<Label>($"{Root}/StatusBox/StatusValue");
        _playersTitle = GetNode<Label>($"{Root}/PlayersTitle");
        _playersList  = GetNode<VBoxContainer>($"{Root}/PlayersPanel/ScrollContainer/PlayersList");
        _leaveButton  = GetNode<Button>($"{Root}/ButtonsBox/LeaveButton");
        _startButton  = GetNode<Button>($"{Root}/ButtonsBox/StartButton");

        _leaveButton.Pressed += OnLeavePressed;
        _startButton.Pressed += OnStartPressed;

        var snapshot = LobbyState.Current;
        if (snapshot is null)
        {
            GD.PrintErr("[Lobby] No lobby state, going back.");
            GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
            return;
        }

        _codeValue.Text   = snapshot.Code;
        _serverValue.Text = $"{snapshot.ServerIp}:{snapshot.ServerPort}";
        _statusValue.Text = snapshot.Status;

        if (LobbyState.IsHost)
            _startButton.Disabled = false;

        RefreshPlayerList(snapshot);

        var timer = new Timer();
        timer.WaitTime = PollInterval;
        timer.Autostart = true;
        timer.Timeout += OnPollTick;
        AddChild(timer);
    }

    private async void OnPollTick()
    {
        if (_leaving || !IsInsideTree()) return;

        var snapshot = LobbyState.Current;
        if (snapshot is null) return;

        try
        {
            var fresh = await RoomServiceProvider.Repository.GetAsync(snapshot.Code);
            if (fresh is null || _leaving || !IsInsideTree()) return;

            LobbyState.Set(fresh, LobbyState.IsHost);
            RefreshPlayerList(fresh);
            _statusValue.Text = fresh.Status;

            if (fresh.Status == "started")
                GoToGame();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Lobby] Poll failed: {ex.Message}");
        }
    }

    private async void OnStartPressed()
    {
        _startButton.Disabled = true;
        var snapshot = LobbyState.Current;
        if (snapshot is null) return;

        try
        {
            await RoomServiceProvider.Repository.UpdateStatusAsync(snapshot.Code, "started");
            GoToGame();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Lobby] Start failed: {ex.Message}");
            _startButton.Disabled = false;
        }
    }

    private void GoToGame()
    {
        if (_leaving || !IsInsideTree()) return;
        _leaving = true;
        _wasHost = LobbyState.IsHost;

        var serverIp   = LobbyState.Current?.ServerIp   ?? Core.Network.Rooms.Room.HardcodedServerIp;
        var serverPort = LobbyState.Current?.ServerPort ?? Core.Network.Rooms.Room.HardcodedServerPort;
        LobbyState.Clear();

        _statusValue.Text = "Connecting to server…";

        var net = Core.Network.NetworkManager.Instance;
        net.LocalConnected   += OnServerConnected;
        net.ConnectionFailed += OnServerConnectionFailed;
        net.ConnectToServer(serverIp, serverPort);
    }

    private void OnServerConnected(int _)
    {
        var net = Core.Network.NetworkManager.Instance;
        net.LocalConnected   -= OnServerConnected;
        net.ConnectionFailed -= OnServerConnectionFailed;
        GetTree().ChangeSceneToFile("res://Core/World/world.tscn");
    }

    private void OnServerConnectionFailed(string msg)
    {
        var net = Core.Network.NetworkManager.Instance;
        net.LocalConnected   -= OnServerConnected;
        net.ConnectionFailed -= OnServerConnectionFailed;
        _leaving = false;
        _statusValue.Text = $"Connection failed: {msg}";
        if (_wasHost) _startButton.Disabled = false;
    }

    private void RefreshPlayerList(RoomSnapshot snapshot)
    {
        foreach (Node child in _playersList.GetChildren())
            child.QueueFree();

        foreach (var (_, entry) in snapshot.Players)
            AddPlayerRow(entry.Username, entry.IsHost);

        _playersTitle.Text = $"Players ({snapshot.Players.Count} / {snapshot.MaxPlayers})";
    }

    private void AddPlayerRow(string username, bool isHost)
    {
        var label = new Label();
        label.Text = isHost ? $"  ★  {username}  (Host)" : $"  •  {username}";
        _playersList.AddChild(label);
    }

    private async void OnLeavePressed()
    {
        _leaving = true;
        _leaveButton.Disabled = true;

        var snapshot = LobbyState.Current;
        var me       = Core.Auth.AuthServiceProvider.Instance.CurrentUser;
        if (snapshot is not null && me is not null)
        {
            try
            {
                await RoomServiceProvider.Repository.RemovePlayerAsync(snapshot.Code, me.Id);
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Lobby] Leave cleanup failed: {ex.Message}");
            }
        }

        LobbyState.Clear();
        GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
    }
}
