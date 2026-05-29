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
using Core.Stats;
using Core.World;
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

    /// <summary>
    /// Marque cette instance comme rendu UI (SubViewport Lobby/Profile/Winning).
    /// Doit être assigné <b>avant</b> AddChild() pour que <see cref="_Ready"/> y
	/// réagisse&#160;: skip l'inscription au groupe <c>players_alive</c> (sinon le
	/// spectateur cible des pengouins de preview) et bloque la FSM en
	/// <see cref="CharacterState.Preview"/> dès le départ. L'anim Idle est jouée
    /// une fois manuellement&#160;; <see cref="PhysicsSkeleton"/> continue de
    /// tourner pour le jiggle des springs.
    /// </summary>
    [Export] public bool IsPreview = false;

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

    /// <summary>
    /// Durée (en secondes) pendant laquelle ce personnage est immunisé contre
    /// les morts «&#160;environnementales&#160;» (Water&#160;: noyade) après son spawn. Couvre
	/// la fenêtre où les <c>PhysicalBone3D</c> du squelette n'ont pas encore
	/// rattrapé la transform du <c>CharacterBody3D</c>&#160;: leurs positions
	/// transitoires au repos peuvent déclencher des <c>body_entered</c> sur
	/// l'<c>Area3D</c> de l'eau alors que le corps lui-même est sur la
	/// plateforme. Sert aussi à laisser le filet de sécurité aux téléportations
	/// (respawn) déclenchées par le serveur.
	/// </summary>
	[Export(PropertyHint.Range, "0.0f,5.0f,0.05f,suffix:sec")] public float SpawnProtectionDuration = 0.75f;

	private ulong _spawnProtectionUntilMsec = 0UL;

	/// <summary>
	/// Vrai tant que le personnage est encore dans sa fenêtre de protection
	/// post-spawn. Consommé par <see cref="Water"/> pour ignorer les morts par
	/// noyade transitoires.
	/// </summary>
	public bool IsSpawnProtected => Time.GetTicksMsec() < _spawnProtectionUntilMsec;

	/// <summary>
	/// (Re)déclenche la protection post-spawn. Appelé par <see cref="_Ready"/>
	/// et utilisable par les systèmes de respawn pour réarmer la fenêtre après
	/// une téléportation forcée.
	/// </summary>
	public void ArmSpawnProtection()
	{
		float d = MathF.Max(0f, SpawnProtectionDuration);
		_spawnProtectionUntilMsec = Time.GetTicksMsec() + (ulong)(d * 1000f);
	}


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

	// ── Stats (authority only) ───────────────────────────────────────────────

	/// <summary>
	/// Compteurs de la partie en cours. Rempli sur l'instance authority uniquement
    /// (les répliques distantes ne tracent rien&#160;: le serveur agrège la version
    /// authoritative via le RPC <c>SubmitStats</c> en fin de phase Game).
    /// </summary>
    protected PlayerGameStats _stats = new();
    protected float _ragdollEnterSec;

    /// <summary>Accès lecture aux stats accumulées (authority).</summary>
    public PlayerGameStats Stats => _stats;

    // ── Death ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dernière raison de mort enregistrée. Réinitialisée par <see cref="Die"/> à chaque
    /// déclenchement. Utilisée par les consommateurs de stats pour catégoriser la mort.
    /// </summary>
    public DeathReason LastDeathReason { get; private set; } = DeathReason.Unknown;

    /// <summary>
    /// Émis lorsque ce personnage entre en état <see cref="CharacterState.Dead"/>.
	/// Fire-once par mort&#160;: l'événement n'est pas réémis si <see cref="Die"/> est rappelé
	/// alors qu'on est déjà mort (idempotent). Signature&#160;: <c>(peerId, reason)</c>.
	/// Hook prévu pour le futur service de stats.
	/// </summary>
	public event System.Action<int, DeathReason> Died;

	/// <summary>
	/// Bascule le personnage en mort. Application locale uniquement&#160;: la propagation réseau
	/// (RPC) est faite par <see cref="Player.RequestDeath"/> côté authority/serveur.
	/// Idempotent&#160;: ne fait rien si déjà mort.
	/// </summary>
	/// <param name="InReason">Cause de la mort, transmise au futur système de stats.</param>
	public virtual void Die(DeathReason InReason)
	{
		if (_characterState == CharacterState.Dead) return;
		// Diagnostic&#160;: imprime la cause + une stack trace managée pour identifier
		// quel call site a déclenché la mort. À retirer une fois la cause des
		// morts spurieuses comprise.
		GD.Print($"[Character.Die] peer={PeerId} reason={InReason} authority={IsMultiplayerAuthority()} pos={GlobalPosition}\n{System.Environment.StackTrace}");
		LastDeathReason = InReason;
		if (IsMultiplayerAuthority())
		{
			_stats.PeerId = PeerId;
			_stats.TimeOfDeathSeconds = GameController.GameElapsedSeconds;
			_stats.DeathReason = InReason;
		}
		TransitionTo(CharacterState.Dead);
		Died?.Invoke(PeerId, InReason);
	}

	/// <summary>
	/// Enregistre l'instant où ce personnage a franchi la ligne d'arrivée du
	/// mode Obby/Racing. Authority-only (les stats sont remplies sur le côté
	/// authoritative puis transmises via <c>SubmitStats</c>). Idempotent&#160;: ne
	/// re-écrit pas si une finition a déjà été enregistrée.
	/// </summary>
	public void RecordFinish(float InGameElapsedSeconds)
	{
		if (!IsMultiplayerAuthority()) return;
		if (_stats.TimeOfFinishSeconds >= 0f) return;
		_stats.PeerId = PeerId;
		_stats.TimeOfFinishSeconds = InGameElapsedSeconds;
	}

	public virtual PlayerNetState SnapshotState()
	{
		byte flags = 0;
		if (PhysicsSkelton.Aiming) flags |= 0x01;
		if (PhysicsSkelton.ArmsUp) flags |= 0x02;

		if (_characterState == CharacterState.Ragdoll || _characterState == CharacterState.Dead)
		{
			// Position and Velocity are repurposed: spine physics world position
			// and velocity so remote players can anchor their local simulation.
			// BodyYaw and HeadPitch carry the head physical bone's world rotation
            // so the remote correction steers head orientation correctly.
            var (headPitch, headYaw) = PhysicsSkelton.GetHeadPhysicsWorldAngles();
            return new PlayerNetState(PeerId,
                PhysicsSkelton.GetSpinePhysicsWorldPosition(),
                PhysicsSkelton.GetSpinePhysicsLinearVelocity(),
                headYaw, headPitch,
                PhysicsSkelton.ArmPointDir,
                (byte)currentMovementState, (byte)currentEmoteState, flags);
        }

        if (_characterState == CharacterState.Recovering)
            flags |= 0x04;

        return new PlayerNetState(PeerId, GlobalPosition, Velocity,
            Rotation.Y, headAngle, PhysicsSkelton.ArmPointDir,
            (byte)currentMovementState, (byte)currentEmoteState, flags);
    }

    public virtual void ApplyNetworkState(PlayerNetState state)
    {
        GlobalPosition = state.Position;
        velocity = state.Velocity;
        Rotation = new Vector3(Rotation.X, state.BodyYaw, Rotation.Z);
        RotateHead(state.HeadPitch);
        PhysicsSkelton.HeadAngle = -headAngle;
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
                if (IsMultiplayerAuthority())
                {
                    _stats.PeerId = PeerId;
                    _stats.RagdollCount++;
                    _ragdollEnterSec = Time.GetTicksMsec() / 1000f;
                }
                break;
            case CharacterState.Recovering:
                if (_spineCollBox != null)
                    GlobalPosition = _spineCollBox.GlobalPosition;
                Rotation = new Vector3(0f, Rotation.Y, 0f);
                _graceTime = PhysicsSkelton.RagdollGraceTime;
                velocity = Vector3.Zero;
                Velocity = Vector3.Zero;
                PhysicsSkelton.RagdollTriggered = false;
                break;
            case CharacterState.Dead:

                if (_capsule != null) _capsule.Disabled = true;
                PhysicsSkelton.IsRagdoll = true;
                PhysicsSkelton.RagdollTriggered = false;
                velocity = Vector3.Zero;
                Velocity = Vector3.Zero;
                moveVec = Vector3.Zero;
                aimVec = Vector3.Zero;
                if (IsInGroup("players_alive")) RemoveFromGroup("players_alive");
                break;
            case CharacterState.Spectating:
				// Le corps doit RESTER ragdoll pendant le spectateur (l'utilisateur a
				// dit «&#160;keep dead body visible&#160;»). On reproduit les invariants de
				// Dead côté corps physique parce qu'ExitState(Dead) un-ragdoll le
                // squelette et réactive la capsule — sans cette ré-application, le
				// personnage se relèverait visuellement au moment d'entrer en spectateur.
				if (_capsule != null) _capsule.Disabled = true;
				PhysicsSkelton.IsRagdoll = true;
				PhysicsSkelton.RagdollTriggered = false;
				velocity = Vector3.Zero;
				Velocity = Vector3.Zero;
				moveVec = Vector3.Zero;
				aimVec = Vector3.Zero;
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
				if (IsMultiplayerAuthority())
				{
					float now = Time.GetTicksMsec() / 1000f;
					_stats.TotalRagdollSeconds += now - _ragdollEnterSec;
				}
				break;
			case CharacterState.Dead:
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

		if (IsPreview)
		{
			// FSM bloquée dès le _Ready: _PhysicsProcess early-return sur Preview,
			// _Process mappe vers MovementState.Idle via le default switch.
			// AnimPlayer reste muet sans ce Play() (currentMovementState=Idle déjà,
			// donc la garde "if equal return" de _Process ne déclenche jamais Play).
			_characterState = CharacterState.Preview;
			PlayAnimationFromMovement(MovementState.Idle, AnimPlayer);
			return;
		}

		// Groupe de référence pour la sélection de cibles spectateur. Maintenu
		// par EnterState(Dead) qui retire l'entrée à la mort. Joindre ici (pas
        // dans Player._Ready) garantit que toutes les répliques distantes sont
        // énumérables — le joueur local doit pouvoir cibler les autres peers.
        AddToGroup("players_alive");

		// Fenêtre d'immunité post-spawn : couvre la frame où les PhysicalBone3D
		// du squelette n'ont pas encore rattrapé la transform du CharacterBody3D.
        ArmSpawnProtection();
    }

    public override void _PhysicsProcess(double delta)
    {
        // Instance UI: pas de MoveAndSlide, pas de FSM, pas de respawn. Le pitch
		// de tête est piloté de l'extérieur via Preview.cs qui écrit directement
		// PhysicsSkelton.HeadAngle; le yaw via Rotation sur ce node.
		if (_characterState == CharacterState.Preview) return;

		// Respawn local — uniquement quand on n'est pas en réseau. En multi le
        // serveur fait autorité (cf. NetworkManager.OnPacketReceived /
        // FallThreshold) et envoie la correction de position ; téléporter ici
        // en plus produirait un fight client/serveur sur la position.
        if (GlobalPosition.Y < FallLimit && (NetworkManager.Instance is null || !NetworkManager.Instance.IsRunning))
        {
            GlobalPosition = SpawnPosition;
            velocity = Vector3.Zero;
            TransitionTo(CharacterState.Idle);
        }


        if (PhysicsSkelton.RagdollTriggered
            && _characterState != CharacterState.Ragdoll
            && _characterState != CharacterState.Recovering)
            TransitionTo(CharacterState.Ragdoll);

        switch (_characterState)
        {
            case CharacterState.Ragdoll:
                return;

            case CharacterState.Dead:
                return;

            case CharacterState.Spectating:
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
            CharacterState.Ragdoll or CharacterState.Recovering or CharacterState.Dead or CharacterState.Spectating => MovementState.Ragdolling,
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

    public override void _Notification(int what)
    {
        if (what == Node.NotificationExitTree)
        {
            //GD.Print($"[Character] EXIT TREE: {Name}");
        }
        else if (what == Node.NotificationEnterTree)
        {
            //GD.Print($"[Character] ENTER TREE: {Name}");
        }
        base._Notification(what);
    }
    public override void _ExitTree()
    {
        base._ExitTree();
    }
}
