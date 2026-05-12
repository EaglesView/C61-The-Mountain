using Godot;
using System;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
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
			_done = true;
			return;
		}

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

		var serverIp = snapshot.ServerIp ?? Room.HardcodedServerIp;
		var serverPort = snapshot.ServerPort != 0 ? snapshot.ServerPort : Room.HardcodedServerPort;
		LobbyState.SetSelectedMap(snapshot.MapId ?? MapRegistry.DefaultMapId);
		// Note: pas de LobbyState.Clear() ici — la FSM peut cycler Winning -> Lobby
		// et la prochaine entrée du LobbyScene relit Current pour bootstrap son UI.

		var net = NetworkManager.Instance;
		net.LocalConnected += OnNetConnected;
		net.ConnectionFailed += OnNetConnectionFailed;
		net.ConnectToServer(serverIp, serverPort);
	}

	private void OnNetConnected(int _)
	{
		UnsubscribeNetwork();
		_connectionSucceeded = true;
	}

	private void OnNetConnectionFailed(string InMessage)
	{
		UnsubscribeNetwork();
		GD.PrintErr($"[LobbyController] Connexion échouée&#160;: {InMessage}");
		_connectionFailed = true;
	}

	private void UnsubscribeNetwork()
	{
		var net = NetworkManager.Instance;
		if (net is null) return;
		net.LocalConnected -= OnNetConnected;
		net.ConnectionFailed -= OnNetConnectionFailed;
	}

	private static async System.Threading.Tasks.Task ResetRoomStatusAsync(string InCode)
	{
		try
		{
			await RoomServiceProvider.Repository.UpdateStatusAsync(InCode, "waiting");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyController] Reset statut salle échoué&#160;: {ex.Message}");
		}
	}

	private void OnSubEnter(State InState)
	{
		switch (InState)
		{
			case State.Ready:
			case State.Failure:
				_done = true;
				break;
		}
	}

	private void OnSubExit(State _) { }

	public override void _Ready() { }
}
