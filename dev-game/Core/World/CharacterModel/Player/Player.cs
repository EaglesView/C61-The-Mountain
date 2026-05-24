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
	[Export(PropertyHint.Range, "0.0f,50.0f,0.1f")] public float RecoveryVelocityThreshold = 3.0f;

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
	private Label3D? _nameLabel;

	/// <summary>
	/// Identifiant du chapeau cosmétique à instancier sur la tête du
	/// personnage. Assigné par <c>GameController.SpawnPlayerNode</c> à partir
	/// du <c>RoomSnapshot</c> (ou <c>LobbyState.SelectedHatId</c> en offline).
	/// Voir <see cref="HatRegistry"/> pour le catalogue.
	/// </summary>
	public string HatId { get; set; } = HatRegistry.DefaultHatId;
	private Node3D? _hatInstance;
	private CameraType _lastCamTypeForHat = CameraType.ThirdPerson;

	/// <summary>
	/// Verrou d'entrée pour le joueur local. Activé par le GameController pendant
	/// le countdown d'avant-partie, quand la partie est avortée (host parti), ou
	/// par toute autre logique qui veut figer l'input sans ouvrir le menu pause.
	/// Quand <c>true</c>&#160;: <see cref="_Input"/> retourne immédiatement et
	/// <see cref="_PhysicsProcess"/> ne lit plus les commandes (moveVec/aimVec
	/// forcés à zéro). Ne touche pas au mode souris — c'est à l'appelant
	/// d'arbitrer si le curseur doit être visible (overlay) ou capturé (countdown).
	/// </summary>
	public bool InputFrozen
	{
		get => _inputFrozen;
		set => _inputFrozen = value;
	}
	private bool _inputFrozen;

	// ── Spectator (local authority only) ──────────────────────────────────────

	/// <summary>
	/// Liste circulaire de cibles construite à chaque appui sur N/P&#160;:
	/// [ancre du niveau, puis tous les Characters du groupe "players_alive"
	/// triés par PeerId, en excluant soi-même]. L'ancre est un Marker3D
	/// optionnel du groupe "spectator_anchor"; si absente, on tombe sur
	/// <c>null</c> qui fait orbiter la caméra autour de Vector3.Zero (cf.
	/// <see cref="CameraMan.SetSpectatorPivot"/>).
	/// </summary>
	private Node3D? _spectatorCurrentTarget;
	private Character? _spectatorTrackedCharacter;

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
		if (state == CharacterState.Spectating && IsMultiplayerAuthority())
		{
			// La caméra appartient au joueur local&#160;: pas de spectateur pour les
			// répliques distantes. EnterSpectator() force TP, le pivot initial
			// est l'ancre du niveau (Marker3D du groupe "spectator_anchor")
			// ou null (qui fait orbiter autour de Vector3.Zero).
			_cameraMan?.EnterSpectator();
			var anchor = GetTree().GetFirstNodeInGroup("spectator_anchor") as Node3D;
			_SetSpectatorTarget(anchor);
			// Souris capturée pour piloter l'orbit-cam (sinon le curseur se
			// promène et la souris ne tourne pas la caméra).
			Input.MouseMode = Input.MouseModeEnum.Captured;
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
		// Revert to FirstPerson disabled
		if (state == CharacterState.Spectating && IsMultiplayerAuthority())
		{
			if (_spectatorTrackedCharacter is not null)
			{
				_spectatorTrackedCharacter.Died -= _OnSpectatorTargetDied;
				_spectatorTrackedCharacter = null;
			}
			_spectatorCurrentTarget = null;
			_cameraMan?.ExitSpectator();
		}
	}


	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		base._Ready();

		GD.Print($"[LoadDiag] Player._Ready name={Name} peerId={PeerId} " +
			$"authority={IsMultiplayerAuthority()} " +
			$"localPeerId={Core.Network.NetworkManager.Instance?.LocalPeerId ?? -1} " +
			$"spawnPos={SpawnPosition} parent={GetParent()?.Name}");

		if (!IsMultiplayerAuthority())
		{
			// Remote player: disable camera, set up animation, show name billboard
			_cameraMan?.QueueFree();
			_cameraMan = null;
			if (PhysicsSkelton != null && AnimPlayer == null)
				AnimPlayer = PhysicsSkelton.AnimPlayer;
			speed = WalkSpeed;
			if (SpawnPosition != Vector3.Zero)
				GlobalPosition = SpawnPosition;

			_nameLabel = new Label3D();
			_nameLabel.Text = $"P{PeerId}";
			_nameLabel.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
			_nameLabel.PixelSize = 0.005f;
			_nameLabel.FontSize = 48;
			_nameLabel.OutlineSize = 8;
			_nameLabel.Position = new Vector3(0f, 2.2f, 0f);
			_nameLabel.NoDepthTest = true;
			AddChild(_nameLabel);
			_SpawnHat();
			return;
		}

		// Local player
		if (SpawnPosition != Vector3.Zero)
			GlobalPosition = SpawnPosition;

		NetworkManager.Instance.SetLocalPlayer(this);
		AddToGroup("local_player");

		if (_cameraMan == null) { _SpawnHat(); return; }
		_currentCamOffset = _offsetTP;
		if (Raycaster == null) Raycaster = _cameraMan.GetNode<RayCast3D>("PlayerCamera/RayCastTo");
		if (DisplayServer.GetName() != "headless")
			_playerFocused = ToggleCharacterFocus(_playerFocused);
		if (PhysicsSkelton == null) { _SpawnHat(); return; }
		if (AnimPlayer == null) AnimPlayer = PhysicsSkelton.AnimPlayer;
		speed = WalkSpeed;
		_SpawnHat();
	}

	/// <summary>
	/// Instancie la scène du chapeau (si <see cref="HatId"/> en désigne un)
	/// sous un <c>BoneAttachment3D</c> créé à la volée et parenté au squelette
	/// <b>physique</b> (<see cref="Character.PhysicsSkelton"/>), suiveur du bone
	/// <c>Head.001</c>. On vise volontairement le rig physique et non l'animé:
	/// le rig animé est invisible (<c>AnimatedRig.visible = false</c>) et sert
	/// uniquement de cible cinématique aux ressorts de
	/// <see cref="PhysicsSkeleton._PhysicsProcess"/>; le mesh rendu est porté
	/// par le rig physique, dont les poses d'os sont réécrites chaque tick par
	/// le <c>PhysicalBoneSimulator3D</c>. Conséquence: en jeu normal le
	/// chapeau colle au mesh (les ressorts rapprochent les os physiques de la
	/// cible animée), et en ragdoll il suit la tête physique au lieu de rester
	/// figé sur la pose d'animation. Les deux squelettes proviennent du même
	/// <c>.glb</c>, le rest pose de <c>Head.001</c> est donc identique: aucun
	/// décalage local n'est à retoucher. Le placement (offset / rotation /
	/// scale) reste défini dans la scène du chapeau, sa racine <c>Node3D</c>
	/// servant d'ancre.
	/// </summary>
	private void _SpawnHat()
	{
		if (_hatInstance != null) return;
		var hatDef = HatRegistry.Get(HatId) ?? HatRegistry.Get(HatRegistry.DefaultHatId);
		if (hatDef == null || string.IsNullOrEmpty(hatDef.ScenePath)) return;

		if (PhysicsSkelton == null)
		{
			GD.PrintErr($"[Player] PhysicsSkelton introuvable — chapeau ignoré (peer={PeerId}).");
			return;
		}

		var hatScene = ResourceLoader.Load<PackedScene>(hatDef.ScenePath);
		if (hatScene == null)
		{
			GD.PrintErr($"[Player] Scène chapeau introuvable&#160;: {hatDef.ScenePath}");
			return;
		}

		var attachment = new BoneAttachment3D();
		attachment.Name = "HatAttachment";
		attachment.BoneName = "Head.001";
		PhysicsSkelton.AddChild(attachment);

		var hatNode = hatScene.Instantiate<Node3D>();
		attachment.AddChild(hatNode);
		_hatInstance = hatNode;
	}

	/// <summary>
	/// Demande la mort du joueur avec routing réseau. Application:
	/// <list type="bullet">
	/// <item>Offline: applique <see cref="Character.Die"/> localement.</item>
	/// <item>Client (authority de ce Player): applique localement (prédictif) puis notifie
	///   le serveur via <see cref="ServerKillMe"/>.</item>
	/// <item>Serveur: applique localement puis broadcast <see cref="NetApplyDeath"/> à tous
	///   les clients (utile pour les morts déclenchées par le mode de jeu: noyade, écrasement…).</item>
	/// </list>
	/// Toute autre tentative (client non-authority qui essaie de tuer un autre joueur)
	/// est ignorée silencieusement.
	/// </summary>
	/// <param name="InReason">Cause de la mort, transmise au futur système de stats.</param>
	public void RequestDeath(DeathReason InReason)
	{
		var net = NetworkManager.Instance;
		if (net is null || !net.IsRunning)
		{
			Die(InReason);
			return;
		}
		if (Multiplayer.IsServer())
		{
			Die(InReason);
			Rpc(MethodName.NetApplyDeath, (byte)InReason);
			return;
		}
		if (IsMultiplayerAuthority())
		{
			Die(InReason);
			RpcId(1, MethodName.ServerKillMe, (byte)InReason);
		}
	}

	/// <summary>
	/// RPC client → serveur: «je suis mort, broadcast aux autres». Le serveur vérifie
	/// que l'émetteur possède bien ce Player (sender == <see cref="Character.PeerId"/>) pour
	/// éviter qu'un peer ne tue le personnage d'un autre.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ServerKillMe(byte InReason)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId != PeerId) return;
		Die((DeathReason)InReason);
		// On ré-émet aux AUTRES peers seulement: l'émetteur a déjà appliqué
		// sa mort localement (prédiction client), et lui renvoyer la RPC pose
		// une race lors de la transition Game→Winning: si son Player a déjà
		// été QueueFree (mapContainer libéré), l'écho échoue à trouver le
		// nœud cible et pollue les logs avec un «Node not found» inoffensif
		// mais bruyant. On itère donc explicitement les peers actifs en sautant
		// le sender.
		foreach (int peerId in Multiplayer.GetPeers())
		{
			if (peerId == senderId) continue;
			RpcId(peerId, MethodName.NetApplyDeath, InReason);
		}
	}

	/// <summary>
	/// RPC serveur → clients: applique la mort sur les répliques distantes. Filtre
	/// pour ne faire confiance qu'au peer ID 1 (serveur).
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NetApplyDeath(byte InReason)
	{
		GD.Print($"[Player.NetApplyDeath] peer={PeerId} fromSender={Multiplayer.GetRemoteSenderId()} reason={(DeathReason)InReason}");
		if (Multiplayer.GetRemoteSenderId() != 1) return;
		Die((DeathReason)InReason);
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsMultiplayerAuthority()) return;

		// Handle camera rotation even if frozen (ThirdPerson only)
		if (@event is InputEventMouseMotion mm && _cameraMan?.CamType == CameraType.ThirdPerson)
		{
			_cameraMan.RotateCameraTP(mm.Relative.X, -mm.Relative.Y, _mouseSensitivity);
			
			// If frozen, we stop here to avoid rotating the head/body
			if (_inputFrozen) return;
		}

		if (_inputFrozen) return;

		if (_characterState == CharacterState.Spectating)
		{
			// N/P et molette : cycler la cible.
			if (@event.IsActionPressed("spectate_next")) _CycleSpectatorTarget(+1);
			else if (@event.IsActionPressed("spectate_prev")) _CycleSpectatorTarget(-1);
			else if (@event.IsActionPressed("pause_menu")) TransitionTo(CharacterState.Paused);
			return;
		}

		if (_characterState == CharacterState.Dead) return;

		if (@event is InputEventMouseMotion mouseMotion)
		{
			// Note: RotateCameraTP is already called above if in ThirdPerson.
			// We only need to handle RotateHead and FirstPerson rotation here.
			if (_cameraMan?.CamType != CameraType.ThirdPerson)
			{
				RotateY(-mouseMotion.Relative.X * _mouseSensitivity);
			}
			
			float newAngle = headAngle + mouseMotion.Relative.Y * _mouseSensitivity;
			RotateHead(Mathf.Clamp(newAngle, Mathf.DegToRad(-80), Mathf.DegToRad(80)));
		}

		if (_characterState == CharacterState.Ragdoll)
		{
			if (@event.IsActionPressed("jump") &&
				PhysicsSkelton.GetSpinePhysicsLinearVelocity().Length() < RecoveryVelocityThreshold)
				TransitionTo(CharacterState.Recovering);
			return;
		}
		if (_characterState == CharacterState.Paused)
		{
			if (@event.IsActionPressed("pause_menu"))
				TransitionTo(_stateBeforePause);
			return;
		}
		if (@event.IsActionPressed("pause_menu"))
		{
			TransitionTo(CharacterState.Paused);
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

		if (_inputFrozen)
		{
			moveVec = Vector3.Zero;
			aimVec = Vector3.Zero;
			base._PhysicsProcess(delta);
			return;
		}

		if (_characterState == CharacterState.Spectating)
		{
			// Aucun input local — base._PhysicsProcess short-circuit sur Spectating.
			// On délègue uniquement pour que le hot-path FallLimit reste évalué
			// si jamais (mais le corps est ragdoll et FallLimit ne change pas
			// l'état Spectating de toute façon).
            moveVec = Vector3.Zero;
            aimVec = Vector3.Zero;
            base._PhysicsProcess(delta);
            return;
        }

        if (_characterState == CharacterState.Dead)
        {
            // Mort permanente: le corps est livré au ragdoll, on délègue à Character
            // (qui short-circuit le switch et laisse PhysicsSkeleton piloter la simulation).
            base._PhysicsProcess(delta);
            return;
        }

        if (_characterState == CharacterState.Ragdoll)
        {
            base._PhysicsProcess(delta);
            return;
        }
        if (_characterState != CharacterState.Ragdoll &&
            _characterState != CharacterState.Recovering &&
            Velocity.Length() > SpeedRagdollThreshold)
        {
            //GD.Print($"[speed-ragdoll] V={Velocity.Length():F2}  Y={GlobalPosition.Y:F2}");
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
            _stats.PeerId = PeerId;
            _stats.JumpCount++;
            TransitionTo(CharacterState.Airborne);
        }
        if (Input.IsActionJustPressed("run"))
        {
            speed *= RunMultiplier;
            AnimPlayer.SpeedScale *= RunMultiplier;
        }
        if (Input.IsActionJustReleased("run"))
        {
            speed = WalkSpeed;
            AnimPlayer.SpeedScale = 1.0f;
        }
        if (Input.IsActionJustPressed("interact"))
        {
            GetObjectTypeFromRaycast(Raycaster);
            PhysicsSkelton.Aiming = false;
            currentEmoteState = EmoteState.None;
            var interactable = GetInteractableFromRaycast(Raycaster);
            interactable?.Interact(this);
        }

        if (Input.IsActionJustPressed("show_sign")) PhysicsSkelton.ArmsUp = true;
        if (Input.IsActionJustReleased("show_sign")) PhysicsSkelton.ArmsUp = false;

        // Camera switching disabled

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
            if (_nameLabel != null)
            {
                _nameLabel.GlobalPosition = currentMovementState == MovementState.Ragdolling
                    ? PhysicsSkelton.GetSpinePhysicsWorldPosition() + Vector3.Up * 1.5f
                    : GlobalPosition + new Vector3(0f, 2.2f, 0f);
            }
            if (_lastAnimatedState == currentMovementState) return;
            _lastAnimatedState = currentMovementState;
            PlayAnimationFromMovement(currentMovementState, AnimPlayer);
            return;
        }

        base._Process(delta);
        int boneIdx = PhysicsSkelton.FindBone("Head.001");
        Transform3D headWorld = PhysicsSkelton.GlobalTransform * PhysicsSkelton.GetBoneGlobalPose(boneIdx);

        if (_hatInstance != null && _cameraMan != null && _cameraMan.CamType != _lastCamTypeForHat)
        {
            _lastCamTypeForHat = _cameraMan.CamType;
            _hatInstance.Visible = _cameraMan.CamType != CameraType.FirstPerson;
        }

        var interactable = GetInteractableFromRaycast(Raycaster);
        if (interactable != _highlightedInteractable)
        {
            _highlightedInteractable?.OnUnhighlight();
            _highlightedInteractable = interactable;
            _highlightedInteractable?.OnHighlight();
        }
    }

    // ── Spectator helpers ─────────────────────────────────────────────────────

    /// <summary>
	/// Définit la cible courante de l'orbit-cam spectateur. Désabonne l'éventuelle
    /// cible précédente du signal Died (pour ne pas accumuler de handlers à chaque
	/// cycle) et abonne la nouvelle si c'est un Character — auto-avance quand la
	/// cible meurt. Le pivot null est valide (= orbite autour de Vector3.Zero).
	/// </summary>
	private void _SetSpectatorTarget(Node3D? InTarget)
	{
		if (_spectatorTrackedCharacter is not null)
		{
			_spectatorTrackedCharacter.Died -= _OnSpectatorTargetDied;
			_spectatorTrackedCharacter = null;
		}
		_spectatorCurrentTarget = InTarget;
		if (InTarget is Character ch)
		{
			_spectatorTrackedCharacter = ch;
			ch.Died += _OnSpectatorTargetDied;
		}
		_cameraMan?.SetSpectatorPivot(InTarget);
	}

	/// <summary>Handler abonné via <see cref="_SetSpectatorTarget"/>&#160;: la cible
	/// suivie vient de mourir, on avance d'un cran dans la liste.</summary>
    private void _OnSpectatorTargetDied(int InPeerId, DeathReason InReason)
    {
        if (_characterState != CharacterState.Spectating) return;
        _CycleSpectatorTarget(+1);
    }

    /// <summary>
    /// Construit la liste circulaire [ancre, joueurs vivants triés par PeerId
    /// (sauf soi-même)] et avance de <paramref name="InDir"/> cran(s). La liste
	/// est rebâtie à chaque appel parce qu'elle change quand des joueurs meurent
	/// — pas de cache à invalider.
	/// </summary>
	private void _CycleSpectatorTarget(int InDir)
	{
		var anchor = GetTree().GetFirstNodeInGroup("spectator_anchor") as Node3D;
		var alive = GetTree().GetNodesInGroup("players_alive");

		var targets = new System.Collections.Generic.List<Node3D?>();
		targets.Add(anchor); // entrée 0 : ancre (peut être null — pivot devient Vector3.Zero)
		var aliveChars = new System.Collections.Generic.List<Character>();
		foreach (var n in alive)
		{
			if (n is Character c && c.PeerId != PeerId) aliveChars.Add(c);
		}
		aliveChars.Sort((a, b) => a.PeerId.CompareTo(b.PeerId));
		foreach (var c in aliveChars) targets.Add(c);

		// Liste de taille 1 (juste l'ancre, aucun autre joueur vivant)&#160;: rien à
		// cycler, on remet le pivot sur l'ancre et on sort.
		if (targets.Count <= 1)
		{
			_SetSpectatorTarget(anchor);
			return;
		}

		int currentIdx = targets.IndexOf(_spectatorCurrentTarget);
		if (currentIdx < 0) currentIdx = 0;
		int next = ((currentIdx + InDir) % targets.Count + targets.Count) % targets.Count;
		_SetSpectatorTarget(targets[next]);
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
			PhysicsSkelton.IsRagdoll = shouldRagdoll;
			PhysicsSkelton.RemoteCorrection = shouldRagdoll;
			if (_capsule != null) _capsule.Disabled = shouldRagdoll;

			if (shouldRagdoll)
				PhysicsSkelton.ApplyRagdollKick(_snapshots[latestIdx].Velocity);
		}

		if (_remoteRagdoll)
		{
			PhysicsSkelton.RemoteSpineTarget = _snapshots[latestIdx].Position;
			PhysicsSkelton.RemoteHeadPitch = _snapshots[latestIdx].HeadPitch;
			PhysicsSkelton.RemoteHeadYaw = _snapshots[latestIdx].BodyYaw;
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
