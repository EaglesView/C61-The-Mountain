/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:               Player.cs                           |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Permet de contrôler le personnage client         |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;
using System;
using Core.Network;
using static Utils.CharacterUtils;
public partial class Character : CharacterBody3D
{

	/// ····································
	/// : _____  _____  ___  ___ _____ ___ :
	/// :| __\ \/ | _ \/ _ \| _ |_   _/ __|:
	/// :| _| >  <|  _| (_) |   / | | \__ \:
	/// :|___/_/\_|_|  \___/|_|_\ |_| |___/:
	/// ····································
	[ExportGroup("Character Nodes")]
	[Export] protected AnimationPlayer AnimPlayer;
	[Export] public PhysicsSkeleton PhysicsSkelton;

	[ExportGroup("Fall Recovery")]
	[Export] public float FallLimit = -50.0f;

	protected float speed;
	protected float jumpVelocity = 5.0f;
	protected Vector3 velocity = Vector3.Zero;
	protected Vector3 moveVec = Vector3.Zero;
	protected Vector3 aimVec = Vector3.Zero;
	protected Vector3 pointVec = Vector3.Forward;
	protected MovementState currentMovementState = MovementState.Idle;
	protected EmoteState currentEmoteState = EmoteState.None;
	protected float prevHeadAngle, headAngle = 0.0f;
	private bool _prevRagdoll = false;
	public Vector3 SpawnPosition { get; set; } = Vector3.Zero;


	public void RotateHead(float InXAngle)
	{
		prevHeadAngle = headAngle;
		headAngle = InXAngle;
	}
	public void PointAt(Vector3 InDirection)
	{
		PhysicsSkelton.ArmPointDir = InDirection;
	}
	public Vector3 GetHeadBonePosition()
	{
		return PhysicsSkelton.GetPoseTargetSkel().Origin;
	}
	public Vector3 GetPhysicsHeadBonePosition()
	{
		return PhysicsSkelton.GetPoseTargetSkel(true).Origin;
	}
	public float GetHeadAngle() => headAngle;

	// ── Network ──────────────────────────────────────────────────────────────

	public int PeerId { get; set; } = 0;

	public virtual PlayerNetState SnapshotState()
	{
		byte flags = 0;
		if (PhysicsSkelton.Aiming) flags |= 0x01;
		if (PhysicsSkelton.ArmsUp) flags |= 0x02;
		return new PlayerNetState(PeerId, GlobalPosition, velocity,
			Rotation.Y, headAngle, PhysicsSkelton.ArmPointDir,
			(byte)currentMovementState, (byte)currentEmoteState, flags);
	}

	public virtual void ApplyNetworkState(PlayerNetState state)
	{
		GlobalPosition = state.Position;
		velocity = state.Velocity;
		Rotation = new Vector3(Rotation.X, state.BodyYaw, Rotation.Z);
		RotateHead(state.HeadPitch);
		PhysicsSkelton.ArmPointDir = state.ArmPointDir;
		PhysicsSkelton.Aiming = state.Aiming;
		PhysicsSkelton.ArmsUp = state.ArmsUp;
		currentMovementState = state.MoveState;
		currentEmoteState = state.EmoteState;
	}
	public void ComputeEmotePhysics()
	{
		switch (currentEmoteState)
		{
			case EmoteState.None:
				break;
			case EmoteState.Pointing:
				PointAt(pointVec);
				break;
			case EmoteState.ArmsUp:
				break;
			case EmoteState.ShowSign:
				break;
		}
	}

	/// ···········································
	/// : _    ___ ___ ___ _____   _____ _    ___ :
	/// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
	/// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
	/// :|____|___|_| |___\___| |_| \___|____|___|:
	/// ···········································

	public override void _PhysicsProcess(double delta)
	{
		//  CheckIf Out of Bounds, si oui respawn
		if (GlobalPosition.Y < FallLimit)
		{
			GlobalPosition = SpawnPosition;
			velocity = Vector3.Zero;
		}
		if (PhysicsSkelton.IsRagdoll)
		{
			_prevRagdoll = true;
			return; // CharacterBody3D stays put, physical bones simulate freely
		}

		if (_prevRagdoll)
		{
			// Leaving ragdoll: snap character to where the spine landed
			CollisionShape3D spineCollBox =
	GetNode<CollisionShape3D>("PhysicsRig/Armature/Skeleton3D/PhysicalBoneSimulator3D/Physical Bone Spine_001/CollisionShape3D");
			GlobalPosition = spineCollBox.GlobalPosition;
			Rotation = new Vector3(0f, Rotation.Y, 0f);
			_prevRagdoll = false;
		}
		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		Vector3 inputDir = moveVec;
		Vector3 direction = aimVec;
		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * speed;
			velocity.Z = direction.Z * speed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, speed);
		}
		PhysicsSkelton.HeadAngle = -headAngle;
		ComputeEmotePhysics();
		Velocity = velocity;
		MoveAndSlide();
	}
	public override void _Process(double delta)
	{
		MovementState newMovementState = GetMovementStateFromMovement(moveVec, aimVec);
		if (currentMovementState == newMovementState) return;
		currentMovementState = newMovementState;
		PlayAnimationFromMovement(newMovementState, AnimPlayer);

	}
}
