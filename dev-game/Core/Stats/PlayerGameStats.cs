using static Utils.CharacterUtils;
namespace Core.Stats;

/// <summary>
/// Compteurs par joueur accumulés pendant une partie. Rempli côté authority sur
/// chaque <c>Player</c> (cf. <see cref="Character"/>/<see cref="Player"/>), agrégé
/// côté serveur en fin de phase Game via le RPC <c>SubmitStats</c>, puis consommé
/// par <see cref="ISubwinningCondition"/> pour départager le gagnant principal et
/// les sous-gagnants.
/// </summary>
/// <remarks>
/// Ajouter un nouveau compteur ici&#160;: ajouter le champ, l'incrémenter là où l'évènement
/// se produit (Character/Player), étendre le RPC <c>SubmitStats</c> côté GameController
/// pour le transporter, et ajouter une condition concrète dans <c>Core/Stats/Conditions</c>
/// qui le consomme.
/// </remarks>
public sealed class PlayerGameStats
{
    /// <summary>Identifiant ENet du peer auquel ces stats appartiennent.</summary>
    public int PeerId;

    /// <summary>Nombre de fois où le joueur est entré en état <c>Ragdoll</c>.</summary>
    public int RagdollCount;

    /// <summary>Temps cumulé passé en état <c>Ragdoll</c> (secondes).</summary>
    public float TotalRagdollSeconds;

    /// <summary>Nombre de sauts effectués pendant la partie.</summary>
    public int JumpCount;

    /// <summary>
    /// Temps de partie écoulé au moment de la mort (en secondes, mesuré depuis
    /// l'entrée en phase <c>Playing</c>). Reste à <c>-1f</c> si le joueur a survécu
    /// jusqu'à la fin&#160;: les conditions de type «&#160;Fastest to die&#160;» peuvent
    /// filtrer ces survivants.
    /// </summary>
    public float TimeOfDeathSeconds = -1f;

    /// <summary>Cause de la mort (cf. <see cref="DeathReason"/>). Pertinent seulement si <see cref="TimeOfDeathSeconds"/> &gt;= 0.</summary>
    public DeathReason DeathReason = DeathReason.Unknown;

    /// <summary>Indique si le joueur a survécu jusqu'à la fin de la partie.</summary>
    public bool Survived => TimeOfDeathSeconds < 0f;
}
