using Godot;
using System;
using Core.Shared.StateMachine;
namespace Core.World;

/// <summary>
/// Phase «&#160;Winning&#160;» de la FSM principale. Possède l'instance de
/// <c>winning.tscn</c> pendant qu'elle est active et écoute son signal
/// <c>LaunchNewLobby</c>. La phase enchaîne&#160;: <c>Celebration</c> (affichage
/// stats/podium) → <c>Vote</c> (les joueurs choisissent de relancer) →
/// <c>NewGame</c> (terminal, signale la fin via <see cref="IsDone"/>).
/// </summary>
public sealed partial class WinningController : Node3D, IPhase
{
	/// <summary>Scène <c>winning.tscn</c> à instancier à l'entrée de la phase.</summary>
	[Export] private PackedScene _winningSceneAsset;

	/// <summary>Durée de l'écran de célébration avant ouverture du vote.</summary>
	[Export] private float _celebrationDuration = 10f;

	/// <summary>Délai max du vote avant auto-avance vers <c>NewGame</c>.</summary>
	[Export] private float _voteTimeout = 30f;

	public enum State { Init, Failure, Celebration, Vote, NewGame }

	private StateMachine<State> _fsm;
	private WinningScene _winningInstance;
	private bool _voteComplete;
	private bool _done;

	public bool IsDone => _done;

	public void Enter()
	{
		_done = false;
		_voteComplete = false;

		if (_winningSceneAsset is null)
		{
			GD.PrintErr("[WinningController] _winningSceneAsset non assigné dans l'inspecteur.");
			_fsm = new StateMachine<State>(State.Failure, OnSubEnter, OnSubExit);
			OnSubEnter(State.Failure);
			return;
		}

		_winningInstance = _winningSceneAsset.Instantiate<WinningScene>();
		_winningInstance.LaunchNewLobby += OnLaunchNewLobby;
		AddChild(_winningInstance);

		_fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);

		// Init -> Celebration dès que l'instance est dans l'arbre.
		_fsm.When(State.Init,
			new PredicateCondition<State>(() => _winningInstance is not null && _winningInstance.IsInsideTree()),
			State.Celebration
		);

		// Celebration -> Vote après le délai d'affichage des stats.
		_fsm.When(State.Celebration,
			new TimeElapsedCondition<State>(_celebrationDuration),
			State.Vote
		);

		// Vote -> NewGame quand le signal LaunchNewLobby est reçu.
		_fsm.When(State.Vote,
			new PredicateCondition<State>(() => _voteComplete),
			State.NewGame
		);

		// Vote -> NewGame en fallback si le timeout du vote expire (évite le
		// blocage si personne ne clique).
		_fsm.When(State.Vote,
			new TimeElapsedCondition<State>(_voteTimeout),
			State.NewGame
		);

		OnSubEnter(State.Init);
	}

	public void Tick(float InDelta) => _fsm?.Tick(InDelta);

	public void Exit()
	{
		if (_winningInstance is not null)
		{
			_winningInstance.LaunchNewLobby -= OnLaunchNewLobby;
			_winningInstance.QueueFree();
			_winningInstance = null;
		}
		_fsm = null;
	}

	private void OnLaunchNewLobby() => _voteComplete = true;

	private void OnSubEnter(State InState)
	{
		switch (InState)
		{
			case State.NewGame:
			case State.Failure:
				_done = true;
				break;
		}
	}

	private void OnSubExit(State _) { }

	public override void _Ready() { }
}
