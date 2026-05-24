using System;
using System.Collections.Generic;
namespace Core.Network.Rooms;

public static class LobbyState
{
    public static RoomSnapshot? Current { get; private set; }
    public static bool IsHost { get; private set; }
    // Survives Clear() so World._Ready can read it after LobbyState is torn down
    public static string SelectedMapId { get; private set; } = MapRegistry.DefaultMapId;

    /// <summary>
    /// Chapeau cosmétique sélectionné par le joueur local pour la partie en
    /// cours. Survit à <see cref="Clear"/> pour que <c>GameController.Enter</c>
    /// puisse le transmettre via le RPC <c>ClientReady</c> au moment du spawn.
    /// </summary>
    public static string SelectedHatId { get; private set; } = HatRegistry.DefaultHatId;

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

    /// <summary>
    /// Snapshot peerId → hatId capturé par <c>GameController</c> au moment de
    /// la résolution de la partie. Lu par la phase Winning pour reconstruire
    /// le penguin du gagnant (et des sous-gagnants) avec le bon chapeau. Vide
    /// par défaut&#160;; survit à <see cref="Clear"/> au même titre que
    /// <see cref="LastWinnerPeerId"/>.
    /// </summary>
    public static IReadOnlyDictionary<int, string> LastHats { get; private set; }
        = new Dictionary<int, string>();

    public static void SetSelectedMap(string mapId) => SelectedMapId = mapId;
    public static void SetSelectedHat(string hatId) => SelectedHatId = string.IsNullOrEmpty(hatId) ? HatRegistry.DefaultHatId : hatId;

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

    /// <summary>
    /// Pousse le snapshot des chapeaux de la partie qui vient de finir. Copie
    /// défensive pour ne pas aliaser le dictionnaire du <c>GameController</c>
    /// (qui est vidé à <c>Exit</c>).
    /// </summary>
    public static void SetWinnerHats(IReadOnlyDictionary<int, string> hatsByPeer)
    {
        if (hatsByPeer is null || hatsByPeer.Count == 0)
        {
            LastHats = new Dictionary<int, string>();
            return;
        }
        var copy = new Dictionary<int, string>(hatsByPeer.Count);
        foreach (var kv in hatsByPeer) copy[kv.Key] = kv.Value ?? HatRegistry.DefaultHatId;
        LastHats = copy;
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
