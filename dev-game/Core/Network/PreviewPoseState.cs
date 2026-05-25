using System.IO;

namespace Core.Network;

/// <summary>
/// Pose minimale d'un penguin de <c>Preview</c> (Lobby&#160;/&#160;Winning)&#160;:
/// orientation de la tête uniquement, le reste de la simulation étant calculé
/// localement par le <c>Character</c> en <c>CharacterState.Preview</c> sur
/// chaque client. Yaw rotate le root du penguin&#160;; pitch est écrit dans
/// <c>PhysicsSkeleton.HeadAngle</c>.
/// <para>
/// Wire layout (13 octets)&#160;:
/// <c>PacketType(1) PeerId(4) Yaw(4) Pitch(4)</c>. Pas de timestamp&#160;: le
/// flux est unreliable haute fréquence, le dernier paquet reçu fait foi.
/// </para>
/// </summary>
public readonly struct PreviewPoseState
{
	public readonly int   PeerId;
	public readonly float Yaw;
	public readonly float Pitch;

	public PreviewPoseState(int peerId, float yaw, float pitch)
	{
		PeerId = peerId;
		Yaw    = yaw;
		Pitch  = pitch;
	}

	public static byte[] Serialize(PreviewPoseState s)
	{
		using var ms = new MemoryStream(13);
		using var w  = new BinaryWriter(ms);
		w.Write((byte)PacketType.PreviewPose);
		w.Write(s.PeerId);
		w.Write(s.Yaw);
		w.Write(s.Pitch);
		return ms.ToArray();
	}

	public static PreviewPoseState Deserialize(byte[] data)
	{
		using var ms = new MemoryStream(data);
		using var r  = new BinaryReader(ms);
		r.ReadByte(); // PacketType — déjà discriminé par l'appelant.
		int peerId  = r.ReadInt32();
		float yaw   = r.ReadSingle();
		float pitch = r.ReadSingle();
		return new PreviewPoseState(peerId, yaw, pitch);
	}
}
