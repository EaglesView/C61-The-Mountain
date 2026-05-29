using Godot;

namespace Core.Network.Rooms;

/// <summary>
/// Helpers partagés pour les chemins «&#160;retour au main menu&#160;» (quit
/// volontaire depuis Winning, ErrorDialog du Lobby, ErrorDialog du Game).
/// Centralise le ménage Firestore — sans ça, un quitteur reste listé pour
/// les autres clients qui polent la salle toutes les 4 secondes, donnant
/// l'illusion qu'il est encore dans le lobby. Fire-and-forget&#160;: la
/// suppression ne doit pas bloquer le <c>ChangeSceneToFile</c> qui suit.
/// </summary>
public static class LobbyCleanup
{
    /// <summary>
    /// Capture <see cref="LobbyState.Current"/> + l'utilisateur courant et
    /// lance le ménage Firestore sans attendre. À appeler AVANT
    /// <see cref="LobbyState.Clear"/> (sinon le snapshot est déjà perdu).
    /// <list type="bullet">
    /// <item>Hôte&#160;: supprime entièrement la salle. Les non-hôtes qui
    /// polent verront <c>GetAsync</c> renvoyer null — le LobbyScene les
    /// ramène alors au main menu.</item>
    /// <item>Non-hôte&#160;: retire uniquement son entrée dans
    /// <c>players/{uid}</c>, la salle continue d'exister.</item>
    /// </list>
    /// No-op silencieux s'il n'y a pas de salle active ou pas
    /// d'utilisateur authentifié.
    /// </summary>
    public static void LeaveRoomFireAndForget()
    {
        var snapshot = LobbyState.Current;
        if (snapshot is null) return;
        var me = Core.Auth.AuthServiceProvider.Instance?.CurrentUser;
        if (me is null || string.IsNullOrEmpty(me.Id)) return;
        if (string.IsNullOrEmpty(snapshot.Code)) return;

        var code = snapshot.Code;
        var userId = me.Id;
        bool isHost = LobbyState.IsHost;
        _ = LeaveAsync(code, userId, isHost);
    }

    private static async System.Threading.Tasks.Task LeaveAsync(string code, string userId, bool isHost)
    {
        try
        {
            if (isHost)
                await RoomServiceProvider.Repository.DeleteRoomAsync(code);
            else
                await RoomServiceProvider.Repository.RemovePlayerAsync(code, userId);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[LobbyCleanup] Leave failed for {userId}@{code} (isHost={isHost}): {ex.Message}");
        }
    }
}
