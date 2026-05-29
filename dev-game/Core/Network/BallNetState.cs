using System.IO;
using Godot;

namespace Core.Network;

/// <summary>
/// Snapshot immuable de l'état physique d'une balle (ou de tout RigidBody3D
/// serveur-autoritaire singleton de scène). Sérialisé en 53 octets.
/// Layout wire&#160;: <c>PacketType(1) Position(12) Rotation(16 quat) LinearVelocity(12) AngularVelocity(12)</c>.
/// Sert <see cref="PacketType.BallStateUpdate"/>.
/// </summary>
public readonly struct BallNetState
{
	public readonly Vector3    Position;
	public readonly Quaternion Rotation;
	public readonly Vector3    LinearVelocity;
	public readonly Vector3    AngularVelocity;

	public BallNetState(Vector3 position, Quaternion rotation,
		Vector3 linearVelocity, Vector3 angularVelocity)
	{
		Position        = position;
		Rotation        = rotation;
		LinearVelocity  = linearVelocity;
		AngularVelocity = angularVelocity;
	}

	public static byte[] Serialize(BallNetState s)
	{
		using var ms = new MemoryStream(53);
		using var w  = new BinaryWriter(ms);
		w.Write((byte)PacketType.BallStateUpdate);
		w.Write(s.Position.X);        w.Write(s.Position.Y);        w.Write(s.Position.Z);
		w.Write(s.Rotation.X);        w.Write(s.Rotation.Y);        w.Write(s.Rotation.Z);        w.Write(s.Rotation.W);
		w.Write(s.LinearVelocity.X);  w.Write(s.LinearVelocity.Y);  w.Write(s.LinearVelocity.Z);
		w.Write(s.AngularVelocity.X); w.Write(s.AngularVelocity.Y); w.Write(s.AngularVelocity.Z);
		return ms.ToArray();
	}

	public static BallNetState Deserialize(byte[] data)
	{
		using var ms = new MemoryStream(data);
		using var r  = new BinaryReader(ms);
		r.ReadByte(); // PacketType — déjà discriminé par l'appelant.
		var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		var lin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		var ang = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		return new BallNetState(pos, rot, lin, ang);
	}
}
