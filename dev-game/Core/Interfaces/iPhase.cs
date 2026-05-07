namespace Core.Share.StateMachine;
/// <summary>
/// Phase d'exécution attachée à un état d'une FSM parente. La FSM parente appelle
/// <see cref="Enter"/> à l'entrée de l'état, <see cref="Tick"/> à chaque frame, et
/// <see cref="Exit"/> à la sortie. La phase signale sa terminaison via <see cref="IsDone"/>.
/// </summary>
public interface IPhase
{
    bool isDone { get; }
    void Enter();
    void Tick(float InDelta);
    void Exit();
}
