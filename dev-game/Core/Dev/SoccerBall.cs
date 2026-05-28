using Godot;

namespace Core.World;

public partial class SoccerBall : RigidBody3D
{
	private const float KickMultiplier = 4.0f;
	private const float MinKick = 4.0f;

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node body)
	{
		// Seul le serveur applique l'impulsion : le MultiplayerSynchronizer
		// attaché à la balle (cf. soccer_dev.tscn) propage ensuite la nouvelle
		// linear/angular velocity à tous les clients. IsServer() est true en
		// offline, donc le flow standalone passe aussi.
		if (!Multiplayer.IsServer())
			return;

		if (body is not CharacterBody3D charBody)
			return;

		Vector3 dir = (GlobalPosition - charBody.GlobalPosition).Normalized();
		float speed = charBody.Velocity.Length();
		ApplyCentralImpulse(dir * Mathf.Max(speed * KickMultiplier, MinKick));
	}
}
