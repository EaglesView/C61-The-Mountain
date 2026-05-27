using Godot;
using System.Collections.Generic;

public partial class CharacterFollowers : Node3D
{
	[Export] public PackedScene CharacterScene;
	[Export(PropertyHint.Range, "0.1f,200f,1f")] public float AimDistance = 20f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float HeadFollowSpeed = 10f;
	[Export(PropertyHint.Range, "0,90,1")] public float MaxPitchDegrees = 45f;

	private readonly List<CharacterTracker> _trackers = new();
	private Camera3D _camera;
	private MeshInstance3D _aimTargetVisualizer;

	private class CharacterTracker
	{
		public Character Model;
		public float CurrentYaw;
		public float CurrentPitch;
	}

	public override void _Ready()
	{
		if (CharacterScene == null)
		{
			CharacterScene = ResourceLoader.Load<PackedScene>("res://Core/World/CharacterModel/Character/Character.tscn");
		}

		// Detect all Marker3D nodes and spawn characters at their locations
		foreach (Node child in GetChildren())
		{
			if (child is Marker3D marker)
			{
				var character = CharacterScene.Instantiate<Character>();

				// Freeze the character's FSM just like in Preview.cs
				character.IsPreview = true;

				// Set position and rotation BEFORE AddChild to prevent ragdoll physics explosion
				// (PhysicalBonesStartSimulation runs in PhysicsSkeleton._Ready when added to tree)
				character.Position = marker.Position;

				var tracker = new CharacterTracker
				{
					Model = character,
					CurrentYaw = marker.Rotation.Y
				};

				// Initialize the character facing the marker's direction
				character.Rotation = new Vector3(0, tracker.CurrentYaw, 0);

				AddChild(character);

				_trackers.Add(tracker);
			}
		}

		// Create a visible mesh to track the aim position in-game
		_aimTargetVisualizer = new MeshInstance3D();
		_aimTargetVisualizer.Name = "AimTargetVisualizer";

		var sphereMesh = new SphereMesh
		{
			Radius = 0.2f,
			Height = 0.4f
		};

		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 0, 0), // Bright Red
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		sphereMesh.Material = material;

		_aimTargetVisualizer.Mesh = sphereMesh;
		_aimTargetVisualizer.Visible = false;
		AddChild(_aimTargetVisualizer);
	}

	public override void _Process(double delta)
	{
		if (_trackers.Count == 0) return;

		if (_camera == null)
		{
			_camera = GetViewport().GetCamera3D();
			if (_camera == null) return;
		}

		Vector2 mousePos = GetViewport().GetMousePosition();

		// Mirror the mouse position horizontally across the screen.
		// This flawlessly mirrors the hit target without breaking their relative perspectives.

		Vector3 origin = _camera.ProjectRayOrigin(mousePos);
		Vector3 dir = _camera.ProjectRayNormal(mousePos);

		// Create an infinite mathematical plane facing the camera at AimDistance
		Vector3 cameraForward = -_camera.GlobalTransform.Basis.Z;
		Vector3 planeCenter = _camera.GlobalPosition + (cameraForward * AimDistance);
		Plane aimPlane = new Plane(cameraForward, planeCenter);

		// Mathematically intersect the ray with the plane (no physics engine required)
		Vector3? hitResult = aimPlane.IntersectsRay(origin, dir);

		if (hitResult.HasValue)
		{
			Vector3 hitPos = hitResult.Value;

			if (IsInstanceValid(_aimTargetVisualizer))
			{
				_aimTargetVisualizer.GlobalPosition = hitPos;
			}

			float t = Mathf.Clamp((float)delta * HeadFollowSpeed, 0f, 1f);

			foreach (var tracker in _trackers)
			{
				if (!IsInstanceValid(tracker.Model)) continue;

				Vector3 pPos = tracker.Model.GlobalPosition;
				Vector3 toTarget = hitPos - pPos;

				Vector3 targetXZ = new Vector3(hitPos.X, pPos.Y, hitPos.Z);
				float targetYaw = tracker.CurrentYaw;

				if (targetXZ.DistanceSquaredTo(pPos) > 0.001f)
				{
					// Compute optimal Y rotation looking at the mouse point on the XZ plane.
					// Negated to counter the inverted application at the bottom of this loop.
					Transform3D t3d = tracker.Model.GlobalTransform.LookingAt(targetXZ, Vector3.Up);
					targetYaw = t3d.Basis.GetEuler().Y;
				}

				// Compute Pitch (Look up / look down angle relative to distance)
				float distXZ = new Vector2(toTarget.X, toTarget.Z).Length();
				float targetPitch = Mathf.Atan2(toTarget.Y - 0.4f, distXZ);
				float maxPitchRad = Mathf.DegToRad(MaxPitchDegrees);
				targetPitch = Mathf.Clamp(targetPitch, -maxPitchRad, maxPitchRad);

				// Smoothly converge values
				tracker.CurrentYaw = Mathf.LerpAngle(tracker.CurrentYaw, targetYaw, t);
				tracker.CurrentPitch = Mathf.Lerp(tracker.CurrentPitch, targetPitch, t);

				tracker.Model.Rotation = new Vector3(0, tracker.CurrentYaw + (2 * Mathf.Pi / 2f), 0);

				if (tracker.Model.PhysicsSkelton != null)
				{
					tracker.Model.RotateHead(tracker.CurrentPitch);
					// Match the inverted mapping used in Character logic
					tracker.Model.PhysicsSkelton.HeadAngle = tracker.CurrentPitch;
				}
			}
		}
	}
}
