using Godot;
using System.Collections.Generic;
using Core.Network.Rooms;

public partial class MainMenu : Control
{
    private Button _play    = null!;
    private Button _local   = null!;
    private Button _profile = null!;
    private Button _credits = null!;
    private Button _signOut = null!;
    private Button _exit    = null!;
    private Label  _status  = null!;

    public override void _Ready()
    {
        _play    = GetNode<Button>("VBoxContainer/PlayButton");
        _local   = GetNode<Button>("VBoxContainer/LocalButton");
        _profile = GetNode<Button>("VBoxContainer/ProfileButton");
        _credits = GetNode<Button>("VBoxContainer/CreditsButton");
        _signOut = GetNode<Button>("VBoxContainer/SignOutButton");
        _exit    = GetNode<Button>("VBoxContainer/ExitButton");
        _status  = GetNode<Label>("VBoxContainer/StatusLabel");

        _play.Pressed    += OnPlayPressed;
        _local.Pressed   += OnLocalPressed;
        _profile.Pressed += OnProfilePressed;
        _credits.Pressed += OnCreditsPressed;
        _signOut.Pressed += OnSignOutPressed;
        _exit.Pressed    += OnExitPressed;
    }

    // ── Play Online — auto-join / create the shared lobby ────────────────────

    private async void OnPlayPressed()
    {
        _play.Disabled = true;
        ShowStatus("Joining lobby…");

        string serverIp = OS.GetEnvironment("SERVER_IP");
        if (string.IsNullOrEmpty(serverIp)) serverIp = Room.HardcodedServerIp;

        try
        {
            var user = Core.Auth.AuthServiceProvider.Instance.CurrentUser
                ?? throw new System.InvalidOperationException("Not authenticated.");

            var snapshot = await RoomServiceProvider.Repository.GetAsync(Room.SharedLobbyCode);
            bool isHost;

            if (snapshot != null
                && snapshot.Status == Room.DefaultStatus
                && snapshot.Players.Count < snapshot.MaxPlayers)
            {
                // Slot in the existing open lobby
                await RoomServiceProvider.Repository.AddPlayerAsync(
                    Room.SharedLobbyCode, user.Id, user.Username);
                snapshot.Players[user.Id] = new RoomSnapshot.PlayerEntry
                    { Username = user.Username, IsHost = false };
                isHost = false;
            }
            else
            {
                // No open lobby (or it's full / already started) — create a fresh one
                var room = new Room(Room.SharedLobbyCode, user.Id, user.Username);
                await RoomServiceProvider.Repository.CreateAsync(room, serverIp);
                snapshot = new RoomSnapshot
                {
                    Code       = Room.SharedLobbyCode,
                    HostUserId = user.Id,
                    ServerIp   = serverIp,
                    ServerPort = Room.HardcodedServerPort,
                    Status     = Room.DefaultStatus,
                    MaxPlayers = Room.DefaultMaxPlayers,
                    Players    = new Dictionary<string, RoomSnapshot.PlayerEntry>
                    {
                        [user.Id] = new RoomSnapshot.PlayerEntry
                            { Username = user.Username, IsHost = true }
                    }
                };
                isHost = true;
            }

            LobbyState.Set(snapshot, isHost);
            GetTree().ChangeSceneToFile("res://Core/UI/Room/lobby.tscn");
        }
        catch (System.Exception ex)
        {
            ShowStatus($"Error: {ex.Message}");
            _play.Disabled = false;
        }
    }

    // ── Dev shortcut — offline play without Firebase ─────────────────────────

    private void OnLocalPressed()
    {
        LobbyState.Clear();
        GetTree().ChangeSceneToFile("res://Core/World/world.tscn");
    }

    // ── Other buttons ─────────────────────────────────────────────────────────

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

    private void OnExitPressed() => GetTree().Quit();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowStatus(string msg)
    {
        _status.Text    = msg;
        _status.Visible = true;
    }
}
