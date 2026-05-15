using Godot;
using System;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
using Core.UI.Loading;
using static Utils.CharacterUtils;
namespace Core.World;

/// <summary>
/// Phase «&#160;Game&#160;» de la FSM principale. Possède le <c>MapContainer</c>
/// (les affaires du réseau partagée) et y injecte le niveau correspondant à la map
/// choisie par l'hôte dans le lobby. Le root du niveau implémente
/// <see cref="IPhase"/> + <see cref="IGameMode"/> et fournit le mode de jeu —
/// GameController consomme uniquement ce contrat.
/// </summary>
public sealed partial class GameController : Node3D, IPhase
{
    /// <summary>Scène <c>map_container.tscn</c> instanciée à chaque entrée de la phase.</summary>
    [Export] private PackedScene _mapContainerSceneAsset;

    /// <summary>Scène <c>Player.tscn</c> utilisée par le <c>MultiplayerSpawner</c>.</summary>
    [Export] private PackedScene _playerScene;

    /// <summary>HUD in-game (<c>ui_ingame.tscn</c>) instancié pour la durée de la phase.</summary>
    [Export] private PackedScene _uiIngameScene;

    /// <summary>Overlay de mort (<c>wasted.tscn</c>) affiché par-dessus le HUD quand le joueur local meurt.</summary>
    [Export] private PackedScene _wastedScene;

	/// <summary>Durée de l'écran d'attente (countdown) avant de démarrer le mode.</summary>
    [Export] private float _waitingDuration = 10f;

	/// <summary>
	/// Durée du palier «&#160;GAME OVER&#160;» en phase <c>Resolving</c>&#160;: le monde
	/// reste visible derrière l'overlay <c>wasted.tscn</c> dont le label est
	/// réécrit avec «&#160;GAME OVER&#160;», pour laisser le temps de sons/animations
	/// avant le passage à la phase Winning. Secondes.
	/// </summary>
	[Export] private float _gameOverDuration = 3f;

	public enum State { Init, Failure, Waiting, Playing, Resolving, Finalize }
	private StateMachine<State> _fsm = null;

	private IPhase _mode = null;
	private Node _mapContainerInstance = null;
	private MultiplayerSpawner _spawner = null;
	private PlayerSpawner _playerSpawner = null;
	private MultiplayerApi.PeerDisconnectedEventHandler _onPeerDisconnectedHandler = null;
	private bool _done;
	public bool IsDone => _done;

	// ── HUD in-game ──────────────────────────────────────────────────────────
	/// <summary>Durée du fondu d'apparition/disparition des panneaux du HUD (secondes).</summary>
	[Export] private float _hudFadeDuration = 0.3f;

	private Control _uiInstance = null;
	private Control _countdownPanel = null;
	private Control _timerPanel = null;
	private Label _countdownLabel = null;
	private Label _timerLabel = null;
	private TimeElapsedCondition<State> _waitingCondition = null;
	private Tween _countdownTween = null;
	private Tween _timerTween = null;
	private bool _countdownShown;
	private bool _timerShown;

	// ── Overlay wasted + lien joueur local ────────────────────────────────────
	private Control _wastedInstance = null;
	private Player _localPlayer = null;
	private bool _localPlayerDead;

	public override void _Ready()
	{
		//Fallback d'export scene
		_mapContainerSceneAsset ??= ResourceLoader.Load<PackedScene>("res://Core/World/Maps/map_container.tscn");
		_playerScene ??= ResourceLoader.Load<PackedScene>("res://Core/World/CharacterModel/Player/Player.tscn");
		_uiIngameScene ??= ResourceLoader.Load<PackedScene>("res://Core/UI/InGame/ui_ingame.tscn");
		_wastedScene ??= ResourceLoader.Load<PackedScene>("res://Core/UI/InGame/wasted.tscn");
	}

	public void Enter()
	{
		_done = false;
		_localPlayerDead = false;

		// Filet de sécurité : si on entre dans Game sans être passé par OnGameStartRequested
		// (ex. cycle Winning → Game ou retry), on s'assure que l'overlay est visible
		// jusqu'à ce que UpdateLoadingOverlay détecte le niveau et le joueur prêts.
		if (!IsDedicatedServer())
		{
			LoadingScreen.Show(GetTree());
			LoadingScreen.SetStatus("Chargement de la carte", 0.55f);
		}

		// la scene de map container est loaded
		if (_mapContainerSceneAsset is null)
		{
			GD.PrintErr("[GameController] _mapContainerSceneAsset non assigné.");
			_done = true;
			return;
		}
		_mapContainerInstance = _mapContainerSceneAsset.Instantiate();
		AddChild(_mapContainerInstance);
		// Instancie le HUD in-game. Le Control reste caché tant qu'aucun mode
		// n'est chargé&#160;: l'affichage est piloté depuis Tick() selon l'état FSM.
		SpawnIngameUi();
		// prendre le spawner
		_spawner = _mapContainerInstance.GetNodeOrNull<MultiplayerSpawner>("GameLogicAssets/MultiplayerSpawner");
		if (_spawner is null)
		{
			GD.PrintErr("[GameController] MultiplayerSpawner introuvable dans map_container.");
			_done = true;
			return;
		}
		_spawner.SpawnFunction = Callable.From<Variant, GodotObject>(SpawnPlayerNode);

		var net = NetworkManager.Instance;
		if (net is not null) net.StateReceived += OnStateReceived;

		// Vérifier le role (client vs server)
		if (net is not null && net.IsServer)
		{
			GD.Print("[GameController] Dedicated server — waiting for HostMapPick.");
			_onPeerDisconnectedHandler = OnPeerDisconnected;
			Multiplayer.PeerDisconnected += _onPeerDisconnectedHandler;
			// Le niveau est loadé a la réception du HostMapPick
		}
		else if (net is not null && net.IsClient)
		{
			if (!LoadLevel(LobbyState.SelectedMapId)) return;
			GD.Print($"[GameController] Online client — sending HostMapPick('{LobbyState.SelectedMapId}') + ClientReady.");
			RpcId(1, MethodName.HostMapPick, LobbyState.SelectedMapId);
			Rpc(MethodName.ClientReady);
		}
		else
		{
			if (!LoadLevel(LobbyState.SelectedMapId)) return;
			GD.Print("[GameController] Offline — spawning local player directly.");
			SpawnOffline();
		}
	}

	/// <summary>
	/// Charge le niveau correspondant à <paramref name="InMapId"/> dans le slot
	/// <c>Map</c> du container, branche <see cref="_mode"/>/<see cref="_playerSpawner"/>
	/// et démarre la State machine interne. Idempotent : retourne <c>true</c> sans rien
	/// refaire si un niveau est déjà chargé.
	/// </summary>
	private bool LoadLevel(string InMapId)
	{
		if (_mapContainerInstance is null) return false;
		if (_mode is not null) return true;

		var mapDef = MapRegistry.Get(InMapId) ?? MapRegistry.All[0];
		LoadingScreen.SetStatus($"Chargement de la carte&#160;: {mapDef.DisplayName}", 0.65f);

		var levelScene = ResourceLoader.Load<PackedScene>(mapDef.ScenePath);
		if (levelScene is null)
		{
			GD.PrintErr($"[GameController] Niveau introuvable&#160;: {mapDef.ScenePath}");
			_done = true;
			return false;
		}
		var levelInstance = levelScene.Instantiate();
		var mapSlot = _mapContainerInstance.GetNodeOrNull("Map");
		if (mapSlot is null)
		{
			GD.PrintErr("[GameController] Slot 'Map' introuvable dans map_container.tscn.");
			_done = true;
			return false;
		}
		mapSlot.AddChild(levelInstance);
		LoadingScreen.SetStatus("En attente du joueur", 0.85f);

		if (levelInstance is not IPhase mode)
		{
			GD.PrintErr($"[GameController] Le root du niveau '{mapDef.ScenePath}' n'implémente pas IPhase.");
			_done = true;
			return false;
		}
		_mode = mode;

		_playerSpawner = levelInstance.GetNodeOrNull<PlayerSpawner>("PlayerSpawner");
		if (_playerSpawner is null)
			GD.PrintErr($"[GameController] PlayerSpawner introuvable dans le niveau '{mapDef.ScenePath}'.");

		// FSM interne (Init → Waiting → Playing → Resolving). Démarrée ici parce
		// que le prédicat Playing→Resolving dépend de _mode.IsDone.
		_waitingCondition = new TimeElapsedCondition<State>(_waitingDuration);
		_fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);
		_fsm.When(State.Init,
			new PredicateCondition<State>(() =>/* TODO: Inclure level loaded */ true),
			State.Waiting
		);
		_fsm.When(State.Init,
			new PredicateCondition<State>(() => /* TODO: flag d'erreur de chargement */ false),
			State.Failure
		);
		_fsm.When(State.Waiting,
			_waitingCondition,
			State.Playing
		);
		_fsm.When(State.Playing,
			new PredicateCondition<State>(() => _mode.IsDone),
			State.Resolving
		);
		_fsm.When(State.Resolving,
			new TimeElapsedCondition<State>(_gameOverDuration),
			State.Finalize
		);
		OnSubEnter(State.Init);
		return true;
	}

    public void Tick(float InDelta)
    {
		// Le link au joueur local et la mise à jour de l'overlay loading doivent
		// tourner même si la FSM n'est pas encore créée (LoadLevel pas terminé).
        UpdateLocalPlayerLink();
        UpdateLoadingOverlay();
        if (_fsm is null) return;
        if (_fsm.Is(State.Playing)) _mode?.Tick(InDelta);
        _fsm.Tick(InDelta);
        UpdateIngameUi();
    }

    public void Exit()
    {
        var net = NetworkManager.Instance;
        if (net is not null) net.StateReceived -= OnStateReceived;
        if (_onPeerDisconnectedHandler is not null)
        {
            Multiplayer.PeerDisconnected -= _onPeerDisconnectedHandler;
            _onPeerDisconnectedHandler = null;
        }

		// Le Player local capture la souris au _Ready (FPS) et personne ne la
		// libère à la destruction du joueur. Sans ce reset, l'écran Winning
		// hérite d'un curseur masqué/capturé et devient incliquable.
		if (!IsDedicatedServer()) Input.MouseMode = Input.MouseModeEnum.Visible;

        if (_fsm is not null && _fsm.Is(State.Playing)) _mode?.Exit();
        _countdownTween?.Kill();
        _timerTween?.Kill();
        _countdownTween = null;
        _timerTween = null;
        if (_uiInstance is not null)
        {
            _uiInstance.QueueFree();
            _uiInstance = null;
        }
		// Sécurité&#160;: si l'overlay est encore visible au moment où on quitte la phase
		// (ex. échec de chargement avant que UpdateLoadingOverlay puisse Hide()),
		// on le cache ici pour ne pas le traîner jusqu'au Winning.
        LoadingScreen.Hide();
        if (_wastedInstance is not null)
        {
            _wastedInstance.QueueFree();
            _wastedInstance = null;
        }
        if (_localPlayer is not null)
        {
            _localPlayer.Died -= OnLocalPlayerDied;
            _localPlayer = null;
        }
        _localPlayerDead = false;
        _countdownPanel = null;
        _timerPanel = null;
        _countdownLabel = null;
        _timerLabel = null;
        _countdownShown = false;
        _timerShown = false;
        _waitingCondition = null;
        if (_mapContainerInstance is not null)
        {
            _mapContainerInstance.QueueFree();
            _mapContainerInstance = null;
        }
        _spawner = null;
        _playerSpawner = null;
        _mode = null;
        _fsm = null;
    }

    // ── Spawn flow (porté de World.cs) ────────────────────────────────────────

    private GodotObject SpawnPlayerNode(Variant data)
    {
        var dict = data.As<Godot.Collections.Dictionary>();
		int peerId = dict["id"].As<int>();
		Vector3 pos = dict["pos"].As<Vector3>();

        var player = _playerScene.Instantiate<Player>();
        player.Name = peerId.ToString();
        player.PeerId = peerId;
        player.SetMultiplayerAuthority(peerId);
        player.SpawnPosition = pos;

		GD.Print($"[GameController] SpawnPlayerNode&#160;: peerId={peerId}, pos={pos}, isLocal={player.IsMultiplayerAuthority()}");
        return player;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReady()
    {
        if (!Multiplayer.IsServer()) return;
        int peerId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[GameController] ClientReady from peer {peerId}");
        ServerSpawnPeer(peerId);
    }

    /// <summary>
	/// Envoyé par chaque client à l'entrée en phase Game pour annoncer au
	/// serveur quelle map a été choisie dans le lobby. Le serveur charge le
	/// niveau au premier appel (le RPC est réémis par chaque peer, mais
	/// <see cref="LoadLevel"/> est idempotent).
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HostMapPick(string InMapId)
	{
		if (!Multiplayer.IsServer()) return;
		if (_mode is not null) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[GameController] HostMapPick from peer {senderId}: mapId='{InMapId}'");
		LoadLevel(InMapId);
	}

	private void ServerSpawnPeer(int peerId)
	{
		if (_spawner is null || _playerSpawner is null) return;
		Vector3 spawnPos = _playerSpawner.GetNextSpawnPoint();
		var data = new Godot.Collections.Dictionary { ["id"] = peerId, ["pos"] = spawnPos };
		_spawner.Spawn(data);
		// Donne au NetworkManager le spawn par peer pour son respawn autoritaire
		// (chute sous FallThreshold dans OnPacketReceived).
		NetworkManager.Instance?.RegisterPeerSpawn(peerId, spawnPos);
		GD.Print($"[GameController] Server spawned peer {peerId} at {spawnPos}");
	}

	private void OnPeerDisconnected(long peerId)
	{
		var players = _mapContainerInstance?.GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull(((int)peerId).ToString());
		if (player is not null)
		{
			((Node)player).QueueFree();
			GD.Print($"[GameController] Server despawned peer {(int)peerId}");
		}

		// Quand tout les clients se déconnectent, automatiquement finir la game nul et
		// attendre un autre lobby.
		if (Multiplayer.GetPeers().Length == 0)
		{
			GD.Print("[GameController] Last peer left — resetting server to waiting.");
			_done = true;
		}
	}

	private void SpawnOffline()
	{
		if (_playerScene is null || _mapContainerInstance is null) return;
		var player = _playerScene.Instantiate<Player>();
		player.Name = "1";
		player.PeerId = 1;
		player.SpawnPosition = _playerSpawner?.GetNextSpawnPoint() ?? Vector3.Zero;
		var players = _mapContainerInstance.GetNodeOrNull("Players");
		if (players is null)
		{
			GD.PrintErr("[GameController] Container 'Players' introuvable dans map_container.");
			return;
		}
		players.AddChild(player);
	}

	private void OnStateReceived(PlayerNetState state)
	{
		var players = _mapContainerInstance?.GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull<Player>(state.PeerId.ToString());
		if (player is null || player.IsMultiplayerAuthority()) return;
		player.PushSnapshot(state, Time.GetTicksMsec());
	}

	// ── FSM interne ───────────────────────────────────────────────────────────

	private void OnSubEnter(State s)
	{
		switch (s)
		{
			case State.Playing: _mode?.Enter(); break;
			case State.Resolving: ShowGameOverOverlay(); break;
			case State.Finalize: _done = true; break;
		}
	}
	private void OnSubExit(State _) { }

	// ── HUD in-game ───────────────────────────────────────────────────────────

	/// <summary>
	/// Instancie <c>ui_ingame.tscn</c> sous le GameController et résout les
	/// références vers les labels qui seront mis à jour à chaque tick. Les deux
	/// conteneurs sont initialisés en <c>Visible=false</c> + <c>modulate.a=0</c>
	/// pour permettre un fondu d'entrée propre au premier appel à
	/// <see cref="FadePanel"/>.
	/// </summary>
	private void SpawnIngameUi()
	{
		if (_uiIngameScene is null) return;
		_uiInstance = _uiIngameScene.Instantiate<Control>();
		AddChild(_uiInstance);
		_countdownPanel = _uiInstance.GetNodeOrNull<Control>("CountdownContainer");
		_timerPanel = _uiInstance.GetNodeOrNull<Control>("TimerContainer");
		_countdownLabel = _uiInstance.GetNodeOrNull<Label>("CountdownContainer/Label");
		_timerLabel = _uiInstance.GetNodeOrNull<Label>("TimerContainer/Panel/Label");
		InitHidden(_countdownPanel);
		InitHidden(_timerPanel);
		_countdownShown = false;
		_timerShown = false;
	}

	/// <summary>État de départ d'un panneau&#160;: invisible et complètement transparent.</summary>
	private static void InitHidden(Control InPanel)
	{
		if (InPanel is null) return;
		InPanel.Visible = false;
		var m = InPanel.Modulate;
		m.A = 0f;
		InPanel.Modulate = m;
	}

	/// <summary>
	/// Met à jour le HUD selon l'état FSM&#160;: countdown pendant <c>Waiting</c>,
	/// chrono restant pendant <c>Playing</c> (lu depuis <see cref="IGameMode.RemainingSeconds"/>),
	/// les deux cachés sinon. La transition d'affichage est faite en fondu via
	/// <see cref="FadePanel"/>.
	/// </summary>
	private void UpdateIngameUi()
	{
		if (_uiInstance is null || _fsm is null) return;

		bool inWaiting = _fsm.Is(State.Waiting);
		bool inPlaying = _fsm.Is(State.Playing);

		FadePanel(_countdownPanel, ref _countdownTween, ref _countdownShown, inWaiting);
		FadePanel(_timerPanel, ref _timerTween, ref _timerShown, inPlaying);

		if (inWaiting && _countdownLabel is not null && _waitingCondition is not null)
		{
			int secs = Mathf.CeilToInt(_waitingCondition.Remaining);
			_countdownLabel.Text = secs.ToString();
		}

		if (inPlaying && _timerLabel is not null && _mode is IGameMode mode)
		{
			float rem = mode.RemainingSeconds;
			if (rem <= 0f)
			{
				_timerLabel.Text = "TIME LEFT : --:--";
			}
			else
			{
				int total = Mathf.CeilToInt(rem);
				int m = total / 60;
				int s = total % 60;
				_timerLabel.Text = $"TIME LEFT : {m:00}:{s:00}";
			}
		}
	}

	// ── Overlay de chargement ─────────────────────────────────────────────────

	/// <summary>
	/// Cache l'overlay <see cref="LoadingScreen"/> (affiché depuis le lobby au clic
	/// «&#160;Start&#160;») dès que les pré-requis sont remplis&#160;: le niveau est instancié
	/// (<see cref="_mode"/> non null) ET le joueur local a été spawné par le
	/// <c>MultiplayerSpawner</c>. Sur serveur dédié, le joueur local n'existe pas&#160;:
	/// on ne se base que sur le niveau.
	/// </summary>
	private void UpdateLoadingOverlay()
	{
		if (!LoadingScreen.IsVisible) return;
		bool levelReady = _mode is not null;
		bool playerReady = IsDedicatedServer() || _localPlayer is not null;
		if (!levelReady || !playerReady) return;

		// Remplit la jauge à 100&#160;% avant de cacher pour que la disparition soit nette
		// (et pour que les joueurs voient bien la barre arriver au bout).
		LoadingScreen.SetStatus("Prêt", 1f);
		LoadingScreen.Hide();
	}

	/// <summary>
	/// Indique si ce process tourne en mode serveur dédié (headless ou flag <c>--server</c>).
	/// Le joueur local n'existe pas dans ce mode&#160;: les overlays joueur (loading, wasted)
	/// sont skippés.
	/// </summary>
	private static bool IsDedicatedServer()
	{
		var net = NetworkManager.Instance;
		if (net is null) return false;
		if (!net.IsServer) return false;
		return DisplayServer.GetName() == "headless" || OS.HasFeature("dedicated_server");
	}

	// ── Lien joueur local + overlay de mort ───────────────────────────────────

	/// <summary>
	/// Cherche le joueur local dans le groupe <c>local_player</c> (ajouté par
	/// <c>Player._Ready</c> côté authority) et s'abonne à son événement <c>Died</c>
	/// la première fois qu'on le trouve. Idempotent une fois la liaison faite.
	/// </summary>
	private void UpdateLocalPlayerLink()
	{
		if (_localPlayer is not null) return;
		var found = GetTree().GetFirstNodeInGroup("local_player") as Player;
		if (found is null) return;
		_localPlayer = found;
		_localPlayer.Died += OnLocalPlayerDied;
	}

	/// <summary>
	/// Handler du <see cref="Character.Died"/> du joueur local. Bascule l'UI en mode
	/// «&#160;Wasted&#160;»&#160;: HUD caché, overlay de mort affiché par-dessus.
	/// </summary>
	private void OnLocalPlayerDied(int InPeerId, DeathReason InReason)
	{
		GD.Print($"[GameController] Local player {InPeerId} died ({InReason}).");
		_localPlayerDead = true;
		ShowWastedOverlay();
	}

	/// <summary>
	/// Affiche l'overlay <c>wasted.tscn</c> et cache complètement le HUD in-game.
	/// Si <paramref name="InTextOverride"/> est fourni, le label central est
	/// réécrit (sinon le texte par défaut «&#160;WASTED&#160;» de la scène est
	/// conservé). Si l'overlay est déjà actif et qu'un nouveau texte est passé,
	/// le label est simplement mis à jour&#160;: utile pour passer de «&#160;WASTED&#160;»
	/// à «&#160;GAME OVER&#160;» quand le mode se termine alors que le joueur local
	/// était déjà mort.
	/// </summary>
	private void ShowWastedOverlay(string InTextOverride = null)
	{
		if (_wastedInstance is null)
		{
			if (_wastedScene is null) return;
			_wastedInstance = _wastedScene.Instantiate<Control>();
			AddChild(_wastedInstance);
			if (_uiInstance is not null) _uiInstance.Visible = false;
		}
		if (InTextOverride is not null)
		{
			var label = _wastedInstance.GetNodeOrNull<Label>("MarginContainer/Label");
			if (label is not null) label.Text = InTextOverride;
		}
	}

	/// <summary>
	/// Affiche l'overlay <c>wasted.tscn</c> avec le texte «&#160;GAME OVER&#160;».
	/// Appelé à l'entrée en phase <c>Resolving</c> pour marquer la fin de la
	/// partie pendant que le monde reste visible en arrière-plan.
	/// </summary>
	private void ShowGameOverOverlay() => ShowWastedOverlay("GAME OVER");

	/// <summary>
	/// Fait apparaître ou disparaître un panneau du HUD en fondu sur
	/// <see cref="_hudFadeDuration"/> secondes. Idempotent&#160;: ne fait rien si
	/// l'état demandé est déjà l'état courant. <c>Visible</c> est forcé à
	/// <c>true</c> avant un fade-in pour que la transparence soit visible, et
	/// rebasculé à <c>false</c> en fin de fade-out pour ne pas intercepter
	/// les inputs.
	/// </summary>
	private void FadePanel(Control InPanel, ref Tween InOutTween, ref bool InOutShown, bool InTarget)
	{
		if (InPanel is null) return;
		if (InOutShown == InTarget) return;
		InOutShown = InTarget;

		InOutTween?.Kill();
		InOutTween = CreateTween();

		if (InTarget)
		{
			InPanel.Visible = true;
			InOutTween.TweenProperty(InPanel, "modulate:a", 1f, _hudFadeDuration);
		}
		else
		{
			Control captured = InPanel;
			InOutTween.TweenProperty(InPanel, "modulate:a", 0f, _hudFadeDuration);
			InOutTween.TweenCallback(Callable.From(() =>
			{
				if (IsInstanceValid(captured)) captured.Visible = false;
			}));
		}
	}
}
