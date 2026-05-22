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

    private Label _codeValue = null!;
    private Label _serverValue = null!;
    private Label _statusValue = null!;
    private Label _mapValue = null!;
    private Button _mapPrevButton = null!;
    private Button _mapNextButton = null!;
    private Label _hatValue = null!;
    private Button _hatPrevButton = null!;
    private Button _hatNextButton = null!;
    private Label _playersTitle = null!;
    private VBoxContainer _playersList = null!;
    private Button _leaveButton = null!;
    private Button _startButton = null!;

    private bool _leaving = false;
    private bool _wasHost = false;
    private int _mapIndex = 0;
    private int _hatIndex = 0;
    private ulong _instantiatedAtMsec;
    private const string Root = "PanelContainer/MarginContainer/VBoxContainer";
    private const float PollInterval = 4.0f;
    /// <summary>
    /// Période de grâce après instanciation pendant laquelle on ignore un
    /// statut "started" lu depuis Firestore. Évite qu'un non-hôte re-entrant
    /// dans le lobby (cycle Winning → Lobby) déclenche immédiatement
    /// GameStartRequested sur un snapshot encore stale, avant que l'hôte
    /// ait eu le temps de remettre le statut à "waiting".
    /// </summary>
    private const ulong StartedTriggerGraceMsec = 4_000;

    public override void _Ready()
    {
        _instantiatedAtMsec = Time.GetTicksMsec();
        _codeValue = GetNode<Label>($"{Root}/CodeBox/CodeValue");
        _serverValue = GetNode<Label>($"{Root}/ServerBox/ServerValue");
        _statusValue = GetNode<Label>($"{Root}/StatusBox/StatusValue");
        _mapValue = GetNode<Label>($"{Root}/MapBox/MapValue");
        _mapPrevButton = GetNode<Button>($"{Root}/MapBox/MapPrev");
        _mapNextButton = GetNode<Button>($"{Root}/MapBox/MapNext");
        _hatValue = GetNode<Label>($"{Root}/HatBox/HatValue");
        _hatPrevButton = GetNode<Button>($"{Root}/HatBox/HatPrev");
        _hatNextButton = GetNode<Button>($"{Root}/HatBox/HatNext");
        _playersTitle = GetNode<Label>($"{Root}/PlayersTitle");
        _playersList = GetNode<VBoxContainer>($"{Root}/PlayersPanel/ScrollContainer/PlayersList");
        _leaveButton = GetNode<Button>($"{Root}/ButtonsBox/LeaveButton");
        _startButton = GetNode<Button>($"{Root}/ButtonsBox/StartButton");

        _leaveButton.Pressed += OnLeavePressed;
        _startButton.Pressed += OnStartPressed;
        _mapPrevButton.Pressed += OnMapPrev;
        _mapNextButton.Pressed += OnMapNext;
        _hatPrevButton.Pressed += OnHatPrev;
        _hatNextButton.Pressed += OnHatNext;

        var snapshot = LobbyState.Current;
        if (snapshot is null)
        {
            GD.PrintErr("[Lobby] No lobby state, going back.");
            GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
            return;
        }

        _codeValue.Text = snapshot.Code;
        _serverValue.Text = $"{snapshot.ServerIp}:{snapshot.ServerPort}";
        _statusValue.Text = snapshot.Status;

        _mapIndex = MapRegistry.IndexOf(snapshot.MapId);
        _RefreshMapDisplay();

        // Only the host can change the map
        _mapPrevButton.Visible = LobbyState.IsHost;
        _mapNextButton.Visible = LobbyState.IsHost;

        // Chapeau : chacun choisit le sien — toujours visible et toujours actif.
        _hatIndex = HatRegistry.IndexOf(_LocalHatIdFromSnapshot(snapshot));
        _RefreshHatDisplay();

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
            RefreshPlayerList(fresh);
            _statusValue.Text = fresh.Status;

            // Non-host clients pick up map changes on each poll
            if (!LobbyState.IsHost)
            {
                _mapIndex = MapRegistry.IndexOf(fresh.MapId);
                _RefreshMapDisplay();
            }

            // Le chapeau du joueur local peut avoir été modifié depuis une
            // autre session : on resynchronise l'affichage sur la valeur
            // canonique de Firestore.
            _hatIndex = HatRegistry.IndexOf(_LocalHatIdFromSnapshot(fresh));
            _RefreshHatDisplay();

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

    private void _RefreshHatDisplay()
    {
        _hatValue.Text = HatRegistry.All[_hatIndex].DisplayName;
    }

    private async void OnHatPrev()
    {
        _hatIndex = (_hatIndex - 1 + HatRegistry.All.Length) % HatRegistry.All.Length;
        _RefreshHatDisplay();
        await _PushHatUpdate();
    }

    private async void OnHatNext()
    {
        _hatIndex = (_hatIndex + 1) % HatRegistry.All.Length;
        _RefreshHatDisplay();
        await _PushHatUpdate();
    }

    /// <summary>
    /// Renvoie le HatId du joueur local dans le snapshot fourni, ou le défaut
    /// si l'utilisateur n'est pas authentifié / n'a pas encore d'entrée.
    /// </summary>
    private static string _LocalHatIdFromSnapshot(RoomSnapshot snapshot)
    {
        var me = Core.Auth.AuthServiceProvider.Instance.CurrentUser;
        if (me is null) return HatRegistry.DefaultHatId;
        if (!snapshot.Players.TryGetValue(me.Id, out var entry)) return HatRegistry.DefaultHatId;
        return string.IsNullOrEmpty(entry.HatId) ? HatRegistry.DefaultHatId : entry.HatId;
    }

    private async System.Threading.Tasks.Task _PushHatUpdate()
    {
        var snapshot = LobbyState.Current;
        var me = Core.Auth.AuthServiceProvider.Instance.CurrentUser;
        if (snapshot is null || me is null) return;
        _hatPrevButton.Disabled = true;
        _hatNextButton.Disabled = true;
        try
        {
            var newHatId = HatRegistry.All[_hatIndex].Id;
            await RoomServiceProvider.Repository.UpdateHatAsync(snapshot.Code, me.Id, newHatId);

            // Mise à jour locale du snapshot pour qu'un futur poll ne réécrase
            // pas le choix avec un état stale, et pour que LobbyController.Enter
            // lise la bonne valeur s'il s'exécute avant le prochain poll.
            if (snapshot.Players.TryGetValue(me.Id, out var existing))
            {
                snapshot.Players[me.Id] = new RoomSnapshot.PlayerEntry
                {
                    Username = existing.Username,
                    IsHost = existing.IsHost,
                    HatId = newHatId
                };
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Lobby] Hat update failed: {ex.Message}");
        }
        finally
        {
            _hatPrevButton.Disabled = false;
            _hatNextButton.Disabled = false;
        }
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
