using Godot;
using System.Collections.Generic;
using Core.Stats;
namespace Core.Shared.StateMachine;

/// <summary>
/// Contrat d'un mode de jeu instancié dans le slot <c>Map</c> du <c>map_container</c>.
/// Étend <see cref="IPhase"/> via composition&#160;: le root du niveau implémente les deux.
/// </summary>
public interface IGameMode
{
    /// <summary>Nom affiché du mode (UI, logs, debug).</summary>
    string DisplayName { get; }

    /// <summary>Scène du niveau (PackedScene) associée à ce mode.</summary>
    PackedScene Level { get; }

    /// <summary>
    /// Temps restant à afficher dans le HUD pendant la phase de jeu (secondes).
    /// Renvoyer 0 si le mode a pas de chrono (ex.&#160;: conditions
    /// de victoire/élimination)&#160;: le HUD affichera alors un placeholder.
    /// </summary>
    float RemainingSeconds { get; }

    /// <summary>
    /// Liste des conditions de sous-victoire à évaluer en fin de partie, chacune
    /// pondérée pour ce mode. Le <see cref="SubwinningResolver"/> combine ces poids
    /// avec la pertinence retournée par chaque condition pour choisir le gagnant
    /// principal + les sous-gagnants.
    /// </summary>
    /// <remarks>
    /// Les modes doivent déclarer cette liste statiquement (pas de réallocation par
    /// frame)&#160;: les conditions sont stateless et réutilisables.
    /// </remarks>
    IReadOnlyList<WeightedCondition> SubwinningConditions { get; }

    void LoadLevel();
}
