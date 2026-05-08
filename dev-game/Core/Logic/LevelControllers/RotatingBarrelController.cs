using Godot;
using Core.Shared.StateMachine;
namespace Core.World;

public sealed partial class RotatingBarrelController : Node3D, IPhase, IGameMode
{
	[Export] public PackedScene CurrentLevel;
	private enum Phase { Normal, Hard, Done }
	private StateMachine<Phase> _fsm = null;
	public string DisplayName => "Rotating Barrel";
	public PackedScene Level => CurrentLevel;
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);

	public void Enter()
	{
		_fsm = new StateMachine<Phase>(Phase.Normal);
		_fsm.When(Phase.Normal, new TimeElapsedCondition<Phase>(30f), Phase.Hard);
		_fsm.When(Phase.Hard, new TimeElapsedCondition<Phase>(30f), Phase.Done);
	}
	public void Tick(float InDelta) => _fsm?.Tick(InDelta);
	public void Exit() => /**TODO: Cleanup ici */GD.Print("Oublie qqchose?");

	// TODO: implémentations réelles. Pour l'instant ces stubs satisfont
	// l'interface IGameMode pour permettre la compilation.
	public void LoadLevel() { }
	public void GetRelevantStats() { }
}
