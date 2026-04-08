/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:               Character.cs                        |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Classe de base du personnage — FSM + physique    |
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
	[Export] protected CollisionShape3D? _capsule;

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
	protected CharacterState _characterState = CharacterState.Idle;
	protected CharacterState _stateBeforePause = CharacterState.Idle;
	protected float _graceTime = 0f;
	private CollisionShape3D? _spineCollBox;
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
	public CharacterState GetCurrentState() => _characterState;

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

	// ── FSM ──────────────────────────────────────────────────────────────────

	public void TransitionTo(CharacterState next)
	{
		if (_characterState == next) return;
		if (next == CharacterState.Paused)
			_stateBeforePause = _characterState;
		ExitState(_characterState);
		_characterState = next;
		EnterState(_characterState);
	}

	protected virtual void EnterState(CharacterState state)
	{
		switch (state)
		{
			case CharacterState.Ragdoll:
				if (_capsule != null) _capsule.Disabled = true;
				PhysicsSkelton.IsRagdoll = true;
				PhysicsSkelton.RagdollTriggered = false;
				break;
			case CharacterState.Recovering:
				if (_spineCollBox != null)
					GlobalPosition = _spineCollBox.GlobalPosition;
				Rotation = new Vector3(0f, Rotation.Y, 0f);
				_graceTime = PhysicsSkelton.RagdollGraceTime;
				velocity = Vector3.Zero;
				Velocity = Vector3.Zero;
				break;
		}
	}

	protected virtual void ExitState(CharacterState state)
	{
		switch (state)
		{
			case CharacterState.Ragdoll:
				PhysicsSkelton.IsRagdoll = false;
				if (_capsule != null) _capsule.Disabled = false;
				break;
		}
	}

	/// ···········································
	/// : _    ___ ___ ___ _____   _____ _    ___ :
	/// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
	/// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
	/// :|____|___|_| |___\___| |_| \___|____|___|:
	/// ···········································

	public override void _Ready()
	{
		_spineCollBox = GetNodeOrNull<CollisionShape3D>(
			"PhysicsRig/Armature/Skeleton3D/PhysicalBoneSimulator3D/Physical Bone Spine_001/CollisionShape3D");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (GlobalPosition.Y < FallLimit)
		{
			GlobalPosition = SpawnPosition;
			velocity = Vector3.Zero;
			TransitionTo(CharacterState.Idle);
		}

		if (PhysicsSkelton.RagdollTriggered && _characterState != CharacterState.Ragdoll)
			TransitionTo(CharacterState.Ragdoll);

		switch (_characterState)
		{
			case CharacterState.Ragdoll:
				return;

			case CharacterState.Recovering:
				_graceTime -= (float)delta;
				_PhysicsGrounded(delta);
				_CheckRecoveringTransitions();
				break;

			case CharacterState.Paused:
				if (!IsOnFloor()) velocity += GetGravity() * (float)delta;
				Velocity = velocity;
				MoveAndSlide();
				break;

			case CharacterState.Airborne:
				_PhysicsAirborne(delta);
				_CheckAirborneTransitions();
				break;

			default: // Idle, Moving
				_PhysicsGrounded(delta);
				_CheckGroundedTransitions();
				break;
		}
	}

	private void _PhysicsGrounded(double delta)
	{
		if (!IsOnFloor()) velocity += GetGravity() * (float)delta;
		if (aimVec != Vector3.Zero)
		{
			velocity.X = aimVec.X * speed;
			velocity.Z = aimVec.Z * speed;
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

	private void _PhysicsAirborne(double delta)
	{
		velocity += GetGravity() * (float)delta;
		if (aimVec != Vector3.Zero)
		{
			velocity.X = aimVec.X * speed;
			velocity.Z = aimVec.Z * speed;
		}
		PhysicsSkelton.HeadAngle = -headAngle;
		ComputeEmotePhysics();
		Velocity = velocity;
		MoveAndSlide();
	}

	private void _CheckGroundedTransitions()
	{
		if (!IsOnFloor())
			TransitionTo(CharacterState.Airborne);
		else if (moveVec != Vector3.Zero)
			TransitionTo(CharacterState.Moving);
		else
			TransitionTo(CharacterState.Idle);
	}

	private void _CheckAirborneTransitions()
	{
		if (IsOnFloor())
			TransitionTo(moveVec != Vector3.Zero ? CharacterState.Moving : CharacterState.Idle);
	}

	private void _CheckRecoveringTransitions()
	{
		if (_graceTime <= 0f && IsOnFloor())
			TransitionTo(moveVec != Vector3.Zero ? CharacterState.Moving : CharacterState.Idle);
	}

	private MovementState _CharacterStateToMovementState()
	{
		return _characterState switch
		{
			CharacterState.Moving => MovementState.Walking,
			CharacterState.Ragdoll or CharacterState.Recovering => MovementState.Ragdolling,
			_ => MovementState.Idle
		};
	}

	public override void _Process(double delta)
	{
		MovementState newMovementState = _CharacterStateToMovementState();
		if (currentMovementState == newMovementState) return;
		currentMovementState = newMovementState;
		PlayAnimationFromMovement(newMovementState, AnimPlayer);
	}
}
