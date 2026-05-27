using Godot;
using System;

public partial class Camera3d : Camera3D
{
	[Export]
	public float Speed { get; set; } = 5.0f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public override void _PhysicsProcess(double delta)
	{
		// Move forward (negative Z axis) at the specified speed
		Translate(Vector3.Forward * Speed * (float)delta);
	}
}
