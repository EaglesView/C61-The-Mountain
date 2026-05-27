using Godot;
using System.Collections.Generic;
using Core.Network.Rooms;

public partial class MainMenu : Control
{
	[Export] private Button _local;
	[Export] private Button _play;
	[Export] private Button _profile;
	[Export] private Button _credits;
	[Export] private Button _settings;
	[Export] private Button _signOut;
	[Export] private Button _exit;
	[Export] private Label _status;

	public override void _Ready()
	{
		if (_play == null) _play = GetNode<Button>("VBoxContainer/PlayButton");
		if (_local == null) _local = GetNode<Button>("VBoxContainer/LocalButton");
		if (_profile == null) _profile = GetNode<Button>("VBoxContainer/ProfileButton");
		if (_credits == null) _credits = GetNode<Button>("VBoxContainer/CreditsButton");
		if (_settings == null) _settings = GetNode<Button>("VBoxContainer/SettingsButton");
		if (_signOut == null) _signOut = GetNode<Button>("VBoxContainer/SignOutButton");
		if (_exit == null) _exit = GetNode<Button>("VBoxContainer/ExitButton");
		if (_status == null) _status = GetNode<Label>("VBoxContainer/StatusLabel");

		_play.Pressed += OnPlayPressed;
		_local.Pressed += OnLocalPressed;
		_profile.Pressed += OnProfilePressed;
		_credits.Pressed += OnCreditsPressed;
		_settings.Pressed += OnSettingsPressed;
		_signOut.Pressed += OnSignOutPressed;
		_exit.Pressed += OnExitPressed;
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

			if (string.IsNullOrEmpty(LobbyState.ProfileUsername) || LobbyState.SelectedHatId == HatRegistry.DefaultHatId)
			{
				ShowStatus("Chargement du profil…");
				var profile = await Core.Profile.ProfileServiceProvider.Instance.GetOrCreateProfileAsync(
					user.Id, user.Username);
				LobbyState.SetProfileUsername(profile.Username);
				LobbyState.SetSelectedHat(profile.SelectedHatId);
			}

			var displayName = !string.IsNullOrEmpty(LobbyState.ProfileUsername) ? LobbyState.ProfileUsername : user.Username;

			var snapshot = await RoomServiceProvider.Repository.GetAsync(Room.SharedLobbyCode);
			bool isHost;

			if (snapshot != null
				&& snapshot.Status == Room.DefaultStatus
				&& snapshot.Players.Count < snapshot.MaxPlayers)
			{
				// Si le lobby existant n'a plus aucun hôte vivant (zombie : tous
				// les joueurs ont Quitté, ou l'hôte est parti sans migration),
				// le nouveau venu en prend possession. Sinon il rejoint comme
				// invité de l'hôte courant.
				bool lobbyHasLivingHost = false;
				foreach (var p in snapshot.Players.Values)
					if (p.IsHost) { lobbyHasLivingHost = true; break; }

				isHost = !lobbyHasLivingHost;
				await RoomServiceProvider.Repository.AddPlayerAsync(
					Room.SharedLobbyCode, user.Id, displayName, isHost, LobbyState.SelectedHatId);
				if (isHost)
					await RoomServiceProvider.Repository.UpdateHostAsync(
						Room.SharedLobbyCode, user.Id);
				snapshot.Players[user.Id] = new RoomSnapshot.PlayerEntry
				{ Username = displayName, IsHost = isHost, HatId = LobbyState.SelectedHatId };
			}
			else
			{
				// No open lobby (or it's full / already started) — create a fresh one
				var room = new Room(Room.SharedLobbyCode, user.Id, displayName);
				await RoomServiceProvider.Repository.CreateAsync(room, serverIp);
				snapshot = new RoomSnapshot
				{
					Code = Room.SharedLobbyCode,
					HostUserId = user.Id,
					ServerIp = serverIp,
					ServerPort = Room.HardcodedServerPort,
					Status = Room.DefaultStatus,
					MaxPlayers = Room.DefaultMaxPlayers,
					Players = new Dictionary<string, RoomSnapshot.PlayerEntry>
					{
						[user.Id] = new RoomSnapshot.PlayerEntry
						{ Username = displayName, IsHost = true, HatId = LobbyState.SelectedHatId }
					}
				};
				isHost = true;
			}

			LobbyState.Set(snapshot, isHost);
			var err = GetTree().ChangeSceneToFile("res://Core/Logic/main_controller.tscn");
			if (err != Error.Ok) ShowStatus($"Scene change failed: {err}");
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
		GetTree().ChangeSceneToFile("res://Core/UI/Lobby/lobbylocal.tscn");
	}

	// ── Other buttons ─────────────────────────────────────────────────────────

	private void OnProfilePressed()
	{
		GetTree().ChangeSceneToFile("res://Core/UI/Profile/profile.tscn");
	}

	private void OnCreditsPressed()
	{
		GetTree().ChangeSceneToFile("res://Core/UI/Splash/credits_splash.tscn");
	}

	private void OnSettingsPressed()
	{
		var settingsScene = GD.Load<PackedScene>("res://Core/UI/Settings/settings_menu.tscn");
		AddChild(settingsScene.Instantiate<Control>());
	}

	private async void OnSignOutPressed()
	{
		await Core.Auth.AuthServiceProvider.SignOutAsync();
		// Coupe la session ENet pour libérer le slot invité côté serveur
		// (PeerDisconnected → GuestSlotRegistry.ReleasePeer). Sans ça, la
		// session ENet survit au sign-out (NetworkManager est autoload) et
		// le slot reste revendiqué jusqu'au timeout ENet, ~30s.
		Core.Network.NetworkManager.Instance?.Disconnect();
		GetTree().ChangeSceneToFile("res://Core/UI/LoginTemp/login_temp.tscn");
	}

	private void OnExitPressed() => GetTree().Quit();

	// ── Helpers ───────────────────────────────────────────────────────────────

	private void ShowStatus(string msg)
	{
		_status.Text = msg;
		_status.Visible = true;
	}
}
