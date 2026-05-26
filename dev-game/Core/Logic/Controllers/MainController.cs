using Godot;
using System;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
using System.Collections.Generic;
namespace Core.World;
/// <summary>
///
public sealed partial class MainController : Node3D
{
	[Export] private LobbyController _lobbyController;
	[Export] private GameController _gameController;
	[Export] private WinningController _winningController;
	public enum State { Lobby, Game, Winning, Loading }
	private StateMachine<State> _fsm = null;
	private IPhase _current = null;
	private readonly Dictionary<State, IPhase> _phases = new();
	private bool _quitScheduled;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_phases[State.Lobby] = _lobbyController;
		_phases[State.Game] = _gameController;
		_phases[State.Winning] = _winningController;
		_current = _phases[State.Lobby];

		_fsm = new StateMachine<State>(State.Lobby, OnEnter, OnExit);

		// Transition Lobby -> Jeu
		_fsm.When(State.Lobby,
			new PredicateCondition<State>(() => _phases[State.Lobby].IsDone),
			State.Game
		);
		// Transition Jeu -> Winning
		_fsm.When(State.Game,
			new PredicateCondition<State>(() => _phases[State.Game].IsDone),
			State.Winning
		);
		// Transision Winning -> Lobby
		_fsm.When(State.Winning,
		new PredicateCondition<State>(() => _phases[State.Winning].IsDone),
		State.Lobby
		);
		//Entrer dans l etat lobby manuellement
		_current.Enter();

		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.ServerDisconnected += OnServerDisconnected;
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		_current.Tick((float)delta);

		// Quit volontaire depuis Winning&#160;: court-circuite la FSM principale
		// pour ne pas instancier la lobby UI alors que la scène est sur le
		// point d'être remplacée par le main menu. Fait le ménage (Exit
        // propre de Winning, disconnect réseau, clear LobbyState, mouse mode)
		// avant d'enchaîner sur ChangeSceneToFile.
		if (!_quitScheduled && _winningController is not null && _winningController.QuittingToMenu)
		{
			_quitScheduled = true;
			_current.Exit();
			// Retire le joueur de la salle Firestore avant de couper ENet + vider
			// LobbyState. Sans cette ligne, le joueur restait listé dans la salle
			// pour les autres clients (qui polent toutes les 4s), même après être
			// rentré au main menu — d'où l'impression «&#160;il est encore dans le lobby&#160;».
			LobbyCleanup.LeaveRoomFireAndForget();
			NetworkManager.Instance?.Disconnect();
			LobbyState.Clear();
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
			return;
		}

		_fsm.Tick((float)delta);
	}

	private void OnEnter(State s)
	{
		GD.Print($"[LoadDiag] MainController phase ENTER: {s} (server={NetworkManager.Instance?.IsServer ?? false})");
		_current = _phases[s];
		_current.Enter();
	}
	private void OnExit(State s)
	{
		GD.Print($"[LoadDiag] MainController phase EXIT: {s} (server={NetworkManager.Instance?.IsServer ?? false})");
		_phases[s].Exit();
	}

	private void OnServerDisconnected()
	{
		if (_quitScheduled || (NetworkManager.Instance != null && NetworkManager.Instance.IsServer)) return;

		GD.Print("[MainController] Forcing quit to main menu due to server disconnect.");
		_quitScheduled = true;
		_current?.Exit();

		LobbyCleanup.LeaveRoomFireAndForget();
		NetworkManager.Instance?.Disconnect();
		LobbyState.Clear();
		Input.MouseMode = Input.MouseModeEnum.Visible;

		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}

}
