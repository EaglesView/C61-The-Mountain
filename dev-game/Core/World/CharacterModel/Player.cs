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
using static Utils.CharacterUtils;
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
    [Export] public RayCast3D Raycaster;
    //[Export] public required AnimationPlayer AnimPlayer;
    //[Export] public required PhysicsSkeleton PhysicsSkelton;
    [Export] private CameraMan _cameraMan;
    //[Export] private Camera3D? _cam;
    private Skeleton3D? _animSkeleton;
    private Vector3 _offsetFP = new Vector3(0, 0.05f, 0.25f);
    private Vector3 _offsetTP = new Vector3(0, 0.1f, -1.25f);
    private Vector3 _currentCamOffset;
    private string? _currentAnim;
    private Interactable? _highlightedInteractable;
    //[Export] private Node3D _characterRig;
    private bool _showDebug = false;


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
    public override void _Ready()
    {
        PeerId = Multiplayer.GetUniqueId();

        if (_cameraMan == null) return;
        _currentCamOffset = _offsetFP;
        if (Raycaster == null) Raycaster = _cameraMan.GetNode<RayCast3D>("SpringArm3D/PlayerCamera/RayCastTo");
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured; //Cache le curseur à son controle
                                                            //TODO: Mettre dans un médiateur de contrôle
                                                            // pour permettre de lose control
        if (PhysicsSkelton == null) PhysicsSkelton = GetNode<PhysicsSkeleton>("PhysicsRig/Armature/Skeleton3D");
        if (AnimPlayer == null) AnimPlayer = PhysicsSkelton.AnimPlayer;
        _animSkeleton = PhysicsSkelton.TargetSkeleton;
        speed = WalkSpeed;
        AddToGroup("local_player");

    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            RotateY(-mouseMotion.Relative.X * _mouseSensitivity);
            float newAngle = headAngle + mouseMotion.Relative.Y * _mouseSensitivity;
            RotateHead(Mathf.Clamp(newAngle, Mathf.DegToRad(-80), Mathf.DegToRad(80)));
        }
        else if (@event is InputEventAction action)
        {

        }
    }
    public override void _PhysicsProcess(double delta)
    {
        velocity = Velocity;


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

        if (Input.IsActionJustPressed("interact"))
        {

            GetObjectTypeFromRaycast(Raycaster);
            //TEMPORAIRE : Changer la camera a third person pour voir le rig
            //_currentCamOffset = (_currentCamOffset == _offsetFP)?_offsetTP:_offsetFP;
            PhysicsSkelton.Aiming = false;
            currentEmoteState = EmoteState.None; //temporary

        }

        if (Input.IsActionJustPressed("show_sign"))
        {
            PhysicsSkelton.ArmsUp = true;
            var interactable = GetInteractableFromRaycast(Raycaster);
            interactable?.Interact(this);
        }

        // Basic movements from the godot boilerplate, to adapt to the game
        Vector2 aimDir = Input.GetVector("aim_left", "aim_right", "aim_up", "aim_down");
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Vector3 direction = (Transform.Basis * new Vector3(-inputDir.X, 0, -inputDir.Y)).Normalized();
        //Gérer le aiming (controller ou souris)
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
