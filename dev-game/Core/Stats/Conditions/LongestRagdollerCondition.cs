using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Joueur ayant cumulé le plus de temps en ragdoll. Distinct de
/// <see cref="MostRagdolledCondition"/>&#160;: ici on récompense les longues sessions
/// au sol (typiquement les bons «&#160;clients du baril&#160;») plutôt que la fréquence
/// d'entrée en ragdoll.
/// </summary>
public sealed class LongestRagdollerCondition : ISubwinningCondition
{
    public string Id => "longest_ragdoller";
    public string DisplayName => "Le plus longtemps au sol";
    public float BaseWeight => 7f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        int leader = 0;
        float max = -1f;
        float secondMax = 0f;
        foreach (var kv in InStats)
        {
            float v = kv.Value.TotalRagdollSeconds;
            if (v > max)
            {
                secondMax = max < 0f ? 0f : max;
                max = v;
                leader = kv.Key;
            }
            else if (v > secondMax)
            {
                secondMax = v;
            }
        }
        if (leader == 0 || max <= 0f) return (0, 0f);
        float relevance = (max - secondMax) / max;
        return (leader, relevance);
    }

    public string FormatDetail(PlayerGameStats InStats)
    {
        return $"{InStats.TotalRagdollSeconds:F1}s";
    }
}
