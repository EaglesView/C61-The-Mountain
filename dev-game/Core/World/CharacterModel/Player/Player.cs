using Godot;
using static Utils.RayCastUtils;
using static Utils.CharacterUtils;
using static Utils.CameraUtils;
using Core.Network;

public partial class Player : Character
{
	[ExportGroup("Player Settings")]
	[Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float WalkSpeed = 2.5f;
	[Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float RunMultiplier = 2.0f;
	[Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float JumpVelocity = 4.5f;

	[ExportGroup("Player Physics Settings")]
	[Export(PropertyHint.Range, "0.1f,1000.0f,0.1f")] public float SpeedRagdollThreshold = 10.0f;
	[Export(PropertyHint.Range, "0.0f,360.0f,1.0f,suffix:deg")] public float FloorAngleRagdollThreshold = 60.0f;

	[ExportGroup("Controller Settings")]
	[Export(PropertyHint.Range, "0.0f,0.1f,0.001f")] private float _mouseSensitivity = 0.002f;
	[Export(PropertyHint.Range, "0.0f,5.0f,0.05f")] private float _controllerSensitivity = 2.5f;

	[ExportGroup("Character Nodes")]
	[Export] private CameraMan? _cameraMan;
	[Export] public RayCast3D Raycaster;

	private Vector3 _offsetFP = new Vector3(0, 0.05f, 0.25f);
	private Vector3 _offsetTP = new Vector3(0, 0.1f, -1.25f);
	private Vector3 _currentCamOffset;
	private Interactable? _highlightedInteractable;
	private bool _showDebug = false;
	private bool _playerFocused = false;
	private bool _playerPaused = false;
	private CameraType _lastCamType;

	// ── Remote interpolation (non-authority players) ──────────────────────────
	private const int BufferSize = 4;
	private const float RenderDelay = 0.1f;
	private readonly PlayerNetState[] _snapshots = new PlayerNetState[BufferSize];
	private readonly ulong[] _timestamps = new ulong[BufferSize];
	private int _head = 0;
	private int _count = 0;
	private MovementState _lastAnimatedState = MovementState.Idle;
	private bool _remoteRagdoll = false;

	public void PushSnapshot(PlayerNetState state, ulong timestampMsec)
	{
		_snapshots[_head] = state;
		_timestamps[_head] = timestampMsec;
		_head = (_head + 1) % BufferSize;
		if (_count < BufferSize) _count++;
	}

	// ── FSM callbacks ─────────────────────────────────────────────────────────

	protected override void EnterState(CharacterState state)
	{
		base.EnterState(state);
		if (state == CharacterState.Paused)
		{
			_playerFocused = ToggleCharacterFocus(_playerFocused);
			GameMenu.Instance?.OpenMenu();
		}
		if (state == CharacterState.Ragdoll && _cameraMan != null)
		{
			_lastCamType = _cameraMan.CamType;
			if (_cameraMan.CamType == CameraType.FirstPerson)
				_cameraMan.SetCameraType(CameraType.ThirdPerson);
		}
	}

	protected override void ExitState(CharacterState state)
	{
		base.ExitState(state);
		if (state == CharacterState.Paused)
		{
			_playerFocused = ToggleCharacterFocus(_playerFocused);
			GameMenu.Instance?.CloseMenu();
		}
		if (state == CharacterState.Ragdoll)
		{
			speed = WalkSpeed;
			AnimPlayer.SpeedScale = 1.0f;
		}
		if (state == CharacterState.Recovering && _lastCamType == CameraType.FirstPerson)
		{
			_cameraMan?.SetCameraType(CameraType.FirstPerson);
		}
	}

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		base._Ready();

		if (!IsMultiplayerAuthority())
		{
			// Remote player: disable camera, set up animation
			_cameraMan?.QueueFree();
			_cameraMan = null;
			if (PhysicsSkelton != null && AnimPlayer == null)
				AnimPlayer = PhysicsSkelton.AnimPlayer;
			speed = WalkSpeed;
			if (SpawnPosition != Vector3.Zero)
				GlobalPosition = SpawnPosition;
			return;
		}

		// Local player
		if (SpawnPosition != Vector3.Zero)
			GlobalPosition = SpawnPosition;

		NetworkManager.Instance.SetLocalPlayer(this);
		AddToGroup("local_player");

		if (_cameraMan == null) return;
		_currentCamOffset = _offsetFP;
		if (Raycaster == null) Raycaster = _cameraMan.GetNode<RayCast3D>("PlayerCamera/RayCastTo");
		if (DisplayServer.GetName() != "headless")
			_playerFocused = ToggleCharacterFocus(_playerFocused);
		if (PhysicsSkelton == null) return;
		if (AnimPlayer == null) AnimPlayer = PhysicsSkelton.AnimPlayer;
		speed = WalkSpeed;
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsMultiplayerAuthority()) return;

		if (@event is InputEventMouseMotion mouseMotion)
		{
			if (_cameraMan?.CamType == CameraType.ThirdPerson)
			{
				_cameraMan.RotateCameraTP(
					mouseMotion.Relative.X,
					-mouseMotion.Relative.Y,
					_mouseSensitivity
				);
			}
			else
			{
				RotateY(-mouseMotion.Relative.X * _mouseSensitivity);
			}
			float newAngle = headAngle + mouseMotion.Relative.Y * _mouseSensitivity;
			RotateHead(Mathf.Clamp(newAngle, Mathf.DegToRad(-80), Mathf.DegToRad(80)));
		}

		if (_characterState == CharacterState.Ragdoll)
		{
			if (@event.IsActionPressed("jump"))
				TransitionTo(CharacterState.Recovering);
			return;
		}
		if (_characterState == CharacterState.Paused)
		{
			if (@event.IsActionPressed("pause_menu"))
				TransitionTo(_stateBeforePause);
			return;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsMultiplayerAuthority())
		{
			RemotePhysicsProcess();
			return;
		}

		velocity = Velocity;
		if (_cameraMan == null) return;

		if (_characterState == CharacterState.Ragdoll)
		{
			base._PhysicsProcess(delta);
			return;
		}
		if (_characterState != CharacterState.Ragdoll &&
			_characterState != CharacterState.Recovering &&
			Velocity.Length() > SpeedRagdollThreshold)
		{
			TransitionTo(CharacterState.Ragdoll);
		}

		if (_characterState == CharacterState.Paused)
		{
			moveVec = Vector3.Zero;
			aimVec = Vector3.Zero;
			base._PhysicsProcess(delta);
			return;
		}

		if (IsOnFloor() && GetFloorAngle() > Mathf.DegToRad(FloorAngleRagdollThreshold))
		{
			TransitionTo(CharacterState.Ragdoll);
			return;
		}

		if (Input.IsActionJustPressed("jump") && IsOnFloor()
			&& (_characterState == CharacterState.Idle || _characterState == CharacterState.Moving))
		{
			velocity.Y = JumpVelocity;
			TransitionTo(CharacterState.Airborne);
		}
		if (Input.IsActionJustPressed("run"))
		{
			speed *= RunMultiplier;
			AnimPlayer.SpeedScale *= RunMultiplier;
		}
		if (Input.IsActionJustReleased("run"))
		{
			speed /= RunMultiplier;
			AnimPlayer.SpeedScale /= RunMultiplier;
		}
		if (Input.IsActionJustPressed("pause_menu"))
			TransitionTo(CharacterState.Paused);

		if (Input.IsActionJustPressed("interact"))
		{
			GetObjectTypeFromRaycast(Raycaster);
			PhysicsSkelton.Aiming = false;
			currentEmoteState = EmoteState.None;
			var interactable = GetInteractableFromRaycast(Raycaster);
			interactable?.Interact(this);
		}

		if (Input.IsActionJustPressed("show_sign"))  PhysicsSkelton.ArmsUp = true;
		if (Input.IsActionJustReleased("show_sign")) PhysicsSkelton.ArmsUp = false;

		if (Input.IsActionJustPressed("change_view"))
			_cameraMan?.SetNextCamera([CameraType.FirstPerson, CameraType.ThirdPerson]);

		Vector2 aimDir = Input.GetVector("aim_left", "aim_right", "aim_up", "aim_down");
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		Vector3 direction;

		if (_cameraMan?.CamType == CameraType.ThirdPerson)
		{
			Vector3 camForward = _cameraMan.GetCameraForwardFlat();
			Vector3 camRight = _cameraMan.GetCameraRightFlat();
			direction = (camForward * -inputDir.Y + camRight * -inputDir.X).Normalized();
			if (direction != Vector3.Zero)
				Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(-direction.X, -direction.Z), 0.15f), Rotation.Z);
		}
		else
		{
			direction = -(Transform.Basis * new Vector3(-inputDir.X, 0, -inputDir.Y)).Normalized();
		}

		moveVec = direction;
		aimVec = direction;
		base._PhysicsProcess(delta);
		if (currentEmoteState == EmoteState.Pointing)
			pointVec = _cameraMan.GetRaycastPointingVector();
	}

	public override void _Process(double delta)
	{
		if (!IsMultiplayerAuthority())
		{
			if (_lastAnimatedState == currentMovementState) return;
			_lastAnimatedState = currentMovementState;
			PlayAnimationFromMovement(currentMovementState, AnimPlayer);
			return;
		}

		base._Process(delta);
		int boneIdx = PhysicsSkelton.FindBone("Head.001");
		Transform3D headWorld = PhysicsSkelton.GlobalTransform * PhysicsSkelton.GetBoneGlobalPose(boneIdx);

		var interactable = GetInteractableFromRaycast(Raycaster);
		if (interactable != _highlightedInteractable)
		{
			_highlightedInteractable?.OnUnhighlight();
			_highlightedInteractable = interactable;
			_highlightedInteractable?.OnHighlight();
		}
	}

	// ── Remote interpolation ──────────────────────────────────────────────────

	private void RemotePhysicsProcess()
	{
		if (_count < 1) return;

		int latestIdx = (_head - 1 + BufferSize) % BufferSize;
		bool ragdolling = _snapshots[latestIdx].MoveState == MovementState.Ragdolling;
		bool recovering = _snapshots[latestIdx].Recovering;
		bool shouldRagdoll = ragdolling && !recovering;

		if (shouldRagdoll != _remoteRagdoll)
		{
			_remoteRagdoll = shouldRagdoll;
			PhysicsSkelton.IsRagdoll      = shouldRagdoll;
			PhysicsSkelton.RemoteCorrection = shouldRagdoll;
			if (_capsule != null) _capsule.Disabled = shouldRagdoll;

			if (shouldRagdoll)
				PhysicsSkelton.ApplyRagdollKick(_snapshots[latestIdx].Velocity);
		}

		if (_remoteRagdoll)
		{
			PhysicsSkelton.RemoteSpineTarget = _snapshots[latestIdx].Position;
			PhysicsSkelton.RemoteHeadPitch   = _snapshots[latestIdx].HeadPitch;
			PhysicsSkelton.RemoteHeadYaw     = _snapshots[latestIdx].BodyYaw;
			return;
		}

		ulong nowMsec = Time.GetTicksMsec();
		ulong renderTime = nowMsec - (ulong)(RenderDelay * 1000f);

		int oldest = (_head - _count + BufferSize) % BufferSize;
		int aIdx = -1, bIdx = -1;

		for (int i = 0; i < _count - 1; i++)
		{
			int ia = (oldest + i) % BufferSize;
			int ib = (oldest + i + 1) % BufferSize;
			if (_timestamps[ia] <= renderTime && _timestamps[ib] >= renderTime)
			{
				aIdx = ia;
				bIdx = ib;
				break;
			}
		}

		if (aIdx < 0)
		{
			int latest = (_head - 1 + BufferSize) % BufferSize;
			ApplyNetworkState(_snapshots[latest]);
			return;
		}

		ulong tA = _timestamps[aIdx];
		ulong tB = _timestamps[bIdx];
		float t = (tB == tA) ? 0f : (float)(renderTime - tA) / (float)(tB - tA);
		t = Mathf.Clamp(t, 0f, 1f);

		PlayerNetState a = _snapshots[aIdx];
		PlayerNetState b = _snapshots[bIdx];

		GlobalPosition = a.Position.Lerp(b.Position, t);
		velocity = a.Velocity.Lerp(b.Velocity, t);
		Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(a.BodyYaw, b.BodyYaw, t), Rotation.Z);
		RotateHead(Mathf.LerpAngle(a.HeadPitch, b.HeadPitch, t));
		PhysicsSkelton.HeadAngle = -headAngle;
		PhysicsSkelton.ArmPointDir = a.ArmPointDir.Lerp(b.ArmPointDir, t);

		PlayerNetState snap = t >= 0.5f ? b : a;
		PhysicsSkelton.Aiming = snap.Aiming;
		PhysicsSkelton.ArmsUp = snap.ArmsUp;
		currentMovementState = snap.MoveState;
		currentEmoteState = snap.EmoteState;
	}
}
