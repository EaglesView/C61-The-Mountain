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
using static Utils.RayCastUtils;
/// <summary>
/// Joueur principal du client, Permet de contrôler son propre personnage.
/// Sera éventuellement séparé avec un Character Class, mais pour l'instant
/// fonctionne pour le jeu.
/// </summary>
public partial class Player : Character
{
    /// ····································
    /// : _____  _____  ___  ___ _____ ___ :
    /// :| __\ \/ | _ \/ _ \| _ |_   _/ __|:
    /// :| _| >  <|  _| (_) |   / | | \__ \:
    /// :|___/_/\_|_|  \___/|_|_\ |_| |___/:
    /// ····································

    [ExportGroup("Player Settings")]
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float WalkSpeed = 2.5f;
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float RunMultiplier = 2.0f;
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float JumpVelocity = 4.5f;

    [ExportGroup("Controller Settings")]
    [Export(PropertyHint.Range, "0.0f,0.1f,0.001f")] private float _mouseSensitivity = 0.002f;
    [Export(PropertyHint.Range, "0.0f,5.0f,0.05f")] private float _controllerSensitivity = 2.5f; // radians / sec
    [ExportGroup("Character Nodes")]
    [Export] public required AnimationPlayer AnimPlayer;
    [Export] public required PhysicsSkeleton PhysicsSkelton;
    [Export] required public RayCast3D Raycaster;
    [Export] private Camera3D? _cam;
    private float _headAngle = 0.0f;//rads
    private float _prevAngle = 0.0f;
    private Skeleton3D? _animSkeleton;
    private string? _currentAnim;
    private Vector3 _offsetFP = new Vector3(0, 0.05f, 0.25f);
    private Vector3 _offsetTP = new Vector3(0, 0.1f, -1.25f);
    private Vector3 _currentCamOffset;
    //[Export] private Node3D _characterRig;

    public void SetAnimation(string anim)
    {
        if (_currentAnim != anim)
        {
            _currentAnim = anim;
            AnimPlayer.Play(anim);
        }
    }

    public void SetCamPos()
    {
        int boneIdx = PhysicsSkelton.FindBone("Head.001");
        Transform3D headWorld = PhysicsSkelton.GlobalTransform * PhysicsSkelton.GetBoneGlobalPose(boneIdx);
        _cam.GlobalPosition = headWorld.Origin + headWorld.Basis * _currentCamOffset;
    }

    /// ···········································
    /// : _    ___ ___ ___ _____   _____ _    ___ :
    /// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
    /// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
    /// :|____|___|_| |___\___| |_| \___|____|___|:
    /// ···········································
    public override void _Ready()
    {
        if (_cam == null) return;
        _currentCamOffset = _offsetFP;
        if (Raycaster == null) Raycaster = GetNode<RayCast3D>("PlayerCamera_FP/RayCast3D");
        Input.MouseMode = Input.MouseModeEnum.Captured; //Cache le curseur à son controle
                                                        //TODO: Mettre dans un médiateur de contrôle
                                                        // pour permettre de lose control
        if (PhysicsSkelton == null) PhysicsSkelton = GetNode<PhysicsSkeleton>("PhysicsRig/Armature/Skeleton3D");
        if (AnimPlayer == null) AnimPlayer = PhysicsSkelton.AnimPlayer;
        _animSkeleton = PhysicsSkelton.TargetSkeleton;

    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            //Rotate le player en horizontal complet (on fera la tete plus tard isolé)
            RotateY(-mouseMotion.Relative.X * _mouseSensitivity);

            // Vertical tourne seulement la caméra pour l'instant
            _cam.RotateX(mouseMotion.Relative.Y * _mouseSensitivity);
            _cam.Rotation = new Vector3(
                Mathf.Clamp(_cam.Rotation.X, Mathf.DegToRad(-80), Mathf.DegToRad(80)),
                _cam.Rotation.Y,
                _cam.Rotation.Z
            );
            //tourner la tete sur laxe x (les spine bones sont dans le process de PhysicsSkeleton.cs)
            _prevAngle = _headAngle;
            _headAngle = _cam.Rotation.X;
        }
        else if (@event is InputEventAction action)
        {

        }
    }
    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        // Add the gravity.
        if (!IsOnFloor())
        {
            velocity += GetGravity() * (float)delta;
        }

        // Handle Jump.
        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
        }
        if (Input.IsActionJustPressed("run"))
        {
            WalkSpeed *= RunMultiplier;
            AnimPlayer.SpeedScale *= RunMultiplier;
        }
        if (Input.IsActionJustReleased("run"))
        {
            WalkSpeed /= RunMultiplier;
            AnimPlayer.SpeedScale /= RunMultiplier;

        }

        if (Input.IsActionJustPressed("interact"))
        {

            GetObjectTypeFromRaycast(Raycaster);
            //TEMPORAIRE : Changer la camera a third person pour voir le rig
            //_currentCamOffset = (_currentCamOffset == _offsetFP)?_offsetTP:_offsetFP;
            PhysicsSkelton.Aiming = true;
            PhysicsSkelton.ArmPointDir = -Raycaster.GlobalTransform.Basis.Z;

        }
        if (Input.IsActionJustReleased("interact"))
        {
            PhysicsSkelton.Aiming = false;
        }
        if (Input.IsActionJustPressed("show_sign"))
        {
            PhysicsSkelton.ArmsUp = true;
        }
        if (Input.IsActionJustReleased("show_sign"))
        {
            PhysicsSkelton.ArmsUp = false;
        }
        // Basic movements from the godot boilerplate, to adapt to the game
        Vector2 aimDir = Input.GetVector("aim_left", "aim_right", "aim_up", "aim_down");
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Vector3 direction = (Transform.Basis * new Vector3(-inputDir.X, 0, -inputDir.Y)).Normalized();
        //Gérer le aiming (controller ou souris)

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * WalkSpeed;
            velocity.Z = direction.Z * WalkSpeed;
            SetAnimation("WalkAction_001");
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, WalkSpeed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, WalkSpeed);
            SetAnimation("Idle_001");
        }
        PhysicsSkelton.HeadAngle = _headAngle;
        if (PhysicsSkelton.Aiming)
        {
            PhysicsSkelton.ArmPointDir = -Raycaster.GlobalTransform.Basis.Z;
        }
        Velocity = velocity;
        MoveAndSlide();
    }
    public override void _Process(double delta)
    {
        SetCamPos();
    }
}
