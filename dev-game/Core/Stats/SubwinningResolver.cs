using System.Collections.Generic;
namespace Core.Stats;

/// <summary>
/// Combine les <see cref="WeightedCondition"/> déclarées par un mode de jeu avec le snapshot
/// de stats agrégé côté serveur pour produire&#160;:
/// <list type="bullet">
/// <item>un gagnant principal (la condition la mieux notée)&#160;;</item>
/// <item>jusqu'à trois sous-gagnants (conditions suivantes, dé-dupliquées par peer).</item>
/// </list>
/// Algorithme&#160;: <c>finalScore = BaseWeight × ModeWeight × relevance</c>. Les conditions
/// dont la pertinence est nulle ou qui ne désignent personne sont écartées.
/// </summary>
public sealed class SubwinningResolver
{
    /// <summary>Une entrée de sous-gagnant&#160;: peer + identifiant + libellé de la condition gagnée.</summary>
    public sealed record SubWinnerEntry(int PeerId, string ConditionId, string DisplayName, string Detail, float Score);

    /// <summary>
    /// Résultat de l'algorithme. <c>MainWinnerPeerId == 0</c> signifie qu'aucune condition
    /// n'était applicable (cas dégénéré, ex.&#160;: aucun joueur, partie interrompue).
    /// </summary>
    public sealed record Result(
        int MainWinnerPeerId,
        string MainConditionId,
        string MainConditionDisplayName,
        string MainConditionDetail,
        IReadOnlyList<SubWinnerEntry> SubWinners);

    private const int MaxSubWinners = 3;

    public Result Resolve(
        IReadOnlyDictionary<int, PlayerGameStats> InStats,
        IReadOnlyList<WeightedCondition> InModeConditions)
    {
        var scored = new List<SubWinnerEntry>();

        for (int i = 0; i < InModeConditions.Count; i++)
        {
            var wc = InModeConditions[i];
            var (peerId, relevance) = wc.Condition.Evaluate(InStats);
            if (peerId == 0 || relevance <= 0f) continue;

            string detail = "";
            if (InStats.TryGetValue(peerId, out var stats))
            {
                detail = wc.Condition.FormatDetail(stats);
            }

            float finalScore = wc.Condition.BaseWeight * wc.ModeWeight * relevance;
            scored.Add(new SubWinnerEntry(peerId, wc.Condition.Id, wc.Condition.DisplayName, detail, finalScore));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (scored.Count == 0)
            return new Result(0, "", "", "", System.Array.Empty<SubWinnerEntry>());

        var main = scored[0];
        var subs = new List<SubWinnerEntry>(MaxSubWinners);

        // Dé-doublonnage par peer pour mettre en valeur différents joueurs… SAUF
        // s'il n'y a qu'un seul joueur dans le snapshot (mode solo&#160;: un joueur
        // unique mérite alors de rafler toutes les sous-conditions, sinon les
        // panneaux restent vides).
        int uniquePeers = CountUniquePeers(InStats);
        bool dedupeByPeer = uniquePeers > 1;
        var seenPeers = new HashSet<int> { main.PeerId };
        for (int i = 1; i < scored.Count && subs.Count < MaxSubWinners; i++)
        {
            if (dedupeByPeer && !seenPeers.Add(scored[i].PeerId)) continue;
            subs.Add(scored[i]);
        }

        return new Result(main.PeerId, main.ConditionId, main.DisplayName, main.Detail, subs);
    }

    private static int CountUniquePeers(IReadOnlyDictionary<int, PlayerGameStats> InStats)
    {
        var seen = new HashSet<int>();
        foreach (var kv in InStats) seen.Add(kv.Key);
        return seen.Count;
    }
}
