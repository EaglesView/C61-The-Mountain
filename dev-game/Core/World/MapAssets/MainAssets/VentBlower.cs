using Godot;
using System;
using static Utils.CharacterUtils;

public partial class VentBlower : StaticBody3D
{
	[Export] public required Area3D BlowArea;
	[Export] public float BlowForce = 20.0f;

	public override void _PhysicsProcess(double delta)
	{
		foreach (var body in BlowArea.GetOverlappingBodies())
		{
			if (body is PhysicalBone3D bone)
			{
				bone.LinearVelocity += Vector3.Up * BlowForce * (float)delta;
				FindCharacterInParents(bone)?.TransitionTo(CharacterState.Ragdoll);
			}
		}
	}

	private static Character? FindCharacterInParents(Node node)
	{
		var current = node.GetParent();
		while (current != null)
		{
			if (current is Character c) return c;
			current = current.GetParent();
		}
		return null;
	}
}
