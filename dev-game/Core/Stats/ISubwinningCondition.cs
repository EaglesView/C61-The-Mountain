using System.Collections.Generic;
namespace Core.Stats;

/// <summary>
/// Contrat d'une «&#160;condition de sous-victoire&#160;»&#160;: une règle qui désigne
/// un peer gagnant et retourne une mesure de pertinence dans le contexte courant.
/// Le <see cref="SubwinningResolver"/> combine <see cref="BaseWeight"/>, le poids
/// déclaré par le mode (<see cref="WeightedCondition.ModeWeight"/>) et la pertinence
/// retournée pour ordonner les conditions et choisir gagnant principal + sous-gagnants.
/// </summary>
/// <remarks>
/// Ajouter une nouvelle condition&#160;: créer une classe scellée dans
/// <c>Core/Stats/Conditions/</c>, implémenter cette interface, puis la déclarer dans la
/// liste <c>SubwinningConditions</c> du mode de jeu concerné (<see cref="IGameMode"/>).
/// </remarks>
public interface ISubwinningCondition
{
    /// <summary>Identifiant stable&#160;: clef utilisée dans les écritures Firestore et la délivrance UI.</summary>
    string Id { get; }

    /// <summary>Libellé affichable (français). Ex.&#160;: «&#160;Le plus ragdollé&#160;».</summary>
    string DisplayName { get; }

    /// <summary>Poids «&#160;à pleine pertinence&#160;» de cette condition. Multiplié par <see cref="WeightedCondition.ModeWeight"/> et la pertinence.</summary>
    float BaseWeight { get; }

    /// <summary>
    /// Évalue la condition sur le snapshot de stats courant. Retourne&#160;:
    /// <list type="bullet">
    /// <item><c>peerId</c>&#160;: le peer désigné gagnant par cette condition (0 si non applicable)&#160;;</item>
    /// <item><c>relevance</c> dans [0..1]&#160;: 1 = la condition est pleinement informative, 0 = à ignorer (sera filtrée).</item>
    /// </list>
    /// </summary>
    (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats);

    /// <summary>
    /// Formate la stat pertinente pour le UI (ex: "15 fois").
    /// </summary>
    string FormatDetail(PlayerGameStats InStats);
}

/// <summary>
/// Association d'une condition à un poids spécifique du mode de jeu. Les modes
/// déclarent une liste de <c>WeightedCondition</c> via <c>IGameMode.SubwinningConditions</c>&#160;:
/// le même <see cref="ISubwinningCondition"/> peut avoir un poids différent selon le mode.
/// </summary>
public sealed class WeightedCondition
{
    public ISubwinningCondition Condition { get; }
    public float ModeWeight { get; }

    public WeightedCondition(ISubwinningCondition InCondition, float InModeWeight)
    {
        Condition = InCondition;
        ModeWeight = InModeWeight;
    }
}
