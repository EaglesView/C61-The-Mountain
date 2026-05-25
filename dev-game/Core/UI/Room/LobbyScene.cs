using System.Collections.Generic;
using System.Linq;
using Godot;
using Core.Network.Rooms;

public partial class LobbyScene : Control
{
	/// <summary>
	/// Émis quand la connexion au serveur a réussi et que la scène est prête à
	/// céder la main au <c>GameController</c>. Aucun changement de scène n'est
	/// effectué côté lobby&#160;: c'est au <c>LobbyController</c> (parent) de
	/// nettoyer cette UI via <c>Exit()</c> une fois la FSM avancée.
	/// </summary>
	[Signal] public delegate void GameStartRequestedEventHandler();

	private Label  _codeValue     = null!;
	private Label  _statusValue   = null!;
	private Label  _mapValue      = null!;
	private Button _mapPrevButton = null!;
	private Button _mapNextButton = null!;
	private Button _leaveButton   = null!;
	private Button _startButton   = null!;

	private HBoxContainer _penguinsRow = null!;
	private readonly Dictionary<string, Control> _playerSlots = new();

	private bool  _leaving = false;
	private bool  _wasHost = false;
	private int   _mapIndex = 0;
	private ulong _instantiatedAtMsec;
	private const float PollInterval            = 4.0f;
	private const ulong StartedTriggerGraceMsec = 4_000;

	public override void _Ready()
	{
		_instantiatedAtMsec = Time.GetTicksMsec();
		const string TopHBox = "VBox/TopBar/Margin/HBox";
		_codeValue     = GetNode<Label> ($"{TopHBox}/CodeValue");
		_statusValue   = GetNode<Label> ($"{TopHBox}/StatusValue");
		_mapValue      = GetNode<Label> ($"{TopHBox}/MapBox/MapValue");
		_mapPrevButton = GetNode<Button>($"{TopHBox}/MapBox/MapPrev");
		_mapNextButton = GetNode<Button>($"{TopHBox}/MapBox/MapNext");
		_leaveButton   = GetNode<Button>($"{TopHBox}/LeaveButton");
		_startButton   = GetNode<Button>($"{TopHBox}/StartButton");
		_penguinsRow   = GetNode<HBoxContainer>("VBox/PlayersArea/Center/PenguinsRow");

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
		_statusValue.Text = snapshot.Status;
		_mapIndex = MapRegistry.IndexOf(snapshot.MapId);
		_RefreshMapDisplay();

		_mapPrevButton.Visible = LobbyState.IsHost;
		_mapNextButton.Visible = LobbyState.IsHost;
		_startButton.Visible   = LobbyState.IsHost;
		_startButton.Disabled  = !LobbyState.IsHost;

		RefreshPlayerDisplay(snapshot);

		var timer = new Timer { WaitTime = PollInterval, Autostart = true };
		timer.Timeout += OnPollTick;
		AddChild(timer);
	}

	// ── Penguin display ──────────────────────────────────────────────────────

	private void RefreshPlayerDisplay(RoomSnapshot snapshot)
	{
		var currentIds = new HashSet<string>(snapshot.Players.Keys);
		foreach (var id in _playerSlots.Keys.ToList())
		{
			if (!currentIds.Contains(id))
			{
				_playerSlots[id].QueueFree();
				_playerSlots.Remove(id);
			}
		}
		foreach (var (userId, entry) in snapshot.Players)
		{
			if (!_playerSlots.ContainsKey(userId))
				_playerSlots[userId] = _CreatePlayerSlot(entry.Username, entry.HatId, entry.IsHost);
		}
	}

	private const string PreviewScenePath = "res://Core/UI/Preview/preview.tscn";

	private Control _CreatePlayerSlot(string username, string hatId, bool isHost)
	{
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 2);

		var svContainer = new SubViewportContainer();
		svContainer.CustomMinimumSize = new Vector2(180, 260);
		svContainer.StretchShrink = 1;

		var viewport = new SubViewport();
		viewport.Size = new Vector2I(180, 260);
		viewport.TransparentBg = true;
		viewport.HandleInputLocally = false;
		viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		svContainer.AddChild(viewport);

		var packed = ResourceLoader.Load<PackedScene>(PreviewScenePath);
		if (packed != null)
		{
			var preview = packed.Instantiate<Preview>();
			// Static pour l'instant. Le head-sync entre peers (MouseLook pour le
			// slot local, NetworkedRemote pour les autres) viendra avec la mise
			// en place du mesh ENet au moment du lobby.
			preview.Mode = Preview.InteractionMode.Static;
			viewport.AddChild(preview);
			preview.SpawnCharacter(string.IsNullOrEmpty(hatId) ? HatRegistry.DefaultHatId : hatId);
		}
		else
		{
			GD.PrintErr($"[Lobby] Preview scene introuvable&#160;: {PreviewScenePath}");
		}

		var nameLabel = new Label();
		nameLabel.Text = isHost ? username + " ★" : username;
		nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		nameLabel.AddThemeFontSizeOverride("font_size", 18);

		vbox.AddChild(svContainer);
		vbox.AddChild(nameLabel);
		_penguinsRow.AddChild(vbox);
		return vbox;
	}

	private async void OnPollTick()
	{
		if (_leaving || !IsInsideTree()) return;

		var snapshot = LobbyState.Current;
		if (snapshot is null) return;

		try
		{
			var fresh = await RoomServiceProvider.Repository.GetAsync(snapshot.Code);
			if (_leaving || !IsInsideTree()) return;
			// fresh == null ⇒ la salle a été supprimée côté Firestore. Depuis
			// que GetAsync ne renvoie plus null sur erreur HTTP transitoire
			// (cf. RoomRepository), c'est désormais un signal fiable : l'hôte
			// a quitté et a nuke la salle (LobbyCleanup.LeaveRoomFireAndForget).
			// On ramène le non-hôte au main menu via ErrorDialog pour qu'il ne
			// reste pas coincé sur un lobby fantôme.
			if (fresh is null)
			{
				_leaving = true;
				LobbyState.Clear();
				ErrorDialog.Show(GetTree(),
					"L'hôte a quitté la partie. La salle a été fermée.",
					InOkText: "Retour au Menu Principal",
					InOnOk: GoToMainMenu,
					InOnClose: GoToMainMenu);
				return;
			}

			LobbyState.Set(fresh, LobbyState.IsHost);
			RefreshPlayerDisplay(fresh);
			_statusValue.Text = fresh.Status;

			// Non-host clients pick up map changes on each poll
			if (!LobbyState.IsHost)
			{
				_mapIndex = MapRegistry.IndexOf(fresh.MapId);
				_RefreshMapDisplay();
			}

			if (fresh.Status == "started" && !_leaving)
			{
				// Période de grâce : un re-entry depuis Winning peut voir
				// Status="started" en stale tant que l'hôte n'a pas reset
				// (async). Sans ce check, le polling de chaque non-hôte
				// redémarre la partie immédiatement.
				ulong sinceReady = Time.GetTicksMsec() - _instantiatedAtMsec;
				if (sinceReady < StartedTriggerGraceMsec) return;

				_leaving = true;
				_wasHost = LobbyState.IsHost;
				EmitSignal(SignalName.GameStartRequested);
			}
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
			if (_leaving) return;
			_leaving = true;
			_wasHost = LobbyState.IsHost;
			EmitSignal(SignalName.GameStartRequested);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[Lobby] Start failed: {ex.Message}");
			_startButton.Disabled = false;
		}
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
				Code = snapshot.Code,
				HostUserId = snapshot.HostUserId,
				ServerIp = snapshot.ServerIp,
				ServerPort = snapshot.ServerPort,
				Status = snapshot.Status,
				MaxPlayers = snapshot.MaxPlayers,
				MapId = newMapId,
				Players = snapshot.Players
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

	private void OnLeavePressed()
	{
		_leaving = true;
		_leaveButton.Disabled = true;

		// Hôte ⇒ supprime la salle ; non-hôte ⇒ retire juste son entrée.
		// Cf. LobbyCleanup pour la logique partagée avec les chemins
		// d'erreur du Lobby/Game et le bouton QUIT du Winning.
		LobbyCleanup.LeaveRoomFireAndForget();
		Core.Network.NetworkManager.Instance?.Disconnect();
		LobbyState.Clear();
		GoToMainMenu();
	}

	/// <summary>
	/// Retour au main menu&#160;: change de scène sans rien laisser derrière.
	/// Utilisé à la fois par <see cref="OnLeavePressed"/> et par le chemin
	/// «&#160;hôte a quitté → salle supprimée&#160;» de <see cref="OnPollTick"/>
	/// (callback de l'ErrorDialog). N'appelle pas <see cref="LobbyCleanup"/>&#160;:
	/// les deux appelants ont déjà fait leur ménage avant.
	/// </summary>
	private void GoToMainMenu()
	{
		if (!IsInsideTree()) return;
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}
}
