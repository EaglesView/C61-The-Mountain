using System.Collections.Generic;

namespace Core.Network.Rooms;

public sealed class RoomSnapshot
{
    public string Code       { get; init; } = "";
    public string HostUserId { get; init; } = "";
    public string ServerIp   { get; init; } = "";
    public int    ServerPort  { get; init; }
    public string Status     { get; init; } = "";
    public int    MaxPlayers  { get; init; }
    public Dictionary<string, PlayerEntry> Players { get; init; } = new();

    public sealed class PlayerEntry
    {
        public string Username { get; init; } = "";
        public bool   IsHost   { get; init; }
    }
}
