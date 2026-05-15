using Godot;
using System;
using System.Collections.Generic;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
using Core.Stats;
using Core.Shared.Infrastructure;
using Core.UI.Loading;
using Config;
using Core.Auth;
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

	public enum State { Init, Failure, Waiting, Playing, Resolving, Finalize, Aborted }
	private StateMachine<State> _fsm = null;

	private IPhase _mode = null;
	private Node _mapContainerInstance = null;
	private MultiplayerSpawner _spawner = null;
	private PlayerSpawner _playerSpawner = null;
	private MultiplayerApi.PeerDisconnectedEventHandler _onPeerDisconnectedHandler = null;
	private bool _done;
	public bool IsDone => _done;

	// ── Host tracking + partie avortée ───────────────────────────────────────

	/// <summary>
	/// Peer ID du joueur déclaré hôte du lobby (transmis via le RPC <see cref="ClientReady"/>).
	/// Réinitialisé à 0 entre chaque partie. Sert uniquement côté serveur pour décider quand
	/// passer en état <see cref="State.Aborted"/>.
	/// </summary>
	private int _hostPeerId;

	/// <summary>
	/// Drapeau levé quand la partie a été avortée (host parti). Permet au MainController
	/// d'aiguiller le post-game vers le bon flux quand le futur dialog scene sera prêt.
    /// </summary>
    private bool _aborted;
    public bool WasAborted => _aborted;

    // ── Stats et identifiant de partie ───────────────────────────────────────

    /// <summary>
	/// Temps écoulé depuis l'entrée en phase <see cref="State.Playing"/> (secondes).
	/// Exposé en static pour que <see cref="Character.Die"/> puisse l'horodater sans avoir
	/// besoin d'une référence vers cette classe.
	/// </summary>
	public static float GameElapsedSeconds { get; private set; }

	/// <summary>Identifiant unique de la partie courante. Généré à <see cref="Enter"/> côté serveur, broadcasté aux clients.</summary>
	private string _gameId = "";

	/// <summary>Stats agrégées côté serveur. Une entrée par peer ayant soumis via <see cref="SubmitStats"/>.</summary>
	private readonly Dictionary<int, PlayerGameStats> _statsByPeer = new();

	/// <summary>Repo Firestore lazy-initialisé côté client pour les écritures de fin de partie.</summary>
	private GameStatsRepository _statsRepo;

	/// <summary>Algorithme de résolution&#160;: instancié une fois, sans état partagé entre parties.</summary>
	private readonly SubwinningResolver _resolver = new();

	/// <summary>Indique si le serveur a déjà broadcasté les résultats (évite double-envoi en cas de tick rapproché).</summary>
	private bool _winnerBroadcasted;

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
    private bool _alivePeers { set; get; }


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
		_aborted = false;
		_hostPeerId = 0;
		_winnerBroadcasted = false;
		GameElapsedSeconds = 0f;
		_statsByPeer.Clear();
		// Le serveur génère l'identifiant de partie et le diffusera au moment de
        // la résolution. Les clients reçoivent _gameId via NetApplyWinnerData.
        var netInit = NetworkManager.Instance;
        if (netInit is not null && netInit.IsServer) _gameId = Guid.NewGuid().ToString("N");
        else _gameId = "";

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
			//GD.Print("[GameController] Dedicated server — waiting for HostMapPick.");
			_onPeerDisconnectedHandler = OnPeerDisconnected;
			Multiplayer.PeerDisconnected += _onPeerDisconnectedHandler;
			// Le niveau est loadé a la réception du HostMapPick
		}
		else if (net is not null && net.IsClient)
		{
			if (!LoadLevel(LobbyState.SelectedMapId)) return;
			//GD.Print($"[GameController] Online client — sending HostMapPick('{LobbyState.SelectedMapId}') + ClientReady(isHost={LobbyState.IsHost}).");
			RpcId(1, MethodName.HostMapPick, LobbyState.SelectedMapId);
			RpcId(1, MethodName.ClientReady, LobbyState.IsHost);
		}
		else
		{
			if (!LoadLevel(LobbyState.SelectedMapId)) return;
			//GD.Print("[GameController] Offline — spawning local player directly.");
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
        // État terminal&#160;: le monde est figé (level ticks coupés), le HUD reste sur
		// l'overlay <c>wasted.tscn</c>. On laisse <see cref="OnPeerDisconnected"/>
		// continuer à tourner via le signal Godot mais aucune transition FSM n'est
		// évaluée jusqu'à la sortie manuelle de la phase.
		if (_fsm.Is(State.Aborted)) return;
		if (_fsm.Is(State.Playing))
		{
			GameElapsedSeconds += InDelta;
			_mode?.Tick(InDelta);
		}
		_fsm.Tick(InDelta);
		UpdateIngameUi();
	}

	public void Exit()
	{
		var net = NetworkManager.Instance;
		if (net is not null)
		{
			net.StateReceived -= OnStateReceived;

		}
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
		_hostPeerId = 0;
		_aborted = false;
		_winnerBroadcasted = false;
		_statsByPeer.Clear();
		_gameId = "";
		GameElapsedSeconds = 0f;
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

		//GD.Print($"[GameController] SpawnPlayerNode&#160;: peerId={peerId}, pos={pos}, isLocal={player.IsMultiplayerAuthority()}");
		return player;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientReady(bool InIsHost)
	{
		if (!Multiplayer.IsServer()) return;
		int peerId = Multiplayer.GetRemoteSenderId();
		// Premier «&#160;je suis l'hôte&#160;» reçu gagne&#160;: prévient un peer malveillant
        // qui usurperait le rôle, et évite que deux clients revendiquant le titre
        // se neutralisent. Réinitialisé à 0 entre chaque partie via Enter/Exit.
        if (InIsHost && _hostPeerId == 0)
        {
            _hostPeerId = peerId;
            //GD.Print($"[GameController] Host registered: peer {peerId}");
        }
        //GD.Print($"[GameController] ClientReady from peer {peerId} (isHost={InIsHost})");
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
		//GD.Print($"[GameController] HostMapPick from peer {senderId}: mapId='{InMapId}'");
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
		//GD.Print($"[GameController] Server spawned peer {peerId} at {spawnPos}");
	}

	private void OnPeerDisconnected(long peerId)
	{
		var players = _mapContainerInstance?.GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull(((int)peerId).ToString());
		if (player is not null)
		{
			((Node)player).QueueFree();
			//GD.Print($"[GameController] Server despawned peer {(int)peerId}");
		}

		// Si l'hôte du lobby quitte mid-game, la partie est avortée&#160;: on bascule
        // immédiatement en état Aborted (terminal), on fige le monde et on
		// broadcaste l'overlay aux clients restants. Cette logique passe AVANT
		// le check «&#160;dernier peer parti&#160;» pour bien marquer l'aborted comme cause.
        if ((int)peerId == _hostPeerId && _hostPeerId != 0 && !_aborted)
        {
            //GD.Print($"[GameController] Host peer {(int)peerId} left — entering Aborted.");
            if (_fsm is not null) _fsm.TransitionTo(State.Aborted);
            else OnSubEnter(State.Aborted);
            return;
        }

        // Quand tout les clients se déconnectent, automatiquement finir la game nul et
        // attendre un autre lobby.
        if (Multiplayer.GetPeers().Length == 0)
        {
            //GD.Print("[GameController] Last peer left — resetting server to waiting.");
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
            case State.Resolving:
                ShowGameOverOverlay();
                OnEnterResolving();
                break;
            case State.Finalize:
                OnEnterFinalize();
                _done = true;
                break;
            case State.Aborted: OnEnterAborted(); break;
        }
    }
    private void OnSubExit(State _) { }

    // ── Aborted (host parti) ──────────────────────────────────────────────────

    /// <summary>
    /// Entrée en état Aborted&#160;: terminal pour cette phase. Le serveur broadcaste
	/// l'overlay aux clients&#160;; tous les peers (serveur via <c>CallLocal</c>) figent
	/// l'input du joueur local et affichent <c>wasted.tscn</c> avec un libellé dédié.
	/// <see cref="_done"/> n'est PAS levé&#160;: on reste figé jusqu'à ce qu'un futur
	/// dialog scene déclenche le retour au lobby manuellement (cf. <see cref="WasAborted"/>).
	/// </summary>
	private void OnEnterAborted()
	{
		_aborted = true;
		var net = NetworkManager.Instance;
		if (net is not null && net.IsServer)
		{
			Rpc(MethodName.NetEnterAborted);
			ApplyAbortedLocal(); // serveur dédié l'applique aussi (no-op visuel si headless)
        }
        else
        {
            ApplyAbortedLocal();
        }
    }

    /// <summary>
	/// Application locale de l'état avorté&#160;: fige les inputs du joueur local et
	/// remplace le HUD par l'overlay <c>wasted.tscn</c> avec le texte «&#160;PARTIE INTERROMPUE&#160;».
    /// Sans effet visuel sur un serveur dédié headless.
    /// </summary>
    private void ApplyAbortedLocal()
    {
        if (_localPlayer is not null) _localPlayer.InputFrozen = true;
        else
        {
            // Lien joueur local pas encore fait&#160;: tenter une résolution opportuniste
            // (UpdateLocalPlayerLink est appelé à chaque Tick, donc même un retard
			// de quelques frames est tolérable&#160;; on attrape ce qu'on peut maintenant).
			var found = GetTree().GetFirstNodeInGroup("local_player") as Player;
			if (found is not null) found.InputFrozen = true;
		}
		ShowWastedOverlay("PARTIE INTERROMPUE");
	}

	/// <summary>
	/// RPC serveur → clients déclenchant la bascule en mode avorté côté UI.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NetEnterAborted()
	{
		if (Multiplayer.GetRemoteSenderId() != 1) return;
		if (_aborted) return;
		_aborted = true;
		// Faire avancer la FSM locale en Aborted pour cohérence (sinon Tick continue
		// d'évaluer Playing/Resolving alors qu'on est figé). TransitionTo no-op si déjà là.
		if (_fsm is not null) _fsm.TransitionTo(State.Aborted);
		else ApplyAbortedLocal();
	}

	// ── Stats: submit (client → serveur), résolution (serveur) ───────────────

	/// <summary>
	/// Entrée en état Resolving. Soumet les stats du joueur local au serveur (ou
	/// les écrit directement dans <see cref="_statsByPeer"/> en offline / côté serveur
	/// pour le compte des joueurs hébergés localement).
	/// </summary>
	private void OnEnterResolving()
	{
		var net = NetworkManager.Instance;
		// Toutes les répliques locales authority soumettent leurs stats.
		var localPlayer = GetTree().GetFirstNodeInGroup("local_player") as Player;
		if (localPlayer is null) return;
		var s = localPlayer.Stats;
		s.PeerId = localPlayer.PeerId;

		if (net is null || !net.IsRunning)
		{
			// Offline&#160;: pas de réseau, on agrège directement.
			_statsByPeer[s.PeerId] = CloneStats(s);
			return;
		}
		if (net.IsServer)
		{
			// Serveur non-dédié (rare ici, mais possible)&#160;: idem.
			_statsByPeer[s.PeerId] = CloneStats(s);
			return;
		}
		// Client: envoyer au serveur.
		RpcId(1, MethodName.SubmitStats, s.PeerId, s.RagdollCount,
			s.TotalRagdollSeconds, s.JumpCount, s.TimeOfDeathSeconds, (byte)s.DeathReason);
	}

	/// <summary>
	/// RPC client → serveur&#160;: dépôt des stats authoritatives du peer. Le serveur
	/// valide que l'émetteur correspond bien au peer revendiqué pour éviter qu'un
	/// peer ne soumette des stats au nom d'un autre.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitStats(int InPeerId, int InRagdollCount, float InTotalRagdollSeconds,
        int InJumpCount, float InTimeOfDeathSeconds, byte InDeathReason)
    {
        if (!Multiplayer.IsServer()) return;
        int senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != InPeerId)
        {
            GD.PrintErr($"[GameController] SubmitStats refused: sender={senderId} claims peerId={InPeerId}");
            return;
        }
        _statsByPeer[InPeerId] = new PlayerGameStats
        {
            PeerId = InPeerId,
            RagdollCount = InRagdollCount,
            TotalRagdollSeconds = InTotalRagdollSeconds,
            JumpCount = InJumpCount,
            TimeOfDeathSeconds = InTimeOfDeathSeconds,
            DeathReason = (DeathReason)InDeathReason,
        };
    }

    /// <summary>
	/// Entrée en état Finalize côté serveur&#160;: exécute la résolution si elle n'a pas
	/// déjà eu lieu, puis broadcaste les gagnants. Côté client, l'écriture en Firestore
    /// est déclenchée à la réception du broadcast (<see cref="NetApplyWinnerData"/>).
    /// </summary>
    private void OnEnterFinalize()
    {
        var net = NetworkManager.Instance;
        if (net is null || !net.IsRunning)
        {
            // Offline&#160;: résoudre et écrire directement dans LobbyState.
            ResolveAndApplyLocal();
            return;
        }
        if (!net.IsServer) return;
        if (_winnerBroadcasted) return;
        _winnerBroadcasted = true;

        var modeConditions = (_mode as IGameMode)?.SubwinningConditions;
        if (modeConditions is null || modeConditions.Count == 0)
        {
            //GD.Print("[GameController] No subwinning conditions declared by mode — skipping resolve.");
            return;
        }
        var result = _resolver.Resolve(_statsByPeer, modeConditions);

        // Sérialiser les sous-gagnants en tableaux parallèles pour le RPC.
        int subN = result.SubWinners.Count;
        int[] subPeers = new int[subN];
        string[] subCondIds = new string[subN];
        string[] subLabels = new string[subN];
        for (int i = 0; i < subN; i++)
        {
            subPeers[i] = result.SubWinners[i].PeerId;
            subCondIds[i] = result.SubWinners[i].ConditionId;
            subLabels[i] = result.SubWinners[i].DisplayName;
        }
        Rpc(MethodName.NetApplyWinnerData, _gameId, LobbyState.SelectedMapId,
            result.MainWinnerPeerId, result.MainConditionId, result.MainConditionDisplayName,
            subPeers, subCondIds, subLabels);
    }

    /// <summary>
    /// Pipeline offline équivalent au broadcast réseau&#160;: résoudre, mettre à jour
	/// <see cref="LobbyState"/>, déclencher l'écriture Firestore. Toujours appelé sur
	/// le seul peer existant en mode offline.
	/// </summary>
	private void ResolveAndApplyLocal()
	{
		var modeConditions = (_mode as IGameMode)?.SubwinningConditions;
		if (modeConditions is null || modeConditions.Count == 0) return;
		var result = _resolver.Resolve(_statsByPeer, modeConditions);

		var subWinners = new List<(int, string, string)>(result.SubWinners.Count);
		for (int i = 0; i < result.SubWinners.Count; i++)
		{
			var e = result.SubWinners[i];
			subWinners.Add((e.PeerId, e.ConditionId, e.DisplayName));
		}
		LobbyState.SetWinnerData(result.MainWinnerPeerId, result.MainConditionId,
			result.MainConditionDisplayName, subWinners);

		// L'écriture Firestore en offline est optionnelle (pas de partie en réseau à
        // pérenniser). Si le joueur est authentifié, on persiste quand même.
        TryWriteStatsToFirestore(_gameId, LobbyState.SelectedMapId, result.MainWinnerPeerId,
            result.MainConditionId, subWinners);
    }

    /// <summary>
    /// Reçu par tous les clients (et le serveur via CallLocal=true)&#160;: applique le
    /// résultat de fin de partie dans <see cref="LobbyState"/> pour que la phase Winning
	/// puisse l'afficher. Côté joueur authentifié, déclenche aussi l'écriture Firestore
	/// du doc joueur (et du doc racine si on est l'hôte du lobby).
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NetApplyWinnerData(string InGameId, string InMapId,
		int InMainPeerId, string InMainConditionId, string InMainConditionLabel,
		int[] InSubPeerIds, string[] InSubConditionIds, string[] InSubLabels)
	{
		_gameId = InGameId ?? "";
		int n = Mathf.Min(Mathf.Min(InSubPeerIds?.Length ?? 0, InSubConditionIds?.Length ?? 0), InSubLabels?.Length ?? 0);
		var subs = new List<(int, string, string)>(n);
		for (int i = 0; i < n; i++) subs.Add((InSubPeerIds[i], InSubConditionIds[i], InSubLabels[i]));
		LobbyState.SetWinnerData(InMainPeerId, InMainConditionId ?? "", InMainConditionLabel ?? "", subs);
		TryWriteStatsToFirestore(_gameId, InMapId ?? "", InMainPeerId, InMainConditionId ?? "", subs);
	}

	/// <summary>
	/// Écrit les stats du joueur local dans Firestore (doc joueur), et le doc racine
	/// de la partie si on est l'hôte du lobby. Silencieux si&#160;: pas d'identifiant de
	/// partie, pas de joueur local authority, ou pas de token Firebase disponible.
	/// </summary>
	private void TryWriteStatsToFirestore(string InGameId, string InMapId, int InMainPeerId,
		string InMainConditionId, IReadOnlyList<(int PeerId, string ConditionId, string Label)> InSubWinners)
	{
		if (string.IsNullOrEmpty(InGameId)) return;

		// Pas d'écriture si le serveur dédié exécute ce chemin&#160;: aucun token utilisateur.
        var net = NetworkManager.Instance;
        if (net is not null && net.IsRunning && net.IsServer && IsDedicatedServer()) return;

        var localPlayer = GetTree().GetFirstNodeInGroup("local_player") as Player;
        if (localPlayer is null) return;

        // Conditions gagnées par le joueur local depuis la vue broadcastée.
        string mainWon = (localPlayer.PeerId == InMainPeerId) ? InMainConditionId : "";
        var subsWon = new List<string>();
        for (int i = 0; i < InSubWinners.Count; i++)
            if (InSubWinners[i].PeerId == localPlayer.PeerId)
                subsWon.Add(InSubWinners[i].ConditionId);

        _statsRepo ??= new GameStatsRepository(
            new FirestoreClient(FirebaseConfig.ProjectId),
            AuthServiceProvider.GetCurrentToken);

		// Fire-and-forget&#160;: l'écriture ne doit pas bloquer la transition de phase.
		_ = WritePlayerDocSafelyAsync(InGameId, localPlayer.Stats, mainWon, subsWon);

		if (LobbyState.IsHost)
			_ = WriteGameDocSafelyAsync(InGameId, InMapId, InMainPeerId, InMainConditionId);
	}

	private async System.Threading.Tasks.Task WritePlayerDocSafelyAsync(string InGameId,
		PlayerGameStats InStats, string InMainConditionWonId, IReadOnlyList<string> InSubConditionsWonIds)
	{
		try { await _statsRepo.SavePlayerStatsAsync(InGameId, InStats, InMainConditionWonId, InSubConditionsWonIds); }
		catch (Exception ex) { GD.PrintErr($"[GameController] Firestore player stats write failed: {ex.Message}"); }
	}

	private async System.Threading.Tasks.Task WriteGameDocSafelyAsync(string InGameId,
		string InMapId, int InWinnerPeerId, string InWinnerConditionId)
	{
		try { await _statsRepo.SaveGameAsync(InGameId, InMapId, InWinnerPeerId, InWinnerConditionId); }
		catch (Exception ex) { GD.PrintErr($"[GameController] Firestore game doc write failed: {ex.Message}"); }
	}

	/// <summary>Copie défensive des stats du joueur local pour les agréger côté serveur sans alias.</summary>
	private static PlayerGameStats CloneStats(PlayerGameStats InStats) => new()
	{
		PeerId = InStats.PeerId,
		RagdollCount = InStats.RagdollCount,
		TotalRagdollSeconds = InStats.TotalRagdollSeconds,
		JumpCount = InStats.JumpCount,
		TimeOfDeathSeconds = InStats.TimeOfDeathSeconds,
		DeathReason = InStats.DeathReason,
	};

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
		GD.Print($"[GameController] OnLocalPlayerDied peer={InPeerId} reason={InReason} fsmState={_fsm?.Current} modeIsDone={_mode?.IsDone}");
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
