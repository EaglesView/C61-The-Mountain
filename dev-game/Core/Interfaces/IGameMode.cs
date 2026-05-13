using Godot;
namespace Core.Shared.StateMachine;

public interface IGameMode
{
    string DisplayName { get; }
    PackedScene Level { get; }
    void LoadLevel();
    void GetRelevantStats();
}
