using Godot;
using System;
using Core.Shared.StateMachine;
namespace Core.World;
/// <summary>
///
public sealed partial class LobbyController : Node3D
{

    public enum State { Init, Failure, Waiting, Ready }
    private StateMachine<State> _fsm = null;
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _fsm = new StateMachine<State>(State.Lobby, OnEnter, OnExit);

        // Transition tout les joueurs prets vers la partie en cours
        _fsm.When(State.Lobby,
        new PredicateCondition<State>(() => Lobby.AllPlayersReady),
        State.Game
        );

    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    //public override void _Process(double delta) => _fsm.Tick((float)delta);

    private void OnEnter(State s) { }
    private void OnExit(State s) { }

}
