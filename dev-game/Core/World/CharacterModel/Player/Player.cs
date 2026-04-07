/*
+=============================================================+
|    _____ _          __  __              _        _          |
|   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
|     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
|     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
|                                                             |
|  ---------------------------------------------------------  |
|  Fichier:               Player.cs                           |
|  Auteur:           Jean-Marc Bouchard                       |
|  Fonction: Permet de contrôler le personnage client         |
|  ---------------------------------------------------------  |
|                                                             |
|                                                             |
|                                                             |
|                                                             |
+==============================================================+
*/
using Godot;
using System;
using static Utils.RayCastUtils;
using static Utils.CharacterUtils;
using static Utils.CameraUtils;
/// <summary>
/// Joueur principal du client, Permet de contrôler son propre personnage.
/// Sera éventuellement séparé avec un Character Class, mais pour l'instant
/// fonctionne pour le jeu.
/// </summary>
public partial class Player : Character
{
    /*
	····································
	: _____  _____  ___  ___ _____ ___ :
	:| __\ \/ | _ \/ _ \| _ |_   _/ __|:
	:| _| >  <|  _| (_) |   / | | \__ \:
	:|___/_/\_|_|  \___/|_|_\ |_| |___/:
	····································
	*/

    [ExportGroup("Player Settings")]
    ///<summary> La vitesse de marche du personnage joué</summary>
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float WalkSpeed = 2.5f;
    ///<summary> Le multiplicateur de vitesse de la course </summary>
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float RunMultiplier = 2.0f;
    ///<summary>La force de saut du personnage</summary>
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float JumpVelocity = 4.5f;

    [ExportGroup("Controller Settings")]
    [Export(PropertyHint.Range, "0.0f,0.1f,0.001f")] private float _mouseSensitivity = 0.002f;
    [Export(PropertyHint.Range, "0.0f,5.0f,0.05f")] private float _controllerSensitivity = 2.5f; // radians / sec
    [ExportGroup("Character Nodes")]
    [Export] private CameraMan? _cameraMan;
    [Export]
    ///<summary> Le raycaster du joueur pour detecter les objets</summary>
    public RayCast3D Raycaster;
    //[Export] public required AnimationPlayer AnimPlayer;
    //[Export] private Camera3D? _cam;
    private Skeleton3D? _animSkeleton;
    private Vector3 _offsetFP = new Vector3(0, 0.05f, 0.25f);
    private Vector3 _offsetTP = new Vector3(0, 0.1f, -1.25f);
    private Vector3 _currentCamOffset;
    private string? _currentAnim;
    private Interactable? _highlightedInteractable;
    //[Export] private Node3D _characterRig;
    private bool _showDebug = false;
    private bool _playerFocused = false;
    private bool _playerPaused = false;
    private CameraType _lastCamType;
    //public void SetCamPos()
    //{
    //    int boneIdx = PhysicsSkelton.FindBone("Head.001");
    //    Transform3D headWorld = PhysicsSkelton.GlobalTransform * PhysicsSkelton.GetBoneGlobalPose(boneIdx);
    //    _cam.GlobalPosition = headWorld.Origin + headWorld.Basis * _currentCamOffset;
    //}

    /// ···········································
    /// : _    ___ ___ ___ _____   _____ _    ___ :
    /// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
    /// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
    /// :|____|___|_| |___\___| |_| \___|____|___|:
    /// ···········································
    protected override void EnterState(CharacterState state)
    {
        base.EnterState(state);
        if (state == CharacterState.Paused)
        {
            _playerFocused = ToggleCharacterFocus(_playerFocused);
            GameMenu.Instance?.OpenMenu();
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
    }

    public override void _Ready()
    {
        base._Ready();
        PeerId = Multiplayer.GetUniqueId();

        if (_cameraMan == null) return;
        _currentCamOffset = _offsetFP;
        if (Raycaster == null) Raycaster = _cameraMan.GetNode<RayCast3D>("PlayerCamera/RayCastTo");
        if (DisplayServer.GetName() != "headless")
            _playerFocused = ToggleCharacterFocus(_playerFocused);
        //TODO: Mettre dans un médiateur de contrôle
        // pour permettre de lose control
        if (PhysicsSkelton == null) return;
        if (AnimPlayer == null) AnimPlayer = PhysicsSkelton.AnimPlayer;
        _animSkeleton = PhysicsSkelton.TargetSkeleton;
        speed = WalkSpeed;
        AddToGroup("local_player");

    }
    public override void _Input(InputEvent @event)
    {
        // En ragdoll : seul le saut permet de se relever
        if (_characterState == CharacterState.Ragdoll)
        {
            if (@event.IsActionPressed("jump"))
                TransitionTo(CharacterState.Recovering);
            return;
        }
        // Bloque tout l'input du joueur quand le menu de pause est ouvert
        if (_characterState == CharacterState.Paused)
        {
            if (@event.IsActionPressed("pause_menu"))
                TransitionTo(_stateBeforePause);
            return;
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {

            if (_cameraMan?.CamType == CameraType.ThirdPerson)
            {
                // TP : la souris orbite la caméra, le corps reste indépendant
                _cameraMan.RotateCameraTP(
                    mouseMotion.Relative.X,
                    -mouseMotion.Relative.Y,
                    _mouseSensitivity
                );
            }
            else
            {
                // FP : la souris tourne le corps et la tête
                RotateY(-mouseMotion.Relative.X * _mouseSensitivity);
            }
            float newAngle = headAngle + mouseMotion.Relative.Y * _mouseSensitivity;
            RotateHead(Mathf.Clamp(newAngle, Mathf.DegToRad(-80), Mathf.DegToRad(80)));

        }
    }
    public override void _PhysicsProcess(double delta)
    {
        velocity = Velocity;
        if (_cameraMan == null) return;

        // En pause : on laisse la gravité tourner mais on bloque les inputs de mouvement
        if (_characterState == CharacterState.Paused)
        {
            moveVec = Vector3.Zero;
            aimVec = Vector3.Zero;
            base._PhysicsProcess(delta);
            return;
        }

        // wipee jumpy
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
        {
            TransitionTo(CharacterState.Paused);
        }
        if (Input.IsActionJustPressed("interact"))
        {

            GetObjectTypeFromRaycast(Raycaster);
            PhysicsSkelton.Aiming = false;
            currentEmoteState = EmoteState.None; //temporary
            var interactable = GetInteractableFromRaycast(Raycaster);
            interactable?.Interact(this);

        }

        if (Input.IsActionJustPressed("show_sign"))
        {
            PhysicsSkelton.ArmsUp = true;

        }
        if (Input.IsActionJustReleased("show_sign"))
        {
            PhysicsSkelton.ArmsUp = false;
        }
        if (Input.IsActionJustPressed("change_view"))
        {
            _cameraMan?.SetNextCamera([CameraType.FirstPerson, CameraType.ThirdPerson]);
        }

        Vector2 aimDir = Input.GetVector("aim_left", "aim_right", "aim_up", "aim_down");
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Vector3 direction;

        if (_cameraMan?.CamType == CameraType.ThirdPerson)
        {
            // TP : le mouvement est relatif à la caméra, pas au corps
            // //TODO: Ajouter les Axis Settings (inverse axis, etc.)
            Vector3 camForward = _cameraMan.GetCameraForwardFlat();
            Vector3 camRight = _cameraMan.GetCameraRightFlat();
            direction = (camForward * -inputDir.Y + camRight * -inputDir.X).Normalized(); //Negatif pcq notre mesh est a l'envers, oops!

            // Le corps se tourne vers la direction de déplacement
            if (direction != Vector3.Zero)
                Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(-direction.X, -direction.Z), 0.15f), Rotation.Z);
        }
        else
        {
            // FP : le mouvement est relatif au corps
            direction = -(Transform.Basis * new Vector3(-inputDir.X, 0, -inputDir.Y)).Normalized();
        }

        moveVec = direction;
        aimVec = direction;
        base._PhysicsProcess(delta);
        if (currentEmoteState == EmoteState.Pointing)
        {
            pointVec = _cameraMan.GetRaycastPointingVector();

        }
        //Velocity = velocity;
        //MoveAndSlide();
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        //SetCamPos();
        int boneIdx = PhysicsSkelton.FindBone("Head.001");
        Transform3D headWorld = PhysicsSkelton.GlobalTransform * PhysicsSkelton.GetBoneGlobalPose(boneIdx);
        //_cam.GlobalPosition = headWorld.Origin + headWorld.Basis * new Vector3(0, 0.05f, 0.25f); //TODO: mettre offset dans var

        var interactable = GetInteractableFromRaycast(Raycaster);
        if (interactable != _highlightedInteractable)
        {
            _highlightedInteractable?.OnUnhighlight();
            _highlightedInteractable = interactable;
            _highlightedInteractable?.OnHighlight();
        }
    }
}
