using Godot;
using System;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
using Core.UI.Loading;
namespace Core.World;

/// <summary>
/// Phase «&#160;Lobby&#160;» de la FSM principale. Possède l'instance de <c>lobby.tscn</c>
/// pendant qu'elle est active, écoute son signal <c>GameStartRequested</c>, puis
/// initie la connexion au serveur. Quand la connexion réussit, signale à la FSM
/// parente que la phase est terminée via <see cref="IsDone"/>.
/// </summary>
public sealed partial class LobbyController : Node3D, IPhase
{
	/// <summary>Scène <c>lobby.tscn</c> à instancier à l'entrée de la phase.</summary>
    [Export] private PackedScene _lobbySceneAsset;

    public enum State { Init, Failure, Waiting, Ready }

	/// <summary>État du transport ENet, indépendant du clic Start de l'utilisateur.</summary>
	private enum NetConn { Idle, Connecting, Connected, Failed }

	private StateMachine<State> _fsm;
	private LobbyScene _lobbyInstance;
	private bool _connectionSucceeded;
	private bool _connectionFailed;
	private bool _done;

	/// <summary>
	/// État du transport ENet. Avancé par <see cref="_BeginConnect"/> au
	/// lobby-enter (non plus à la sortie sur Start)&#160;: on connecte tôt pour
	/// que les Preview slots puissent se synchroniser via PreviewPose avant
	/// même le début de la partie. Une failure pendant le browse n'est PAS
    /// fatale&#160;: elle ne devient fatale (FSM Failure + ErrorDialog) que si
	/// l'utilisateur clique Start sans connexion.
	/// </summary>
	private NetConn _netState = NetConn.Idle;

	/// <summary>L'utilisateur a cliqué Start. Combiné avec <see cref="_netState"/>=Connected pour avancer la FSM.</summary>
    private bool _startRequested;

    /// <summary>
    /// Message à afficher dans <see cref="ErrorDialog"/> lorsque la FSM entre
	/// dans <see cref="State.Failure"/>. Renseigné par le chemin d'erreur qui
	/// déclenche la transition (asset manquant, échec de connexion réseau, …).
	/// Permet de centraliser l'affichage dans <see cref="OnSubEnter"/> au lieu
    /// de le dupliquer sur chaque point de défaillance.
    /// </summary>
    private string _failureMessage;

    public bool IsDone => _done;

    public void Enter()
    {
        _done = false;
        _connectionSucceeded = false;
        _connectionFailed = false;
        _startRequested = false;
        _netState = NetConn.Idle;
		// Repart d'un mapping vide à chaque entrée de phase. Le handshake
		// d'identité re-peuple immédiatement après la connexion ENet.
        LobbyPresence.Clear();

		// Serveur dédié : pas d'UI ni de polling Firestore. La state StateMachine attends
		// a Game les peers
		if (NetworkManager.Instance is not null && NetworkManager.Instance.IsServer)
		{
			// Belt-and-suspenders : vide les caches per-peer pour éviter qu'un
            // _lastKnownState hérité de la partie précédente déclenche une
            // correction anti-téléport sur le premier snapshot de la suivante.
            NetworkManager.Instance.ResetSessionState();
            _done = true;
            return;
        }

        // Côté client, même mesure : si la connexion ENet est conservée entre
        // les manches (cf. court-circuit dans OnGameStartRequested), purger les
        // caches locaux ne fait aucun mal.
        NetworkManager.Instance?.ResetSessionState();

        // Cas re-entrée Winning -> Lobby : le statut Firestore peut être encore
		// "started" depuis la partie précédente. Si on est l'hôte, on le remet
		// à "waiting" — sinon le polling de LobbyScene déclencherait un nouveau
		// GameStartRequested dès le prochain tick.
		var snapshot = LobbyState.Current;
		if (snapshot is not null && LobbyState.IsHost)
		{
			bool needsStatusReset = snapshot.Status == "started";
			bool needsMapUpdate = snapshot.MapId != LobbyState.SelectedMapId;

			if (needsStatusReset || needsMapUpdate)
			{
				var newSnapshot = new RoomSnapshot
				{
					Code = snapshot.Code,
					HostUserId = snapshot.HostUserId,
					ServerIp = snapshot.ServerIp,
					ServerPort = snapshot.ServerPort,
					Status = "waiting",
					MaxPlayers = snapshot.MaxPlayers,
					MapId = LobbyState.SelectedMapId,
					Players = snapshot.Players
				};
				LobbyState.Set(newSnapshot, true);
				_ = PushLobbyStateResetAsync(snapshot.Code, LobbyState.SelectedMapId, needsStatusReset, needsMapUpdate);
			}
		}

		if (_lobbySceneAsset is null)
		{
			GD.PrintErr("[LobbyController] _lobbySceneAsset non assigné dans l'inspecteur.");
			_failureMessage = "Configuration du lobby invalide : la scène n'est pas assignée.";
			_fsm = new StateMachine<State>(State.Failure, OnSubEnter, OnSubExit);
			OnSubEnter(State.Failure);
			return;
		}

		_lobbyInstance = _lobbySceneAsset.Instantiate<LobbyScene>();
		_lobbyInstance.GameStartRequested += OnGameStartRequested;
		AddChild(_lobbyInstance);

		// Bring up ENet dès l'entrée du lobby (au lieu d'attendre Start)&#160;:
		// permet aux Preview slots de synchroniser leurs poses (PreviewPose
		// packets) et alimente le handshake d'identité LobbyPresence. Une
		// failure ici est non-fatale (l'UI lobby reste utilisable sans sync)&#160;;
		// elle ne devient fatale que si l'utilisateur clique Start sans
        // connexion (voir OnGameStartRequested).
        _BeginConnect();

        _fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);

		// Init -> Waiting dès que l'instance est dans l'arbre.
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => _lobbyInstance is not null && _lobbyInstance.IsInsideTree()),
            State.Waiting
        );

        // Waiting -> Ready quand la connexion au serveur a réussi.
        _fsm.When(State.Waiting,
            new PredicateCondition<State>(() => _connectionSucceeded),
            State.Ready
        );

        // Waiting -> Failure si la connexion échoue.
        _fsm.When(State.Waiting,
            new PredicateCondition<State>(() => _connectionFailed),
            State.Failure
        );

        OnSubEnter(State.Init);
    }

    public void Tick(float InDelta) => _fsm?.Tick(InDelta);

    public void Exit()
    {
        UnsubscribeNetwork();

        if (_lobbyInstance is not null)
        {
            _lobbyInstance.GameStartRequested -= OnGameStartRequested;
            _lobbyInstance.QueueFree();
            _lobbyInstance = null;
        }
        _fsm = null;
        // On garde la connexion ENet vivante&#160;: GameController la réutilise.
		// En revanche le mapping userId↔peerId n'est plus pertinent hors lobby
		// (Game/Winning travaillent directement en peerId)&#160;; un re-entry
		// Winning → Lobby le repeuple via Enter ⇒ Clear ⇒ handshake.
		LobbyPresence.Clear();
	}

	private void OnGameStartRequested()
	{
		var snapshot = LobbyState.Current;
		if (snapshot is null)
		{
			GD.PrintErr("[LobbyController] LobbyState.Current null à GameStartRequested.");
			_failureMessage = "État du lobby manquant.";
			_connectionFailed = true;
			return;
		}

		LobbyState.SetSelectedMap(snapshot.MapId ?? MapRegistry.DefaultMapId);

		// Récupère le HatId du joueur local depuis sa propre entrée dans le
		// snapshot. Si l'utilisateur n'est pas (encore) authentifié ou n'a pas
		// d'entrée résolue, on retombe sur le défaut — pas de chapeau.
		string localUserId = Core.Auth.AuthServiceProvider.Instance.CurrentUser?.Id ?? "";
		string localHatId = HatRegistry.DefaultHatId;
		if (!string.IsNullOrEmpty(localUserId) && snapshot.Players.TryGetValue(localUserId, out var entry))
			localHatId = entry.HatId;
		LobbyState.SetSelectedHat(localHatId);
		LoadingScreen.Show(GetTree());

		_startRequested = true;
		switch (_netState)
		{
			case NetConn.Connected:
				LoadingScreen.SetStatus("Connexion existante — préparation de la partie", 0.35f);
				_connectionSucceeded = true;
				break;
			case NetConn.Connecting:
				// On attend l'aboutissement (success ⇒ _connectionSucceeded,
                // failure ⇒ _connectionFailed) côté handlers de _BeginConnect.
                LoadingScreen.SetStatus("Connexion au serveur…", 0.10f);
                break;
            case NetConn.Failed:
                // Background connect a déjà échoué pendant le browse&#160;:
				// remonter en fatal maintenant que l'utilisateur veut avancer.
				LoadingScreen.Hide();
				_connectionFailed = true;
				break;
			case NetConn.Idle:
				// Défensif&#160;: si _BeginConnect n'a pas tourné (snapshot null,
                // NetworkManager.Instance null), retenter ici.
                LoadingScreen.SetStatus("Connexion au serveur…", 0.10f);
                _BeginConnect();
                break;
        }
    }

    /// <summary>
	/// Démarre la connexion ENet vers le serveur dédié si elle n'est pas déjà
	/// active. Idempotent&#160;: réutilise une connexion vivante existante (cycle
	/// Winning → Lobby → Game sans Disconnect intermédiaire). Le statut RÉEL
	/// de la connexion est vérifié via <see cref="IsMultiplayerActuallyConnected"/>
	/// pour ne pas se faire piéger par un timeout ENet silencieux.
	/// </summary>
	private void _BeginConnect()
	{
		var net = NetworkManager.Instance;
		if (net is null) { _netState = NetConn.Failed; return; }

		if (net.IsRunning && net.IsClient && IsMultiplayerActuallyConnected())
		{
			_OnConnected();
			return;
		}

		var snapshot = LobbyState.Current;
		if (snapshot is null) { _netState = NetConn.Failed; return; }

		_netState = NetConn.Connecting;
		var serverIp = snapshot.ServerIp ?? Room.HardcodedServerIp;
		var serverPort = snapshot.ServerPort != 0 ? snapshot.ServerPort : Room.HardcodedServerPort;
		net.LocalConnected += OnNetConnected;
		net.ConnectionFailed += OnNetConnectionFailed;
		net.ConnectToServer(serverIp, serverPort);
	}

	private void OnNetConnected(int _)
	{
		UnsubscribeNetwork();
		_OnConnected();
	}

	private void _OnConnected()
	{
		_netState = NetConn.Connected;

		// Handshake d'identité&#160;: dire au serveur quel userId est associé à
        // notre peerId. Le serveur enregistre, broadcast aux autres clients,
        // et back-fill les identités existantes vers nous. On joint le flag
        // <c>isGuest</c> pour que le serveur puisse appliquer une règle
		// d'unicité ciblée sur les comptes invités (cf.
		// <see cref="ServerReceiveIdentity"/>).
		var currentUser = Core.Auth.AuthServiceProvider.Instance.CurrentUser;
		string localUserId = currentUser?.Id ?? "";
		bool isGuest = currentUser?.IsGuest ?? false;
		if (!string.IsNullOrEmpty(localUserId))
			RpcId(1, MethodName.ServerReceiveIdentity, localUserId, isGuest);

		// Si l'utilisateur avait déjà cliqué Start (cas&#160;: il a cliqué
		// pendant que la connexion était encore en cours), avance la FSM
		// maintenant que tout est en place.
		if (_startRequested)
		{
			LoadingScreen.SetStatus("Connecté — préparation de la partie", 0.35f);
			_connectionSucceeded = true;
		}
	}

	private void OnNetConnectionFailed(string InMessage)
	{
		UnsubscribeNetwork();
		_netState = NetConn.Failed;
		_failureMessage = $"Impossible de se connecter au serveur.\nMessage d'erreur :\n{InMessage}";
		// Failure pendant le browse&#160;: pas fatal. Seulement fatal si
		// l'utilisateur a déjà voulu démarrer (cas&#160;: connexion lente puis
        // échec après le clic Start).
        if (_startRequested)
        {
            LoadingScreen.Hide();
            _connectionFailed = true;
        }
        else
        {
			GD.PrintErr($"[LobbyController] Background connect failed (non-fatal during browse): {InMessage}");
        }
    }

    private void UnsubscribeNetwork()
    {
        var net = NetworkManager.Instance;
        if (net is null) return;
        net.LocalConnected -= OnNetConnected;
        net.ConnectionFailed -= OnNetConnectionFailed;
    }

	// ── Handshake d'identité userId↔peerId ──────────────────────────────────

	/// <summary>
	/// Client → Serveur&#160;: enregistre mon userId pour mon peerId actuel.
	/// Le serveur consigne dans <see cref="LobbyPresence"/>, rebroadcast aux
	/// autres clients, puis back-fill les identités déjà connues vers nous.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ServerReceiveIdentity(string userId, bool isGuest)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (string.IsNullOrEmpty(userId) || senderId <= 1) return;

		// Unicité de session pour les invités&#160;: Firebase accepte le même
		// "invite{N}@outilsenligne.ca" depuis plusieurs clients (cf. Login.cs),
		// donc c'est ici qu'on garde la porte. Si le userId est déjà mappé à
		// un autre peer encore connecté, on refuse&#160;: on signale au sender
		// pour qu'il affiche un message, puis on coupe son lien ENet.
		// PeerDisconnected nettoiera LobbyPresence côté serveur (cf.
		// NetworkManager._Ready). Cette règle ne s'applique PAS aux comptes
		// utilisateurs normaux — eux peuvent rester multi-sessions.
		if (isGuest
			&& LobbyPresence.TryGetPeerId(userId, out int existingPeer)
			&& existingPeer != senderId
			&& IsPeerStillConnected(existingPeer))
		{
			RpcId(senderId, MethodName.ClientReceiveIdentityRejected,
				"Cette session invité est déjà utilisée. Réessayez pour obtenir un autre invité.");
			// Disconnect non-forcé&#160;: laisse ENet flusher le paquet RPC
			// ci-dessus avant de fermer le lien, pour que le client ait une
			// chance de lire la raison du rejet plutôt qu'un simple
			// "ServerDisconnected".
			Multiplayer.MultiplayerPeer?.DisconnectPeer(senderId);
			return;
		}

		LobbyPresence.Set(senderId, userId);
		// Broadcast aux autres clients pour qu'ils peuplent leur map.
        Rpc(MethodName.ClientReceiveIdentity, senderId, userId);
        // Back-fill&#160;: envoyer au sender toutes les identités déjà connues.
        foreach (var (existingPeerSnap, existingUser) in LobbyPresence.Snapshot())
        {
            if (existingPeerSnap == senderId) continue;
            RpcId(senderId, MethodName.ClientReceiveIdentity, existingPeerSnap, existingUser);
        }
    }

    // Vérifie via la liste des peers ENet que l'entrée LobbyPresence n'est pas
    // un fantôme (cas&#160;: PeerDisconnected programmé mais pas encore traité).
    // Sans ça, un retry légitime juste après un disconnect pourrait être
    // refusé à tort.
    private bool IsPeerStillConnected(int peerId)
    {
        foreach (int p in Multiplayer.GetPeers())
            if (p == peerId) return true;
        return false;
    }

    /// <summary>Serveur → Clients&#160;: enregistre une identité (sender + back-fill).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReceiveIdentity(int peerId, string userId)
    {
        LobbyPresence.Set(peerId, userId);
    }

    /// <summary>
    /// Serveur → Client refusé&#160;: le userId invité que le sender a annoncé
    /// est déjà détenu par un autre peer connecté. Le serveur va couper le
    /// lien ENet juste après ce RPC&#160;; on enregistre le message pour que la
	/// FSM remonte un dialog d'erreur explicite plutôt qu'un "Lost connection
	/// to server" générique.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReceiveIdentityRejected(string reason)
    {
		GD.PrintErr($"[LobbyController] Identity rejected by server: {reason}");
        _failureMessage = reason;
        _connectionFailed = true;
    }

    /// <summary>
    /// Vrai uniquement si la session multijoueur courante a un peer assigné,
	/// que ce peer est dans l'état <c>Connected</c>, et que le local peer ID
	/// a été attribué (&gt; 0). Les flags <c>IsRunning</c> et <c>IsClient</c>
	/// du NetworkManager sont insuffisants&#160;: ils ne reflètent que l'état
    /// local (provider instancié, rôle assigné) et restent vrais après un
    /// timeout silencieux côté ENet. Cette vérification couvre les deux trous.
    /// </summary>
    private bool IsMultiplayerActuallyConnected()
    {
		// Multiplayer est une propriété d'instance de Node (raccourci vers
		// GetTree().GetMultiplayer()) — d'où le non-static. Le LobbyController
		// est dans l'arbre quand cette méthode tourne, donc Multiplayer est
		// disponible.
		var peer = Multiplayer.MultiplayerPeer;
		if (peer is null) return false;
		if (peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return false;
		// GetUniqueId() == 0 -> aucun peer / non-connecté. Les vrais clients
		// ont un ID > 1 (1 est réservé au serveur).
		return Multiplayer.GetUniqueId() > 1;
	}

	private static async System.Threading.Tasks.Task PushLobbyStateResetAsync(string InCode, string InMapId, bool resetStatus, bool updateMap)
	{
		try
		{
			if (resetStatus)
				await RoomServiceProvider.Repository.UpdateStatusAsync(InCode, "waiting");
			if (updateMap)
				await RoomServiceProvider.Repository.UpdateMapAsync(InCode, InMapId);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyController] Reset statut/map salle échoué: {ex.Message}");
		}
	}

	private void OnSubEnter(State InState)
	{
		switch (InState)
		{
			case State.Ready:
				_done = true;
				break;
			case State.Failure:
				// Point d'entrée unique : tous les chemins d'erreur (asset
				// manquant, échec réseau, …) renseignent _failureMessage puis
				// routent vers Failure. Sans dialog ici l'état serait
                // terminal silencieux (IsDone jamais vrai) et la FSM
                // principale soft-lockerait.
                ShowFailureDialog();
                break;
        }
    }

    private void OnSubExit(State _) { }

    /// <summary>
	/// Affiche le dialog d'erreur final de la phase Lobby. Idempotent : si un
	/// dialog est déjà visible (typiquement parce qu'OnNetConnectionFailed a
    /// déjà rempli le message et que la FSM ré-entre Failure), on ne le
    /// rouvre pas. Les deux boutons (OK et X) déclenchent le même retour au
	/// menu principal pour que l'utilisateur ne puisse pas rester coincé.
	/// </summary>
	private void ShowFailureDialog()
	{
		if (ErrorDialog.IsVisible) return;
		ErrorDialog.Show(GetTree(),
			string.IsNullOrEmpty(_failureMessage)
				? "Erreur inattendue durant l'initialisation du lobby."
				: _failureMessage,
			InOkText: "Retour au Menu Principal",
			InOnOk: GoToMainMenu,
			InOnClose: GoToMainMenu);
	}

	/// <summary>
	/// Ménage standard pour quitter la phase vers le main menu : coupe la
	/// connexion ENet, vide le LobbyState (sauf champs « survive-Clear »),
	/// rétablit le curseur visible, puis change de scène. Aligné sur le flux
	/// QuittingToMenu du WinningController.
	/// </summary>
	private void GoToMainMenu()
	{
		// Retire l'entrée joueur de Firestore avant de vider LobbyState (sinon
		// le snapshot référencé par le cleanup est déjà null). Sans cet appel,
		// un quitteur via ErrorDialog reste listé pour les autres clients.
		LobbyCleanup.LeaveRoomFireAndForget();
		NetworkManager.Instance?.Disconnect();
		LobbyState.Clear();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}

	public override void _Ready() { }
}
