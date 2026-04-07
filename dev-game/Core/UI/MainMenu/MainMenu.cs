using Godot;
using System.Collections.Generic;
using Core.Network.Rooms;

public partial class MainMenu : Control
{
    private Button _play    = null!;
    private Button _create  = null!;
    private Button _join    = null!;
    private Button _profile = null!;
    private Button _credits = null!;
    private Button _signOut = null!;
    private Button _exit    = null!;

    // Join dialog
    private const string DialogRoot = "JoinOverlay/JoinDialog/MarginContainer/VBoxContainer";
    private Control  _joinOverlay  = null!;
    private LineEdit _codeEntry    = null!;
    private Button   _joinConfirm  = null!;
    private Button   _joinCancel   = null!;
    private Label    _joinStatus   = null!;

    public override void _Ready()
    {
        _play    = GetNode<Button>("VBoxContainer/PlayButton");
        _create  = GetNode<Button>("VBoxContainer/CreateGameButton");
        _join    = GetNode<Button>("VBoxContainer/JoinGameButton");
        _profile = GetNode<Button>("VBoxContainer/ProfileButton");
        _credits = GetNode<Button>("VBoxContainer/CreditsButton");
        _signOut = GetNode<Button>("VBoxContainer/SignOutButton");
        _exit    = GetNode<Button>("VBoxContainer/ExitButton");

        _joinOverlay = GetNode<Control>("JoinOverlay");
        _codeEntry   = GetNode<LineEdit>($"{DialogRoot}/CodeEntry");
        _joinConfirm = GetNode<Button>($"{DialogRoot}/ButtonsRow/JoinButton");
        _joinCancel  = GetNode<Button>($"{DialogRoot}/ButtonsRow/CancelButton");
        _joinStatus  = GetNode<Label>($"{DialogRoot}/JoinStatus");

        _play.Pressed    += OnPlayPressed;
        _create.Pressed  += OnCreatePressed;
        _join.Pressed    += OnJoinPressed;
        _profile.Pressed += OnProfilePressed;
        _credits.Pressed += OnCreditsPressed;
        _signOut.Pressed += OnSignOutPressed;
        _exit.Pressed    += OnExitPressed;

        _joinConfirm.Pressed += OnJoinConfirmPressed;
        _joinCancel.Pressed  += OnJoinCancelPressed;
    }

    private void OnPlayPressed()
    {
        // Local offline play — no LobbyState needed
        GetTree().ChangeSceneToFile("res://Core/World/world.tscn");
    }

    private async void OnCreatePressed()
    {
        _create.Disabled = true;
        try
        {
            var user = Core.Auth.AuthServiceProvider.Instance.CurrentUser
                ?? throw new System.InvalidOperationException("Not authenticated.");

            var code = Room.GenerateCode();
            var room = new Room(code, user.Id, user.Username);
            await RoomServiceProvider.Repository.CreateAsync(room);

            var snapshot = new RoomSnapshot
            {
                Code       = code,
                HostUserId = user.Id,
                ServerIp   = Room.HardcodedServerIp,
                ServerPort = Room.HardcodedServerPort,
                Status     = Room.DefaultStatus,
                MaxPlayers = Room.DefaultMaxPlayers,
                Players    = new Dictionary<string, RoomSnapshot.PlayerEntry>
                {
                    [user.Id] = new RoomSnapshot.PlayerEntry { Username = user.Username, IsHost = true }
                }
            };

            LobbyState.Set(snapshot, isHost: true);
            GetTree().ChangeSceneToFile("res://Core/UI/Room/lobby.tscn");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Room] Failed to create room: {ex.Message}");
            _create.Disabled = false;
        }
    }

    private void OnJoinPressed()
    {
        _codeEntry.Text     = "";
        _joinStatus.Visible = false;
        _joinOverlay.Visible = true;
        _codeEntry.GrabFocus();
    }

    private async void OnJoinConfirmPressed()
    {
        var code = _codeEntry.Text.Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            ShowJoinStatus("Enter a room code.");
            return;
        }

        _joinConfirm.Disabled = true;
        _joinCancel.Disabled  = true;
        ShowJoinStatus("Searching...");

        try
        {
            var user = Core.Auth.AuthServiceProvider.Instance.CurrentUser
                ?? throw new System.InvalidOperationException("Not authenticated.");

            var snapshot = await RoomServiceProvider.Repository.GetAsync(code);
            if (snapshot is null)
            {
                ShowJoinStatus("Room not found.");
                return;
            }
            if (snapshot.Players.Count >= snapshot.MaxPlayers)
            {
                ShowJoinStatus("Room is full.");
                return;
            }

            await RoomServiceProvider.Repository.AddPlayerAsync(code, user.Id, user.Username);

            snapshot.Players[user.Id] = new RoomSnapshot.PlayerEntry
            {
                Username = user.Username,
                IsHost   = false
            };

            LobbyState.Set(snapshot, isHost: false);
            GetTree().ChangeSceneToFile("res://Core/UI/Room/lobby.tscn");
        }
        catch (System.Exception ex)
        {
            ShowJoinStatus($"Error: {ex.Message}");
        }
        finally
        {
            _joinConfirm.Disabled = false;
            _joinCancel.Disabled  = false;
        }
    }

    private void OnJoinCancelPressed()
    {
        _joinOverlay.Visible = false;
    }

    private void ShowJoinStatus(string msg)
    {
        _joinStatus.Text    = msg;
        _joinStatus.Visible = true;
    }

    private void OnProfilePressed()
    {
        GetTree().ChangeSceneToFile("res://Core/UI/Profile/profile.tscn");
    }

    private void OnCreditsPressed() { }

    private async void OnSignOutPressed()
    {
        await Core.Auth.AuthServiceProvider.SignOutAsync();
        GetTree().ChangeSceneToFile("res://Core/UI/LoginTemp/login_temp.tscn");
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }
}
