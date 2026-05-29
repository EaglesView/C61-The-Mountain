using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Joueur ayant sauté le plus de fois. Même barème de pertinence que les autres
/// conditions «&#160;leader vs second&#160;»&#160;: l'écart relatif détermine si l'info vaut
/// la peine d'être affichée.
/// </summary>
public sealed class MostJumpsCondition : ISubwinningCondition
{
    public string Id => "most_jumps";
    public string DisplayName => "Le plus sauteur";
    public float BaseWeight => 5f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        int leader = 0;
        int max = -1;
        int secondMax = 0;
        foreach (var kv in InStats)
        {
            int v = kv.Value.JumpCount;
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
        return $"{InStats.JumpCount} fois";
    }
}
