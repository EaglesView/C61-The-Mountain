using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Premier joueur à franchir la ligne d'arrivée (mode Obby/Racing)&#160;: plus petit
/// <c>TimeOfFinishSeconds &gt;= 0</c>. Pertinence égale à la proportion de joueurs
/// ayant fini&#160;: si personne ne franchit, la condition est ignorée&#160;; si la majorité
/// termine, elle devient l'élément déterminant à mettre en avant côté UI.
/// </summary>
public sealed class FastestToFinishLineCondition : ISubwinningCondition
{
    public string Id => "fastest_to_finish";
    public string DisplayName => "Le plus rapide à la ligne d'arrivée";
    public float BaseWeight => 10f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        if (InStats.Count == 0) return (0, 0f);

        int firstPeer = 0;
        float earliest = float.MaxValue;
        int finishers = 0;
        foreach (var kv in InStats)
        {
            if (!kv.Value.Finished) continue;
            finishers++;
            if (kv.Value.TimeOfFinishSeconds < earliest)
            {
                earliest = kv.Value.TimeOfFinishSeconds;
                firstPeer = kv.Key;
            }
        }
        if (firstPeer == 0) return (0, 0f);
        float relevance = (float)finishers / InStats.Count;
        return (firstPeer, relevance);
    }

    public string FormatDetail(PlayerGameStats InStats)
    {
        return $"{InStats.TimeOfFinishSeconds:F1}s";
    }
}
