using Godot;
namespace Core.Shared.StateMachine;

public interface IGameMode
{
    string DisplayName { get; }
    PackedScene Level { get; }
    /// <summary>
    /// Temps restant à afficher dans le HUD pendant la phase de jeu (secondes).
    /// Renvoyer 0 si le mode a pas de chrono (ex.&#160;: conditions
    /// de victoire/élimination)&#160;: le HUD affichera alors un placeholder.
    /// </summary>
    float RemainingSeconds { get; }
    void LoadLevel();
    void GetRelevantStats();
}
