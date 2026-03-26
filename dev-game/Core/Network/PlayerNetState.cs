using System.IO;
using Godot;
using static Utils.CharacterUtils;

namespace Core.Network;

/// <summary>
/// Type de paquet réseau envoyé sur le wire.
/// Sert de premier octet dans chaque paquet pour identifier son contenu.
/// </summary>
public enum PacketType : byte
{
    /// <summary>Snapshot de l'état complet d'un joueur. Envoyé à 20 Hz en non-fiable.</summary>
    StateUpdate   = 0x01,
    /// <summary>Requête de spawn d'un joueur. Envoyé en fiable à la connexion.</summary>
    SpawnReq      = 0x02,
    /// <summary>Notification de despawn d'un joueur. Envoyé en fiable à la déconnexion.</summary>
    DespawnNotify = 0x03,
}

/// <summary>
/// Snapshot immuable de l'état réseau d'un joueur, sérialisé en 52 octets.
/// Layout wire : <c>PacketType(1) PeerId(4) Position(12) Velocity(12)
/// BodyYaw(4) HeadPitch(4) ArmPointDir(12) MoveState(1) EmoteState(1) Flags(1)</c>
/// </summary>
public readonly struct PlayerNetState
{
    /// <summary>Identifiant unique du pair ENet associé à ce joueur.</summary>
    public readonly int     PeerId;

    /// <summary>Position du personnage en world space.</summary>
    public readonly Vector3 Position;

    /// <summary>Vélocité actuelle du personnage.</summary>
    public readonly Vector3 Velocity;

    /// <summary>Rotation du corps autour de l'axe Y (yaw), en radians.</summary>
    public readonly float   BodyYaw;

    /// <summary>Angle de la tête autour de l'axe X (pitch), en radians.</summary>
    public readonly float   HeadPitch;

    /// <summary>Direction de pointage du bras droit en world space.</summary>
    public readonly Vector3 ArmPointDir;

    /// <summary>État de déplacement courant du personnage.</summary>
    public readonly MovementState MoveState;

    /// <summary>État d'emote courant du personnage.</summary>
    public readonly EmoteState    EmoteState;

    /// <summary>
    /// Champ de bits pour les états booléens.
    /// Bit 0 = <see cref="Aiming"/>, Bit 1 = <see cref="ArmsUp"/>.
    /// </summary>
    public readonly byte    Flags;

    /// <summary><c>true</c> si le personnage est en train de viser (bit 0 de <see cref="Flags"/>).</summary>
    public bool Aiming => (Flags & 0x01) != 0;

    /// <summary><c>true</c> si le personnage a les bras levés (bit 1 de <see cref="Flags"/>).</summary>
    public bool ArmsUp => (Flags & 0x02) != 0;

    /// <summary>
    /// Construit un snapshot d'état joueur complet.
    /// </summary>
    /// <param name="peerId">Identifiant ENet du pair.</param>
    /// <param name="position">Position en world space.</param>
    /// <param name="velocity">Vélocité actuelle.</param>
    /// <param name="bodyYaw">Rotation Y du corps en radians.</param>
    /// <param name="headPitch">Angle X de la tête en radians.</param>
    /// <param name="armPointDir">Direction de pointage du bras en world space.</param>
    /// <param name="moveState">État de déplacement sous forme de byte (<see cref="MovementState"/>).</param>
    /// <param name="emoteState">État d'emote sous forme de byte (<see cref="EmoteState"/>).</param>
    /// <param name="flags">Champ de bits booléens (Aiming, ArmsUp).</param>
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

    /// <summary>
    /// Sérialise un snapshot en tableau d'octets prêt à être envoyé sur le réseau.
    /// </summary>
    /// <param name="type">Le type de paquet à inscrire comme premier octet.</param>
    /// <param name="s">Le snapshot à sérialiser.</param>
    /// <returns>Un tableau de 52 octets en little-endian.</returns>
    /// <summary>
    /// Sérialise une notification de peer (5 octets) : <c>type(1) + peerId(4)</c>.
    /// Utilisé pour <see cref="PacketType.SpawnReq"/> et <see cref="PacketType.DespawnNotify"/>.
    /// </summary>
    public static byte[] SerializePeerNotify(PacketType type, int peerId)
    {
        var data = new byte[5];
        data[0] = (byte)type;
        System.BitConverter.GetBytes(peerId).CopyTo(data, 1);
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

    /// <summary>
    /// Désérialise un tableau d'octets reçu du réseau en snapshot typé.
    /// </summary>
    /// <param name="data">Les octets bruts reçus du transport.</param>
    /// <returns>Un tuple contenant le <see cref="PacketType"/> et le <see cref="PlayerNetState"/> reconstitué.</returns>
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
