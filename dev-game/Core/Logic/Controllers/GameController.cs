using Godot;
using System;
using Core.Shared.StateMachine;
namespace Core.World;

public sealed partial class GameController : Node3D, IPhase
{

    public enum State { Init, Failure, Waiting, Playing, Resolving }
    private StateMachine<State> _fsm = null;
    private IGameMode _mode = null;
    private bool _done;
    public void Enter()
    {
        _done = false;
        //_mode = ???
        _fsm = new StateMachine<State>(State.Init, OnEnter, OnExit);
        _fsm.When(State.Init,
            new PredicateCondition<State>(() =>/* TODO:Inclure level loaded */ true),
            State.Waiting
        );
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => Init.Error),
            State.Failure
        );
        _fsm.When(State.Waiting,
            new TimeElapsedCondition<State>(() => 10f),
            State.Waiting
        );
        _fsm.When(State.Playing,
            new PredicateCondition<State>(() => _mode.IsDone),
            State.Resolving
        );
        _fsm.When(State.Resolving,
            new TimeElapsedCondition<State>(() => 1f), //self loop en attendant le Exit()
            State.Waiting
        );
        OnEnter(State.Init);
    }
    public void Tick(float InDelta)
    {
        if (_fsm.Is(State.Playing)) _mode.Tick(InDelta);
        _fsm.Tick(InDelta);
    }
    public void Exit()
    {
        if (_fsm.Is(SubState.Playing)) _mode.Exit();
    }
    private void OnEnter(State s)
    {
        switch (s)
        {
            case State.Playing: _mode.Enter(); break;
            case State.Resolving: _done = true;
        }
    }
    private void OnExit(State _) { }


}
