using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Premier joueur à mourir (le plus petit <c>TimeOfDeathSeconds &gt;= 0</c>). Pertinence
/// égale à la proportion de joueurs ayant péri&#160;: si peu de monde meurt, la condition
/// est anecdotique&#160;; si presque tout le monde tombe (ex.&#160;: 4/5), elle devient l'élément
/// déterminant à mettre en avant côté UI.
/// </summary>
public sealed class FastestToDieCondition : ISubwinningCondition
{
    public string Id => "fastest_to_die";
    public string DisplayName => "Le plus rapide à mourir";
    public float BaseWeight => 8f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        if (InStats.Count == 0) return (0, 0f);

        int firstPeer = 0;
        float earliest = float.MaxValue;
        int deaths = 0;
        foreach (var kv in InStats)
        {
            if (kv.Value.Survived) continue;
            deaths++;
            if (kv.Value.TimeOfDeathSeconds < earliest)
            {
                earliest = kv.Value.TimeOfDeathSeconds;
                firstPeer = kv.Key;
            }
        }
        if (firstPeer == 0) return (0, 0f);
        float relevance = (float)deaths / InStats.Count;
        return (firstPeer, relevance);
    }

    public string FormatDetail(PlayerGameStats InStats)
    {
        return $"{InStats.TimeOfDeathSeconds:F1}s";
    }
}
