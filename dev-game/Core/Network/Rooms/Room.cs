using System;

namespace Core.Network.Rooms;

public sealed class Room
{
    private static readonly char[] Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public const string HardcodedServerIp   = "5.78.190.227";
    public const int    HardcodedServerPort  = 7777;
    public const string DefaultStatus        = "waiting";
    public const int    DefaultMaxPlayers    = 8;
    public const string SharedLobbyCode      = "MAIN";

    public string Code          { get; }
    public string HostUserId    { get; }
    public string HostUsername  { get; }

    public Room(string code, string hostUserId, string hostUsername)
    {
        Code         = code         ?? throw new ArgumentNullException(nameof(code));
        HostUserId   = hostUserId   ?? throw new ArgumentNullException(nameof(hostUserId));
        HostUsername = hostUsername ?? throw new ArgumentNullException(nameof(hostUsername));
    }

    public static string GenerateCode()
    {
        var rng    = new Random();
        var result = new char[6];
        for (int i = 0; i < result.Length; i++)
            result[i] = Chars[rng.Next(Chars.Length)];
        return new string(result);
    }
}
