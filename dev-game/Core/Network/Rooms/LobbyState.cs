namespace Core.Network.Rooms;

public static class LobbyState
{
    public static RoomSnapshot? Current { get; private set; }
    public static bool IsHost { get; private set; }
    // Survives Clear() so World._Ready can read it after LobbyState is torn down
    public static string SelectedMapId { get; private set; } = MapRegistry.DefaultMapId;

    public static void SetSelectedMap(string mapId) => SelectedMapId = mapId;

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
