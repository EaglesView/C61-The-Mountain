using Godot;
using System;
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
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		_current.Tick((float)delta);
		_fsm.Tick((float)delta);
	}

	private void OnEnter(State s) { _current = _phases[s]; _current.Enter(); }
	private void OnExit(State s) { _phases[s].Exit(); }

}
