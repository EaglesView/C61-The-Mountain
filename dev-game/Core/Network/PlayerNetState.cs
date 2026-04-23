using System.IO;
using Godot;
using static Utils.CharacterUtils;

namespace Core.Network;

/// <summary>
/// Type de paquet réseau envoyé sur le wire.
/// </summary>
public enum PacketType : byte
{
    /// <summary>Snapshot de l'état complet d'un joueur. Envoyé à 20 Hz en non-fiable.</summary>
    StateUpdate     = 0x01,
    /// <summary>Correction de position envoyée par le serveur au client en cas de désync. 17 octets : type(1) peerId(4) pos(12).</summary>
    PositionCorrect = 0x04,
}

/// <summary>
/// Snapshot immuable de l'état réseau d'un joueur, sérialisé en 52 octets.
/// Layout wire : <c>PacketType(1) PeerId(4) Position(12) Velocity(12)
/// BodyYaw(4) HeadPitch(4) ArmPointDir(12) MoveState(1) EmoteState(1) Flags(1)</c>
/// </summary>
public readonly struct PlayerNetState
{
    public readonly int     PeerId;
    public readonly Vector3 Position;
    public readonly Vector3 Velocity;
    public readonly float   BodyYaw;
    public readonly float   HeadPitch;
    public readonly Vector3 ArmPointDir;
    public readonly MovementState MoveState;
    public readonly EmoteState    EmoteState;
    public readonly byte    Flags;

    public bool Aiming => (Flags & 0x01) != 0;
    public bool ArmsUp => (Flags & 0x02) != 0;

    public PlayerNetState(int peerId, Vector3 position, Vector3 velocity,
        float bodyYaw, float headPitch, Vector3 armPointDir,
        byte moveState, byte emoteState, byte flags)
    {
        PeerId      = peerId;
        Position    = position;
        Velocity    = velocity;
        BodyYaw     = bodyYaw;
        HeadPitch   = headPitch;
        ArmPointDir = armPointDir;
        MoveState   = (MovementState)moveState;
        EmoteState  = (EmoteState)emoteState;
        Flags       = flags;
    }

    public static byte[] SerializeCorrection(int peerId, Vector3 position)
    {
        var data = new byte[17];
        data[0] = (byte)PacketType.PositionCorrect;
        System.BitConverter.GetBytes(peerId).CopyTo(data, 1);
        System.BitConverter.GetBytes(position.X).CopyTo(data, 5);
        System.BitConverter.GetBytes(position.Y).CopyTo(data, 9);
        System.BitConverter.GetBytes(position.Z).CopyTo(data, 13);
        return data;
    }

    public static byte[] Serialize(PacketType type, PlayerNetState s)
    {
        using var ms = new MemoryStream(52);
        using var w  = new BinaryWriter(ms);
        w.Write((byte)type);
        w.Write(s.PeerId);
        w.Write(s.Position.X); w.Write(s.Position.Y); w.Write(s.Position.Z);
        w.Write(s.Velocity.X); w.Write(s.Velocity.Y); w.Write(s.Velocity.Z);
        w.Write(s.BodyYaw);
        w.Write(s.HeadPitch);
        w.Write(s.ArmPointDir.X); w.Write(s.ArmPointDir.Y); w.Write(s.ArmPointDir.Z);
        w.Write((byte)s.MoveState);
        w.Write((byte)s.EmoteState);
        w.Write(s.Flags);
        return ms.ToArray();
    }

    public static (PacketType type, PlayerNetState state) Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r  = new BinaryReader(ms);
        var type     = (PacketType)r.ReadByte();
        int peerId   = r.ReadInt32();
        var pos      = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var vel      = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        float yaw    = r.ReadSingle();
        float pitch  = r.ReadSingle();
        var arm      = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        byte move    = r.ReadByte();
        byte emote   = r.ReadByte();
        byte flags   = r.ReadByte();
        return (type, new PlayerNetState(peerId, pos, vel, yaw, pitch, arm, move, emote, flags));
    }
}
