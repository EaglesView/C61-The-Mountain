using Godot;
using Core.Network.Rooms;

public partial class LobbyScene : Control
{
    private Label         _codeValue    = null!;
    private Label         _serverValue  = null!;
    private Label         _statusValue  = null!;
    private Label         _mapValue     = null!;
    private Button        _mapPrevButton = null!;
    private Button        _mapNextButton = null!;
    private Label         _playersTitle = null!;
    private VBoxContainer _playersList  = null!;
    private Button        _leaveButton  = null!;
    private Button        _startButton  = null!;

    private bool _leaving  = false;
    private bool _wasHost  = false;
    private int  _mapIndex = 0;
    private const string Root         = "PanelContainer/MarginContainer/VBoxContainer";
    private const float  PollInterval = 4.0f;

    public override void _Ready()
    {
        _codeValue     = GetNode<Label>($"{Root}/CodeBox/CodeValue");
        _serverValue   = GetNode<Label>($"{Root}/ServerBox/ServerValue");
        _statusValue   = GetNode<Label>($"{Root}/StatusBox/StatusValue");
        _mapValue      = GetNode<Label>($"{Root}/MapBox/MapValue");
        _mapPrevButton = GetNode<Button>($"{Root}/MapBox/MapPrev");
        _mapNextButton = GetNode<Button>($"{Root}/MapBox/MapNext");
        _playersTitle  = GetNode<Label>($"{Root}/PlayersTitle");
        _playersList   = GetNode<VBoxContainer>($"{Root}/PlayersPanel/ScrollContainer/PlayersList");
        _leaveButton   = GetNode<Button>($"{Root}/ButtonsBox/LeaveButton");
        _startButton   = GetNode<Button>($"{Root}/ButtonsBox/StartButton");

        _leaveButton.Pressed   += OnLeavePressed;
        _startButton.Pressed   += OnStartPressed;
        _mapPrevButton.Pressed += OnMapPrev;
        _mapNextButton.Pressed += OnMapNext;

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

        _mapIndex = MapRegistry.IndexOf(snapshot.MapId);
        _RefreshMapDisplay();

        // Only the host can change the map
        _mapPrevButton.Visible = LobbyState.IsHost;
        _mapNextButton.Visible = LobbyState.IsHost;

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

            // Non-host clients pick up map changes on each poll
            if (!LobbyState.IsHost)
            {
                _mapIndex = MapRegistry.IndexOf(fresh.MapId);
                _RefreshMapDisplay();
            }

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
        LobbyState.SetSelectedMap(LobbyState.Current?.MapId ?? MapRegistry.DefaultMapId);
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

    private void _RefreshMapDisplay()
    {
        _mapValue.Text = MapRegistry.All[_mapIndex].DisplayName;
    }

    private async void OnMapPrev()
    {
        _mapIndex = (_mapIndex - 1 + MapRegistry.All.Length) % MapRegistry.All.Length;
        _RefreshMapDisplay();
        await _PushMapUpdate();
    }

    private async void OnMapNext()
    {
        _mapIndex = (_mapIndex + 1) % MapRegistry.All.Length;
        _RefreshMapDisplay();
        await _PushMapUpdate();
    }

    private async System.Threading.Tasks.Task _PushMapUpdate()
    {
        var snapshot = LobbyState.Current;
        if (snapshot is null) return;
        _mapPrevButton.Disabled = true;
        _mapNextButton.Disabled = true;
        try
        {
            var newMapId = MapRegistry.All[_mapIndex].Id;
            await RoomServiceProvider.Repository.UpdateMapAsync(snapshot.Code, newMapId);
            LobbyState.Set(new Core.Network.Rooms.RoomSnapshot
            {
                Code       = snapshot.Code,
                HostUserId = snapshot.HostUserId,
                ServerIp   = snapshot.ServerIp,
                ServerPort = snapshot.ServerPort,
                Status     = snapshot.Status,
                MaxPlayers = snapshot.MaxPlayers,
                MapId      = newMapId,
                Players    = snapshot.Players
            }, LobbyState.IsHost);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Lobby] Map update failed: {ex.Message}");
        }
        finally
        {
            _mapPrevButton.Disabled = false;
            _mapNextButton.Disabled = false;
        }
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
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var icon = new Label();
        icon.Text = isHost ? "★" : "•";

        var name = new Label();
        name.Text = isHost ? $"{username}  (Hôte)" : username;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        row.AddChild(icon);
        row.AddChild(name);
        _playersList.AddChild(row);
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
