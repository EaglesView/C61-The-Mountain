using Godot;
using System;

public partial class Blade : MeshInstance3D
{
	public float Speed = 0.0f;

	public override void _Process(double delta)
	{
		RotateY(Speed * (float)delta);
	}
}