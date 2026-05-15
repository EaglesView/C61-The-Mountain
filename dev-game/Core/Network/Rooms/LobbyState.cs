using System;
using System.Collections.Generic;
namespace Core.Network.Rooms;

public static class LobbyState
{
    public static RoomSnapshot? Current { get; private set; }
    public static bool IsHost { get; private set; }
    // Survives Clear() so World._Ready can read it after LobbyState is torn down
    public static string SelectedMapId { get; private set; } = MapRegistry.DefaultMapId;

    /// <summary>Peer ID du gagnant principal de la dernière partie (0 si aucune).</summary>
    public static int LastWinnerPeerId { get; private set; }

    /// <summary>Identifiant de la condition qui a désigné le gagnant principal (ex. "last_survivor", "fastest_to_die").</summary>
    public static string LastWinnerConditionId { get; private set; } = "";

    /// <summary>Libellé affichable de la condition principale.</summary>
    public static string LastWinnerConditionLabel { get; private set; } = "";

    /// <summary>
    /// Entrées de sous-gagnants&#160;: jusqu'à 3 (peer, conditionId, label). Le label
    /// est précomputé pour que l'UI puisse l'afficher tel quel.
    /// </summary>
    public static IReadOnlyList<(int PeerId, string ConditionId, string Label)> LastSubWinners { get; private set; }
        = Array.Empty<(int, string, string)>();

    public static void SetSelectedMap(string mapId) => SelectedMapId = mapId;

    /// <summary>
    /// Mémorise les gagnants de la dernière partie. Survit à <see cref="Clear"/> pour que
    /// la phase Winning puisse l'afficher après la destruction du LobbyState courant.
    /// </summary>
    public static void SetWinnerData(int mainPeerId, string mainConditionId, string mainConditionLabel,
        IReadOnlyList<(int PeerId, string ConditionId, string Label)> subWinners)
    {
        LastWinnerPeerId = mainPeerId;
        LastWinnerConditionId = mainConditionId ?? "";
        LastWinnerConditionLabel = mainConditionLabel ?? "";
        LastSubWinners = subWinners ?? Array.Empty<(int, string, string)>();
    }

    public static void Set(RoomSnapshot snapshot, bool isHost)
    {
        Current = snapshot;
        IsHost = isHost;
    }

    /// <summary>
    /// Shortcut pour connecter directement, sans Firebase
    /// Utilise pour les devs
    /// </summary>
    public static void SetDirect(string serverIp, int serverPort = 7777)
    {
        Current = new RoomSnapshot { ServerIp = serverIp, ServerPort = serverPort };
        IsHost = false;
    }

    ///<summary>
    /// Permet de vider le LobbyState
    /// </summary>
    public static void Clear()
    {
        Current = null;
        IsHost = false;
    }
}
