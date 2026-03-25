using Core.Auth;

namespace Core.Network.Rooms;

public static class RoomServiceProvider
{
    private static RoomRepository? _repo;

    public static RoomRepository Repository =>
        _repo ??= new RoomRepository(AuthServiceProvider.GetCurrentToken);

    public static void Reset() => _repo = null;
}
