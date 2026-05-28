using Godot;
using System.Collections.Generic;

public partial class SnowballThrower : Node3D
{
	[Export] public PackedScene SnowballScene;
	[Export(PropertyHint.Range, "5,200,1")] public float SnowballSpeed = 55f;
	[Export(PropertyHint.Range, "0.5,10,0.1")] public float SnowballLifetime = 4f;
	[Export(PropertyHint.Range, "0.5,10,0.1")] public float RagdollRecoverTime = 2f;
	[Export(PropertyHint.Range, "10,500,5")] public float HitImpulseStrength = 90f;
	[Export(PropertyHint.Range, "0.0,5.0,0.05")] public float SpawnOffset = 1.0f;

	private Camera3D _camera;
	private readonly Dictionary<Character, Timer> _recoverTimers = new();

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb
			&& mb.Pressed
			&& mb.ButtonIndex == MouseButton.Left)
		{
			ThrowSnowball(mb.Position);
		}
	}

	private void ThrowSnowball(Vector2 mousePos)
	{
		if (SnowballScene == null)
		{
			GD.PushWarning("[SnowballThrower] SnowballScene is not assigned.");
			return;
		}
		if (_camera == null || !IsInstanceValid(_camera))
		{
			_camera = GetViewport().GetCamera3D();
			if (_camera == null) return;
		}

		Vector3 origin = _camera.ProjectRayOrigin(mousePos);
		Vector3 dir = _camera.ProjectRayNormal(mousePos).Normalized();

		Node instance = SnowballScene.Instantiate();
		if (instance is not RigidBody3D ball)
		{
			GD.PushWarning("[SnowballThrower] SnowballScene root must be a RigidBody3D.");
			instance.QueueFree();
			return;
		}

		// Force contact reporting on so BodyEntered fires regardless of scene config.
		ball.ContactMonitor = true;
		if (ball.MaxContactsReported < 1) ball.MaxContactsReported = 4;

		GetTree().CurrentScene.AddChild(ball);
		ball.GlobalPosition = origin + dir * SpawnOffset;
		ball.LinearVelocity = dir * SnowballSpeed;

		ball.BodyEntered += (Node body) => OnSnowballHit(ball, body, dir);

		var lifetime = new Timer { WaitTime = SnowballLifetime, OneShot = true };
		ball.AddChild(lifetime);
		lifetime.Timeout += () => { if (IsInstanceValid(ball)) ball.QueueFree(); };
		lifetime.Start();
	}

	private void OnSnowballHit(RigidBody3D ball, Node body, Vector3 hitDir)
	{
		Character target = FindCharacterAncestor(body);
		if (target != null && IsInstanceValid(target))
		{
			HitCharacter(target, body as PhysicalBone3D, hitDir);
		}
		if (IsInstanceValid(ball)) ball.QueueFree();
	}

	private static Character FindCharacterAncestor(Node node)
	{
		for (Node n = node; n != null; n = n.GetParent())
		{
			if (n is Character c) return c;
		}
		return null;
	}

	private void HitCharacter(Character target, PhysicalBone3D directHitBone, Vector3 hitDir)
	{
		var skel = target.PhysicsSkelton;
		if (skel == null) return;

		// Bypass the FSM (preview characters early-return out of _PhysicsProcess);
		// flipping IsRagdoll directly turns off the spring forces in PhysicsSkeleton.
		skel.IsRagdoll = true;

		// Kick the whole rig so the punch is visible even if the ball didn't
		// hit a specific bone (e.g. it hit the capsule first).
		Vector3 kick = hitDir.Normalized() * HitImpulseStrength;
		skel.ApplyRagdollKick(kick * 0.4f);
		if (directHitBone != null && IsInstanceValid(directHitBone))
			directHitBone.LinearVelocity += kick;

		if (_recoverTimers.TryGetValue(target, out Timer existing) && IsInstanceValid(existing))
		{
			existing.Stop();
			existing.WaitTime = RagdollRecoverTime;
			existing.Start();
		}
		else
		{
			var t = new Timer { WaitTime = RagdollRecoverTime, OneShot = true };
			AddChild(t);
			t.Timeout += () => RecoverCharacter(target);
			_recoverTimers[target] = t;
			t.Start();
		}
	}

	private void RecoverCharacter(Character target)
	{
		if (IsInstanceValid(target) && target.PhysicsSkelton != null)
		{
			// Springs in PhysicsSkeleton pull the bones back to the animated pose;
			// RagdollExitGraceTime prevents the spring forces from immediately
			// re-triggering RagdollTriggered while bones are still snapping back.
			target.PhysicsSkelton.IsRagdoll = false;
		}
		if (_recoverTimers.TryGetValue(target, out Timer t))
		{
			_recoverTimers.Remove(target);
			if (IsInstanceValid(t)) t.QueueFree();
		}
	}
}
