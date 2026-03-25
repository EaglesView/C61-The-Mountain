namespace Core.Network.Rooms;

public static class LobbyState
{
    public static RoomSnapshot? Current { get; private set; }
    public static bool IsHost { get; private set; }

    public static void Set(RoomSnapshot snapshot, bool isHost)
    {
        Current = snapshot;
        IsHost  = isHost;
    }

    public static void Clear()
    {
        Current = null;
        IsHost  = false;
    }
}
