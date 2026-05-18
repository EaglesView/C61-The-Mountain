using System.Collections.Generic;
namespace Core.Stats.Conditions;

/// <summary>
/// Le dernier survivant (single-life last-man-standing). Poids de base très élevé
/// pour dominer naturellement les autres conditions quand un seul joueur a survécu.
/// Pertinence&#160;= 1 si exactement un survivant, 0 sinon&#160;: dans tous les autres cas
/// (zéro ou plusieurs survivants), une autre condition (départage par stats) prend le relais.
/// </summary>
public sealed class LastSurvivorCondition : ISubwinningCondition
{
    public string Id => "last_survivor";
    public string DisplayName => "Dernier survivant";
    public float BaseWeight => 100f;

    public (int peerId, float relevance) Evaluate(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        int survivor = 0;
        int count = 0;
        foreach (var kv in InStats)
        {
            if (!kv.Value.Survived) continue;
            survivor = kv.Key;
            count++;
            if (count > 1) return (0, 0f);
        }
        return count == 1 ? (survivor, 1f) : (0, 0f);
    }
}
