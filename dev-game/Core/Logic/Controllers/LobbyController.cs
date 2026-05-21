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

    private StateMachine<State> _fsm;
    private LobbyScene _lobbyInstance;
    private bool _connectionSucceeded;
    private bool _connectionFailed;
    private bool _done;

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
		if (LobbyState.IsHost && snapshot is not null && snapshot.Status == "started")
		{
			_ = ResetRoomStatusAsync(snapshot.Code);
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
	}

	private void OnGameStartRequested()
	{
		var snapshot = LobbyState.Current;
		if (snapshot is null)
		{
			GD.PrintErr("[LobbyController] LobbyState.Current null à GameStartRequested.");
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

		var net = NetworkManager.Instance;
		// Réutilisation de la connexion si on est déjà client connecté (cycle
		// Winning → Lobby → Game sans Disconnect intermédiaire). Un second
		// ConnectToServer allouerait un nouveau ENetMultiplayerPeer en
		// écrasant l'existant sans le fermer — fuite + état réseau zombie.
		//
		// IMPORTANT : on vérifie le statut RÉEL de la connexion via
		// MultiplayerPeer.GetConnectionStatus() + Multiplayer.GetUniqueId().
		// Sans cette vérification, une connexion morte silencieusement (timeout
		// ENet ~30s sans trafic, et Winning peut durer 60+s sans keep-alive)
		// passait quand même par ce short-circuit : Role=Client + _peer non-null
		// restent vrais alors que le peer n'a plus de session active. On lèverait
		// _connectionSucceeded=true à tort, et les RPCs ClientReady/HostMapPick
		// envoyés par GameController iraient dans le vide → loading collé,
		// playersChildCount=0 côté client, aucun log d'erreur.
        if (net is not null && net.IsRunning && net.IsClient
            && IsMultiplayerActuallyConnected())
        {
            LoadingScreen.SetStatus("Connexion existante — préparation de la partie", 0.35f);
            _connectionSucceeded = true;
            return;
        }

        LoadingScreen.SetStatus("Connexion au serveur…", 0.10f);

        var serverIp = snapshot.ServerIp ?? Room.HardcodedServerIp;
        var serverPort = snapshot.ServerPort != 0 ? snapshot.ServerPort : Room.HardcodedServerPort;
        net.LocalConnected += OnNetConnected;
        net.ConnectionFailed += OnNetConnectionFailed;
        net.ConnectToServer(serverIp, serverPort);
    }

    private void OnNetConnected(int _)
    {
        UnsubscribeNetwork();
        LoadingScreen.SetStatus("Connecté — préparation de la partie", 0.35f);
        _connectionSucceeded = true;
    }

    private void OnNetConnectionFailed(string InMessage)
    {
        UnsubscribeNetwork();
        LoadingScreen.Hide();
		// Le dialog est affiché par OnSubEnter(Failure) — point d'entrée unique
		// pour tous les chemins d'erreur de la phase Lobby. On ne fait ici que
        // capturer le message et lever le flag qui pousse la FSM vers Failure.
		_failureMessage = $"Impossible de se connecter au serveur.\nMessage d'erreur :\n{InMessage}";
        _connectionFailed = true;
    }

    private void UnsubscribeNetwork()
    {
        var net = NetworkManager.Instance;
        if (net is null) return;
        net.LocalConnected -= OnNetConnected;
        net.ConnectionFailed -= OnNetConnectionFailed;
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

    private static async System.Threading.Tasks.Task ResetRoomStatusAsync(string InCode)
    {
        try
        {
			await RoomServiceProvider.Repository.UpdateStatusAsync(InCode, "waiting");
        }
        catch (Exception ex)
        {

			GD.PrintErr($"[LobbyController] Reset statut salle échoué: {ex.Message}");
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
