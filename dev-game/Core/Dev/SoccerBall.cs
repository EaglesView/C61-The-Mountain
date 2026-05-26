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
		if (body is not CharacterBody3D charBody)
			return;

		Vector3 dir = (GlobalPosition - charBody.GlobalPosition).Normalized();
		float speed = charBody.Velocity.Length();
		ApplyCentralImpulse(dir * Mathf.Max(speed * KickMultiplier, MinKick));
	}
}
