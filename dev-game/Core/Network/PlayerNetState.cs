using System.IO;
using Godot;
using static Utils.CharacterUtils;

namespace Core.Network;

public enum PacketType : byte
{
    StateUpdate   = 0x01,
    SpawnReq      = 0x02,
    DespawnNotify = 0x03,
}

/// <summary>
/// 52-byte wire struct: 1 prefix + 51 state bytes.
/// Layout: PacketType(1) PeerId(4) Position(12) Velocity(12)
///         BodyYaw(4) HeadPitch(4) ArmPointDir(12) MoveState(1) EmoteState(1) Flags(1)
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
