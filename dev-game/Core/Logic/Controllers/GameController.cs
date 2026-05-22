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

	/// <summary>
	/// Durée du palier «&#160;PARTIE INTERROMPUE&#160;» en phase <c>Aborted</c>&#160;:
	/// laisse aux joueurs restants le temps de lire l'overlay avant que la FSM
	/// principale n'enchaîne vers Winning (qui rebouclera vers Lobby). Secondes.
	/// </summary>
	[Export] private float _abortedDisplayDuration = 3f;

	/// <summary>
	/// Délai max accordé au chargement du niveau + spawn du joueur local après
	/// l'entrée en phase Game. Si le joueur local n'est pas résolu dans ce délai,
	/// on force la sortie de la phase via Aborted plutôt que de laisser l'overlay
	/// de chargement coller à l'écran indéfiniment.
	/// </summary>
	[Export] private float _loadingWatchdogDuration = 15f;

	public enum State { Init, Failure, Waiting, Playing, Resolving, Finalize, Aborted }
	private StateMachine<State> _fsm = null;

	private IPhase _mode = null;
	private Node _mapContainerInstance = null;
	private MultiplayerSpawner _spawner = null;
	private PlayerSpawner _playerSpawner = null;
	private MultiplayerApi.PeerDisconnectedEventHandler _onPeerDisconnectedHandler = null;
	private bool _done;
	public bool IsDone => _done;

	// ── Spawn coordination (corrige race "ClientReady arrivé dans la fenêtre
	//    morte entre rounds"). Server-side : on ne refuse plus une demande de
	//    spawn quand le map_container/spawner n'est pas encore prêt — on la met
    //    en attente et on draine quand LoadLevel finit. Client-side : on renvoie
	//    HostMapPick+ClientReady jusqu'à recevoir un ack du serveur (ou jusqu'à
	//    l'expiration du watchdog).
	private readonly Dictionary<int, string> _pendingSpawns = new();
	private readonly HashSet<int> _spawnedPeers = new();
	private string _pendingHostMapPick;
	private bool _clientSpawnAcked;
	private float _clientReadyAccum;
	private int _clientReadyRetries;
	private const float ClientReadyRetryInterval = 1.0f;
	private const int ClientReadyMaxRetries = 10;

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

	/// <summary>Durée d'affichage du plein-écran «&#160;WASTED&#160;» avant la bascule en
	/// mode spectateur. Le fade-out du label se joue ensuite (cf. <see cref="_wastedFadeDuration"/>).</summary>
	[Export] private float _wastedHoldDuration = 1.2f;

	/// <summary>Durée du fondu de sortie du label «&#160;WASTED&#160;» quand on enchaîne
	/// sur le spectateur. Le label reste dans la scène (utilisé ensuite pour
	/// «&#160;GAME OVER&#160;» / «&#160;PARTIE INTERROMPUE&#160;»).</summary>
	[Export] private float _wastedFadeDuration = 0.3f;

	/// <summary>
	/// Tween orchestrant la transition «&#160;wasted plein écran → spectateur&#160;».
	/// Conservé pour pouvoir l'annuler proprement (Kill) si la phase se termine
    /// avant son expiration (Resolving / Aborted / Exit) — sans annulation, son
    /// callback pourrait faire entrer en Spectating après le GAME OVER.
    /// </summary>
    private Tween _spectatorEntryTween;

    public override void _Ready()
    {
		//Fallback d'export scene
		_mapContainerSceneAsset ??= ResourceLoader.Load<PackedScene>("res://Core/World/Maps/map_container.tscn");
		_playerScene ??= ResourceLoader.Load<PackedScene>("res://Core/World/CharacterModel/Player/Player.tscn");
		_uiIngameScene ??= ResourceLoader.Load<PackedScene>("res://Core/UI/InGame/ui_ingame.tscn");
		_wastedScene ??= ResourceLoader.Load<PackedScene>("res://Core/UI/InGame/wasted.tscn");
	}

	private bool _levelLoadFailed = false;

	public void Enter()
	{
		_done = false;
		_levelLoadFailed = false;
		_localPlayerDead = false;
		_aborted = false;
		_hostPeerId = 0;
		_winnerBroadcasted = false;
		_loadingWatchdogElapsed = 0f;
		_loadingWatchdogFired = false;
		GameElapsedSeconds = 0f;
		_statsByPeer.Clear();
		_pendingSpawns.Clear();
		_spawnedPeers.Clear();
		_clientSpawnAcked = false;
		_clientReadyAccum = 0f;
		_clientReadyRetries = 0;
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
			ShowErrorAndExit("La scène map_container n'est pas assignée.");
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
			ShowErrorAndExit("MultiplayerSpawner introuvable dans map_container.");
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
			// Le niveau est loadé a la réception du HostMapPick — sauf si on
			// avait reçu un HostMapPick avant que map_container soit prêt
			// (race de la fenêtre morte Lobby/Game côté serveur). Auquel cas
			// on consomme la valeur mise en attente ici. Les ClientReady
			// queue-és seront drainés par LoadLevel à la fin.
			if (_pendingHostMapPick is not null)
			{
				var mapId = _pendingHostMapPick;
				_pendingHostMapPick = null;
				LoadLevel(mapId);
			}
		}
		else if (net is not null && net.IsClient)
		{
			if (!LoadLevel(LobbyState.SelectedMapId)) return;
			//GD.Print($"[GameController] Online client — sending HostMapPick('{LobbyState.SelectedMapId}') + ClientReady(isHost={LobbyState.IsHost}).");
			RpcId(1, MethodName.HostMapPick, LobbyState.SelectedMapId);
			RpcId(1, MethodName.ClientReady, LobbyState.IsHost, LobbyState.SelectedHatId);
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

		// FSM interne (Init → Waiting → Playing → Resolving). Démarrée ici parce
		// que le prédicat Playing→Resolving dépend de _mode.IsDone.
		// Le gate Init → Waiting attend que le niveau ET le joueur local soient
		// prêts (côté client) : sans ça, la FSM avance sur la base d'un timer
        // pur et un peer dont le spawn a été silencieusement perdu passe en
		// Playing à 10s tout en étant collé sur l'overlay loading. C'est aussi
        // ce qui désarme par accident le watchdog (cf. TickLoadingWatchdog).
        _waitingCondition = new TimeElapsedCondition<State>(_waitingDuration);
        _fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => _mode is not null && (IsDedicatedServer() || _localPlayer is not null)),
            State.Waiting
        );
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => _levelLoadFailed),
            State.Failure
        );
        _fsm.When(State.Waiting,
            _waitingCondition,
            State.Playing
        );
        _fsm.When(State.Playing,
            new PredicateCondition<State>(() => _mode?.IsDone ?? false),
            State.Resolving
        );
        _fsm.When(State.Resolving,
            new TimeElapsedCondition<State>(_gameOverDuration),
            State.Finalize
        );
		// Aborted -> Finalize : après le palier d'affichage de l'overlay
        // « PARTIE INTERROMPUE », on enchaîne sur Finalize qui pose
        // _done=true et laisse la FSM principale rebascule vers Winning. Sans
		// cette règle, l'état Aborted serait terminal et la phase Game
		// soft-lockerait (cf. BUG_REVIEW post-2026-05-15).
		_fsm.When(State.Aborted,
			new TimeElapsedCondition<State>(_abortedDisplayDuration),
			State.Finalize
		);
		OnSubEnter(State.Init);

		var mapDef = MapRegistry.Get(InMapId) ?? MapRegistry.All[0];
		LoadingScreen.SetStatus($"Chargement de la carte : {mapDef.DisplayName}", 0.65f);

		if (string.IsNullOrEmpty(mapDef.ScenePath) || !ResourceLoader.Exists(mapDef.ScenePath))
		{
			GD.PrintErr($"[GameController] Failed to find map resource: {mapDef.ScenePath}");
			_levelLoadFailed = true;
			return false; // Interrupt loading
		}

		var levelScene = ResourceLoader.Load<PackedScene>(mapDef.ScenePath);
		if (levelScene is null)
		{
			GD.PrintErr($"[GameController] Niveau introuvable : {mapDef.ScenePath}");
			_levelLoadFailed = true;
			return false;
		}
		var levelInstance = levelScene.Instantiate();
		var mapSlot = _mapContainerInstance.GetNodeOrNull("Map");
		if (mapSlot is null)
		{
			GD.PrintErr("[GameController] Slot 'Map' introuvable dans map_container.tscn.");
			_levelLoadFailed = true;
			return false;
		}
		mapSlot.AddChild(levelInstance);
		LoadingScreen.SetStatus("En attente du joueur", 0.85f);

		if (levelInstance is not IPhase mode)
		{
			GD.PrintErr($"[GameController] Le root du niveau '{mapDef.ScenePath}' n'implémente pas IPhase.");
			_levelLoadFailed = true;
			return false;
		}
		_mode = mode;

		_playerSpawner = levelInstance.GetNodeOrNull<PlayerSpawner>("PlayerSpawner");
		if (_playerSpawner is null)
			GD.PrintErr($"[GameController] PlayerSpawner introuvable dans le niveau '{mapDef.ScenePath}'.");

		// À ce point, _spawner et _playerSpawner sont assignés : on peut
		// honorer les ClientReady qui sont arrivés pendant la fenêtre morte.
		DrainPendingSpawns();
		return true;
	}

	/// <summary>
	/// Côté serveur uniquement&#160;: spawn tous les peers qui avaient envoyé
	/// <see cref="ClientReady"/> avant que <see cref="LoadLevel"/> ait fini de
	/// configurer le <c>MultiplayerSpawner</c> et le <see cref="PlayerSpawner"/>.
	/// Idempotent — un peer déjà spawné (cf. <see cref="_spawnedPeers"/>) est
	/// simplement ré-acquitté, au cas où l'ack précédent ait été perdu.
    /// </summary>
    private void DrainPendingSpawns()
    {
        if (_spawner is null || _playerSpawner is null) return;
        if (_pendingSpawns.Count == 0) return;
        var pending = new List<KeyValuePair<int, string>>(_pendingSpawns);
        _pendingSpawns.Clear();
        foreach (var kv in pending)
        {
            if (_spawnedPeers.Contains(kv.Key))
            {
                RpcId(kv.Key, MethodName.ServerSpawnAck);
                continue;
            }
            ServerSpawnPeer(kv.Key, kv.Value);
            _spawnedPeers.Add(kv.Key);
            RpcId(kv.Key, MethodName.ServerSpawnAck);
        }
    }

    public void Tick(float InDelta)
    {
		// Le link au joueur local et la mise à jour de l'overlay loading doivent
		// tourner même si la FSM n'est pas encore créée (LoadLevel pas terminé).
        UpdateLocalPlayerLink();
        UpdateLoadingOverlay();
        TickClientReadyRetry(InDelta);
		// Watchdog du chargement : si le FSM n'est jamais créé OU que le joueur
		// local n'apparaît jamais, on force la sortie après _loadingWatchdogDuration
		// secondes plutôt que de laisser l'overlay coller. La règle ne s'applique
		// qu'avant l'entrée en Playing — une fois la partie en cours, on n'a plus
		// besoin du watchdog.
		TickLoadingWatchdog(InDelta);
		// Diagnostic : dump périodique tant que le loading reste visible. À
		// retirer une fois la cause du blocage RotatingBarrel comprise.
		TickLoadingDiagnostic(InDelta);
		if (_fsm is null) return;
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
			// Vide les caches per-peer (positions, spawns, cooldowns) sans toucher
			// au transport. Évite qu'un _lastKnownState hérité de cette manche
            // déclenche une correction anti-téléport au premier snapshot de la
            // manche suivante quand la connexion ENet est conservée.
            net.ResetSessionState();
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
        _spectatorEntryTween?.Kill();
        _countdownTween = null;
        _timerTween = null;
        _spectatorEntryTween = null;
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
        _pendingSpawns.Clear();
        _spawnedPeers.Clear();
        _pendingHostMapPick = null;
        _clientSpawnAcked = false;
        _clientReadyAccum = 0f;
        _clientReadyRetries = 0;
        _gameId = "";
        GameElapsedSeconds = 0f;
    }

    // ── Spawn flow (porté de World.cs) ────────────────────────────────────────

    private GodotObject SpawnPlayerNode(Variant data)
    {
        var dict = data.As<Godot.Collections.Dictionary>();
        int peerId = dict["id"].As<int>();
        Vector3 pos = dict["pos"].As<Vector3>();
        string hatId = dict.ContainsKey("hat") ? dict["hat"].As<string>() : HatRegistry.DefaultHatId;

        var player = _playerScene.Instantiate<Player>();
        player.Name = peerId.ToString();
        player.PeerId = peerId;
        player.SetMultiplayerAuthority(peerId);
        player.SpawnPosition = pos;
        player.HatId = hatId;

        GD.Print($"[LoadDiag] SpawnPlayerNode: peerId={peerId}, pos={pos}, hat={hatId}, authorityAtSpawn={player.IsMultiplayerAuthority()}, localPeerId={NetworkManager.Instance?.LocalPeerId ?? -1}");
        return player;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReady(bool InIsHost, string InHatId)
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
		GD.Print($"[LoadDiag] ClientReady RPC received from peer {peerId} (isHost={InIsHost}, hat={InHatId})");

		// Idempotence : le client renvoie ClientReady en boucle jusqu'à recevoir
        // ServerSpawnAck. Si on a déjà spawn ce peer, on ré-acquitte simplement
		// (l'ack précédent a probablement été perdu) sans dupliquer le Player.
		if (_spawnedPeers.Contains(peerId))
		{
			RpcId(peerId, MethodName.ServerSpawnAck);
			return;
		}

		// Fenêtre morte : le RPC est arrivé avant que LoadLevel ait fini de
		// mettre en place _spawner / _playerSpawner (transition Winning →
		// Lobby → Game côté serveur, qui prend plusieurs ticks). On met la
		// demande en attente — DrainPendingSpawns la consommera dès que le
		// niveau sera prêt, plutôt que de la laisser tomber silencieusement.
		if (_spawner is null || _playerSpawner is null)
		{
			_pendingSpawns[peerId] = InHatId ?? HatRegistry.DefaultHatId;
			return;
		}

		ServerSpawnPeer(peerId, InHatId);
		_spawnedPeers.Add(peerId);
		RpcId(peerId, MethodName.ServerSpawnAck);
	}

	/// <summary>
	/// Confirmation serveur → client&#160;: le peer demandeur a bien été spawné
	/// (ou la demande a été enregistrée et le sera dès que le niveau sera
	/// prêt). Coupe la boucle de retransmission de <see cref="ClientReady"/>
	/// côté client. Idempotent — un ack reçu après spawn local n'a pas d'effet.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ServerSpawnAck()
	{
		_clientSpawnAcked = true;
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
        // Fenêtre morte : map_container peut ne pas encore être instancié si la
        // FSM principale est encore en Lobby (1 tick) ou en transition. Garder
        // la valeur — Enter() la consomme aussitôt le container prêt. Sans ça,
        // le premier HostMapPick était perdu et le serveur restait sans niveau.
        if (_mapContainerInstance is null)
        {
            _pendingHostMapPick = InMapId;
            return;
        }
        LoadLevel(InMapId);
    }

    private void ServerSpawnPeer(int peerId, string hatId = null)
    {
        if (_spawner is null || _playerSpawner is null)
        {
            GD.PrintErr($"[LoadDiag] ServerSpawnPeer({peerId}) ABORT: spawner={(_spawner is null ? "null" : "ok")} playerSpawner={(_playerSpawner is null ? "null" : "ok")}");
            return;
        }
        Vector3 spawnPos = _playerSpawner.GetNextSpawnPoint();
        var data = new Godot.Collections.Dictionary
        {
            ["id"] = peerId,
            ["pos"] = spawnPos,
            ["hat"] = hatId ?? HatRegistry.DefaultHatId,
        };
        _spawner.Spawn(data);
        // Donne au NetworkManager le spawn par peer pour son respawn autoritaire
        // (chute sous FallThreshold dans OnPacketReceived).
        NetworkManager.Instance?.RegisterPeerSpawn(peerId, spawnPos);
        GD.Print($"[LoadDiag] Server spawned peer {peerId} at {spawnPos} hat={hatId}");
    }

    private void OnPeerDisconnected(long peerId)
    {
		// Ne PAS retirer le peer de _pendingSpawns ou _spawnedPeers : si c'est
		// le seul joueur et qu'il quitte, on veut pouvoir nettoyer. Mais non,
		// Wait, si on l'enlève, Multiplayer.GetPeers().Length reflète déjà le départ.
		_pendingSpawns.Remove((int)peerId);
		_spawnedPeers.Remove((int)peerId);
		var players = _mapContainerInstance?.GetNodeOrNull("Players");
		var player = players?.GetNodeOrNull(((int)peerId).ToString());
		if (player is not null)
		{
			((Node)player).QueueFree();
			//GD.Print($"[GameController] Server despawned peer {(int)peerId}");
		}

		// Si l'hôte du lobby quitte mid-game, la partie est avortée : on bascule
        // immédiatement en état Aborted (terminal), on fige le monde et on
		// broadcaste l'overlay aux clients restants. Cette logique passe AVANT
		// le check « dernier peer parti » pour bien marquer l'aborted comme cause.
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
            // Raccourci : si tout le monde est parti, on sort de GameController.
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
        player.HatId = LobbyState.SelectedHatId;
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
            case State.Failure:
                LoadingScreen.Hide();
                if (!IsDedicatedServer())
                {
                    ErrorDialog.Show(GetTree(), "Map could not be loaded", InOkText: "OK");
                }
                _done = true;
                break;
            case State.Playing: _mode?.Enter(); break;
            case State.Resolving:
                TeardownSpectator();
                ShowGameOverOverlay();
                OnEnterResolving();
                break;
            case State.Finalize:
                OnEnterFinalize();
                _done = true;
                break;
            case State.Aborted:
                TeardownSpectator();
                OnEnterAborted();
                break;
        }
    }

    /// <summary>
    /// Sortie propre du mode spectateur quand la phase se termine (Resolving,
	/// Aborted, ou Exit). Annule le tween d'entrée en cours (sinon son callback
	/// pourrait faire entrer en Spectating après le GAME OVER), repasse le
	/// joueur local en Dead (terminal, sans input), et libère la souris pour
	/// que les overlays de fin soient visibles correctement.
	/// </summary>
	private void TeardownSpectator()
	{
		_spectatorEntryTween?.Kill();
		_spectatorEntryTween = null;
		if (_localPlayer is not null && _localPlayer.GetCurrentState() == CharacterState.Spectating)
			_localPlayer.TransitionTo(CharacterState.Dead);
		if (!IsDedicatedServer()) Input.MouseMode = Input.MouseModeEnum.Visible;
	}
	private void OnSubExit(State _) { }

	// ── Aborted (host parti) ──────────────────────────────────────────────────

	/// <summary>
	/// Entrée en état Aborted (host parti). Le serveur broadcaste
	/// l'overlay aux clients&#160;; tous les peers (serveur via <c>CallLocal</c>) figent
	/// l'input du joueur local et affichent <c>wasted.tscn</c> avec un libellé dédié.
	/// Après <see cref="_abortedDisplayDuration"/> secondes la règle FSM
	/// <c>Aborted → Finalize</c> pose <see cref="_done"/>=true et la FSM
	/// principale rebascule vers Winning (qui inspectera <see cref="WasAborted"/>).
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

		// Fallback pour le cas où la FSM interne n'a jamais été créée (host parti
		// avant LoadLevel). Sans ce timer, _done resterait à false et la phase
		// Game soft-lockerait. Si la FSM tourne, la règle Aborted → Finalize
		// enregistrée dans LoadLevel gère la sortie — ce timer est alors inerte.
		if (_fsm is null)
		{
			var fallback = new Timer
			{
				WaitTime = _abortedDisplayDuration,
				OneShot = true,
				Autostart = true,
			};
			fallback.Timeout += () => _done = true;
			AddChild(fallback);
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
		// Partie avortée (host parti) ou watchdog de chargement déclenché&#160;: il n'y
        // a pas de stats utiles à résoudre. On laisse simplement OnSubEnter poser
        // _done=true pour relâcher la FSM principale vers Winning, qui détectera
        // <see cref="WasAborted"/> et affichera le bon flux. Évite aussi un Resolve
        // sur un dictionnaire de stats potentiellement vide.
        if (_aborted) return;

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

	/// <summary>Temps écoulé depuis Enter, utilisé par le watchdog de chargement.</summary>
	private float _loadingWatchdogElapsed;
	private bool _loadingWatchdogFired;

	/// <summary>
	/// Accumulateur pour <see cref="TickLoadingDiagnostic"/> : on dump l'état
	/// complet (mode, players container, authority de chaque enfant) toutes
	/// les <see cref="LoadingDiagInterval"/> secondes tant que le loading
	/// reste visible. Diagnostic temporaire — à retirer une fois la cause
	/// du blocage RotatingBarrel identifiée.
	/// </summary>
	private float _loadingDiagAccumulator;
    private const float LoadingDiagInterval = 2f;

    /// <summary>
	/// Watchdog du chargement&#160;: si le joueur local n'apparaît pas dans
	/// <see cref="_loadingWatchdogDuration"/> secondes (et qu'on n'est pas serveur
	/// dédié), on cache l'overlay et on déclenche la sortie de phase via l'état
	/// Aborted. Évite la classe de bug «&#160;loading screen collé&#160;» où un échec
	/// silencieux du <c>MultiplayerSpawner</c> ou de l'authority assignement
    /// laissait <c>_localPlayer</c> à null indéfiniment.
    /// </summary>
    private void TickLoadingWatchdog(float InDelta)
    {
        if (_loadingWatchdogFired) return;
        if (IsDedicatedServer()) return;
        if (_localPlayer is not null) return; // joueur local prêt — watchdog inutile
											  // Note : on n'arme/désarme PAS selon l'état FSM. Le gate Init → Waiting
                                              // dépend désormais de _localPlayer (cf. LoadLevel), donc atteindre
                                              // Playing implique nécessairement _localPlayer != null. Brancher le
                                              // watchdog uniquement sur _localPlayer évite le bug «&#160;watchdog
                                              // désarmé prématurément par le timer Waiting → Playing&#160;» qui laissait
											  // le joueur collé sur l'écran de loading indéfiniment.

		_loadingWatchdogElapsed += InDelta;
		if (_loadingWatchdogElapsed < _loadingWatchdogDuration) return;

		_loadingWatchdogFired = true;
		GD.PrintErr($"[GameController] Loading watchdog: {_loadingWatchdogDuration:F1}s sans joueur local — abandon de la phase.");
		LoadingScreen.Hide();
		// Route via Aborted plutôt que de poser _done=true sec&#160;: l'overlay
		// «&#160;PARTIE INTERROMPUE&#160;» informe l'utilisateur que quelque chose a
		// foiré, puis la règle Aborted → Finalize sortira proprement.
		if (_fsm is not null) _fsm.TransitionTo(State.Aborted);
		else OnSubEnter(State.Aborted);
	}

	/// <summary>
	/// Côté client uniquement&#160;: tant que le serveur n'a pas acquitté le
	/// <see cref="ClientReady"/> via <see cref="ServerSpawnAck"/>, on retransmet
	/// périodiquement <see cref="HostMapPick"/> + <see cref="ClientReady"/>.
	/// Couvre les pertes silencieuses dans la fenêtre morte côté serveur
	/// (transition Lobby/Game, <c>_spawner</c>/<c>_playerSpawner</c> non encore
	/// assignés). Le compteur de retries borne la boucle&#160;: passé
	/// <see cref="ClientReadyMaxRetries"/>, on laisse le watchdog router vers
	/// Aborted plutôt que de faire planer une boucle infinie.
	/// </summary>
	private void TickClientReadyRetry(float InDelta)
    {
        if (_clientSpawnAcked) return;
        if (_localPlayer is not null) return; // spawn replicated localement, OK
        var net = NetworkManager.Instance;
        if (net is null || !net.IsClient) return;
        if (_clientReadyRetries >= ClientReadyMaxRetries) return;

        _clientReadyAccum += InDelta;
        if (_clientReadyAccum < ClientReadyRetryInterval) return;
        _clientReadyAccum = 0f;
        _clientReadyRetries++;
        // Réémet les deux RPCs ensemble&#160;: HostMapPick est idempotent côté
        // serveur (early-return si _mode déjà chargé) et ClientReady est
        // idempotent via _spawnedPeers.
        RpcId(1, MethodName.HostMapPick, LobbyState.SelectedMapId);
        RpcId(1, MethodName.ClientReady, LobbyState.IsHost, LobbyState.SelectedHatId);
    }

    /// <summary>
    /// Diagnostic temporaire : tant que <see cref="LoadingScreen"/> est visible
	/// (i.e. on n'a pas encore trouvé _mode + _localPlayer), dump périodiquement
	/// l'état du container Players, les authorities, l'état FSM. Permet
	/// d'identifier en lisant les logs serveur+client lequel des maillons casse
    /// (spawn pas appelé, replication pas reçue, authority mal assignée, group
    /// non-rejoint…). À retirer une fois la cause comprise.
    /// </summary>
    private void TickLoadingDiagnostic(float InDelta)
    {
        if (!LoadingScreen.IsVisible) return;
        bool levelReady = _mode is not null;
        bool playerReady = IsDedicatedServer() || _localPlayer is not null;
        if (levelReady && playerReady) return;

        _loadingDiagAccumulator += InDelta;
        if (_loadingDiagAccumulator < LoadingDiagInterval) return;
        _loadingDiagAccumulator = 0f;

        var players = _mapContainerInstance?.GetNodeOrNull("Players");
        int playerCount = players?.GetChildCount() ?? 0;
        var localFromGroup = GetTree().GetFirstNodeInGroup("local_player");
        var net = NetworkManager.Instance;
        GD.Print($"[LoadDiag] mode={(_mode is null ? "null" : "ok")} " +
            $"localPlayer={(_localPlayer is null ? "null" : "ok")} " +
            $"playersChildCount={playerCount} " +
            $"groupFound={(localFromGroup is null ? "null" : localFromGroup.Name)} " +
            $"fsmState={_fsm?.Current.ToString() ?? "(null)"} " +
            $"dedicatedServer={IsDedicatedServer()} " +
            $"netRole={(net is null ? "null" : (net.IsServer ? "server" : (net.IsClient ? "client" : "none")))} " +
            $"localPeerId={net?.LocalPeerId ?? -1}");
        if (players is not null)
        {
            foreach (var child in players.GetChildren())
            {
                if (child is Player p)
                    GD.Print($"[LoadDiag]   Player name={p.Name} peerId={p.PeerId} " +
                        $"authority={p.IsMultiplayerAuthority()} " +
                        $"inGroup={p.IsInGroup("local_player")} " +
                        $"inTree={p.IsInsideTree()} " +
                        $"pos={p.GlobalPosition}");
                else
                    GD.Print($"[LoadDiag]   non-Player child name={child.Name} type={child.GetType().Name}");
            }
        }
    }

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
        GD.Print($"player ready? {playerReady}");
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

	// ── Échec de setup : dialog + retour menu ─────────────────────────────────

	/// <summary>
	/// Sortie d'erreur unifiée pour les chemins de setup de la phase Game
	/// (asset manquant, scène de niveau introuvable, slot 'Map' absent,
	/// root du niveau qui n'implémente pas <see cref="IPhase"/>, …).
	/// <list type="bullet">
	/// <item>Serveur dédié&#160;: pas d'UI&#160;; on logge et on pose
	/// <see cref="_done"/>=true pour cycler silencieusement vers Winning
	/// (comportement historique).</item>
	/// <item>Client (ou serveur non-dédié)&#160;: affiche <see cref="ErrorDialog"/>
	/// modal qui propose un retour propre au menu principal. Le watchdog de
	/// chargement est désarmé pour ne pas réveiller Aborted par-dessus le
	/// dialog. <see cref="_done"/> n'est PAS levé — la phase reste en place,
	/// idle, jusqu'à ce que l'utilisateur clique et que le callback fasse le
	/// <c>ChangeSceneToFile</c>. Évite que la FSM principale enchaîne sur
	/// Winning avec des données vides pendant que le dialog est ouvert.</item>
	/// </list>
	/// </summary>
	private void ShowErrorAndExit(string InMessage)
	{
		GD.PrintErr($"[GameController] {InMessage}");

		if (IsDedicatedServer())
		{
			_done = true;
			return;
		}

		LoadingScreen.Hide();
		_loadingWatchdogFired = true;
		ErrorDialog.Show(GetTree(), InMessage,
			InOkText: "Retour au Menu Principal",
			InOnOk: GoToMainMenu,
			InOnClose: GoToMainMenu);
	}

	/// <summary>
	/// Ménage standard pour quitter la phase Game vers le main menu : coupe
	/// la connexion ENet, vide <see cref="LobbyState"/> (sauf champs
	/// « survive-Clear »), rétablit le curseur visible, puis change de scène.
	/// Aligné sur le flux QuittingToMenu du WinningController et sur
	/// <see cref="LobbyController.GoToMainMenu"/>.
	/// </summary>
	private void GoToMainMenu()
	{
		// Cf. LobbyController.GoToMainMenu : même raison, on retire l'entrée
        // Firestore avant le Clear pour ne pas laisser un fantôme dans la salle.
        LobbyCleanup.LeaveRoomFireAndForget();
        NetworkManager.Instance?.Disconnect();
        LobbyState.Clear();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
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

		// On n'arme la bascule en spectateur que si la partie est encore en
		// cours (Playing). En Resolving/Aborted/Finalize, l'overlay GAME OVER
		// ou PARTIE INTERROMPUE va prendre le relais sous peu — pas la peine
		// d'enchaîner un état Spectating qu'on devrait immédiatement défaire.
		if (_fsm is null || !_fsm.Is(State.Playing)) return;
		if (_localPlayer is null) return;
		var label = _wastedInstance?.GetNodeOrNull<Label>("MarginContainer/Label");
		if (label is null) return;

		_spectatorEntryTween?.Kill();
		_spectatorEntryTween = CreateTween();
		_spectatorEntryTween.TweenInterval(_wastedHoldDuration);
		_spectatorEntryTween.TweenProperty(label, "modulate:a", 0f, _wastedFadeDuration);
		_spectatorEntryTween.TweenCallback(Callable.From(EnterSpectatorIfStillPlaying));
	}

	/// <summary>
	/// Callback du <see cref="_spectatorEntryTween"/>. Re-vérifie l'état FSM au
	/// moment du fire (et pas seulement à l'armement) parce que la phase peut
	/// avoir transité pendant le hold/fade — un mode peut se terminer entre la
	/// mort du joueur local et l'échéance du tween. Sans cette double vérification,
	/// le joueur entrerait en Spectating juste après GAME OVER.
	/// </summary>
	private void EnterSpectatorIfStillPlaying()
    {
        if (_fsm is null || !_fsm.Is(State.Playing)) return;
        if (_localPlayer is null) return;
        if (_localPlayer.GetCurrentState() != CharacterState.Dead) return;
        _localPlayer.TransitionTo(CharacterState.Spectating);
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
		// Le label peut avoir été fade-out par _spectatorEntryTween en mode
		// spectateur. On force l'alpha à 1 ici parce que les usages secondaires
        // (GAME OVER, PARTIE INTERROMPUE) doivent réafficher le label en clair
        // — sans ce reset, ils tomberaient sur un alpha=0 hérité du fade et le
        // joueur ne verrait rien.
        var labelNode = _wastedInstance.GetNodeOrNull<Label>("MarginContainer/Label");
        if (labelNode is not null)
        {
            var m = labelNode.Modulate;
            m.A = 1f;
            labelNode.Modulate = m;
        }
        if (InTextOverride is not null && labelNode is not null) labelNode.Text = InTextOverride;
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
