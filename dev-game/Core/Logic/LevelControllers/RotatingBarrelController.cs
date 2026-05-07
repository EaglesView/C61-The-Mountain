public sealed class RotatingBarrelController : IPhase, IGameMode
{
    [Export] public PackedScene CurrentLevel;
    private enum Phase { Normal, Hard, Done }
    private StateMachine<Phase> _fsm = null;
    public string DisplayName => "Rotating Barrel";
    public PackedScene Level => CurrentLevel;
    public bool isDone => _fsm.Is(Phase.Done);

    public void Enter()
    {
        _fsm = new StateMachine<Phase>(Phase.Normal);
        _fsm.When(Phase.Normal, new TimerElapsedCondition<Phase>(30f), Phase.Hard);
        _fsm.When(Phase.Hard, new TimerElapsedCondition<Phase>(30f), Phase.Done);
    }
    public void Tick(float InDelta) => _fsm.Tick(inDelta);
    public void Exit() => /**TODO: Cleanup ici */GD.Print("Oublie qqchose?");
}
