using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Joueur ayant ragdollé le plus de fois. Pertinence proportionnelle à l'écart
/// entre le 1er et le 2e&#160;: si tout le monde a ragdollé autant, la condition n'est
/// pas informative et sera filtrée (pertinence ≈ 0). À l'inverse, un joueur qui
/// domine largement (ex.&#160;: 30 vs 5) atteint une pertinence ≈ 1.
/// </summary>
public sealed class MostRagdolledCondition : ISubwinningCondition
{
    public string Id => "most_ragdolled";
    public string DisplayName => "Le plus ragdollé";
    public float BaseWeight => 10f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        int leader = 0;
        int max = -1;
        int secondMax = 0;
        foreach (var kv in InStats)
        {
            int v = kv.Value.RagdollCount;
            if (v > max)
            {
                secondMax = max < 0 ? 0 : max;
                max = v;
                leader = kv.Key;
            }
            else if (v > secondMax)
            {
                secondMax = v;
            }
        }
        if (leader == 0 || max <= 0) return (0, 0f);
        float relevance = (float)(max - secondMax) / max;
        return (leader, relevance);
    }

    public string FormatDetail(PlayerGameStats InStats)
    {
        return $"{InStats.RagdollCount} fois";
    }
}
