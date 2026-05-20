using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Shared.Infrastructure;
namespace Core.Stats;

/// <summary>
/// Persiste les stats de fin de partie dans Firestore. Une écriture <c>games/{gameId}</c>
/// pour le doc racine (map jouée, peer gagnant, timestamp) et une écriture par joueur
/// dans la sous-collection <c>games/{gameId}/players/{peerId}</c>.
/// </summary>
/// <remarks>
/// L'authentification utilise le token Firebase du joueur courant. Le serveur dédié
/// (qui n'a pas de compte utilisateur) ne doit pas appeler ce repo directement&#160;: la
/// règle est que le client hôte (<c>LobbyState.IsHost</c>) reçoit les stats agrégées
/// du serveur et écrit lui-même dans Firestore avec son propre token.
///
/// Si on a accès à un identifiant utilisateur Firebase (UID) au moment de l'écriture
/// (via <c>LobbyState.Current.Players</c>), on l'enregistre dans le doc joueur sous le
/// champ <c>uid</c>&#160;; sinon on stocke seulement le <c>peerId</c>, qui restera la clef
/// (à corréler plus tard quand le routage peer→UID sera consolidé).
/// </remarks>
public sealed class GameStatsRepository
{
    private readonly FirestoreClient _client;
    private readonly Func<string> _getIdToken;

    public GameStatsRepository(FirestoreClient InClient, Func<string> InGetIdToken)
    {
        _client = InClient;
        _getIdToken = InGetIdToken;
    }

    /// <summary>
    /// Écrit le doc racine de la partie. À appeler une fois par partie après que le
    /// gagnant a été déterminé.
    /// </summary>
    public async Task SaveGameAsync(string InGameId, string InMapId, int InWinnerPeerId,
        string InWinnerConditionId)
    {
        var fields = new
        {
            mapId = new { stringValue = InMapId ?? "" },
            winnerPeerId = new { integerValue = InWinnerPeerId.ToString() },
            winnerConditionId = new { stringValue = InWinnerConditionId ?? "" },
            playedAt = new { timestampValue = DateTime.UtcNow.ToString("o") },
        };
        await _client.SetDocumentAsync("games", InGameId, fields, _getIdToken());
    }

    /// <summary>
    /// Écrit les stats d'un joueur pour une partie donnée. Les sous-conditions gagnées
    /// sont sérialisées en tableau de strings (peuvent être vides).
    /// </summary>
    public async Task SavePlayerStatsAsync(string InGameId, PlayerGameStats InStats,
        string InMainConditionWonId, IReadOnlyList<string> InSubConditionsWonIds, string InUid = "")
    {
        // Firestore REST veut un format imbriqué pour les arrays&#160;: arrayValue.values[].stringValue
        var subValues = new List<object>(InSubConditionsWonIds?.Count ?? 0);
        if (InSubConditionsWonIds is not null)
        {
            foreach (var id in InSubConditionsWonIds)
                subValues.Add(new { stringValue = id ?? "" });
        }

        var fields = new
        {
            peerId = new { integerValue = InStats.PeerId.ToString() },
            uid = new { stringValue = InUid ?? "" },
            ragdollCount = new { integerValue = InStats.RagdollCount.ToString() },
            totalRagdollSeconds = new { doubleValue = (double)InStats.TotalRagdollSeconds },
            jumpCount = new { integerValue = InStats.JumpCount.ToString() },
            timeOfDeathSeconds = new { doubleValue = (double)InStats.TimeOfDeathSeconds },
            deathReason = new { stringValue = InStats.DeathReason.ToString() },
            mainConditionWonId = new { stringValue = InMainConditionWonId ?? "" },
            subConditionsWonIds = new { arrayValue = new { values = subValues.ToArray() } },
        };

        string collection = $"games/{InGameId}/players";
        await _client.SetDocumentAsync(collection, InStats.PeerId.ToString(), fields, _getIdToken());
    }
}
