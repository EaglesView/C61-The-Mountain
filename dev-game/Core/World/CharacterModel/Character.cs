using Godot;

public partial class Character : CharacterBody3D
{
    public const float Speed = 5.0f;
    public const float JumpVelocity = 4.5f;
    public const float RotationSpeed = 10.0f;
    public const float MouseSensitivity = 0.003f;

    private const float PitchMin = -Mathf.Pi * 0.4f; // ~-72°
    private const float PitchMax = Mathf.Pi * 0.2f;  // ~+36°

    private AnimationPlayer _animationPlayer;
    private Node3D _cameraYaw;
    private Camera3D _camera;
    private string _currentAnimation = "";

    private float _cameraWorldYaw = 0f;
    private float _cameraPitch = 0f;

    public override void _Ready()
    {
        _animationPlayer = GetNode<AnimationPlayer>("penguin01/AnimationPlayer");
        _cameraYaw = GetNode<Node3D>("CameraYaw");
        _camera = GetNode<Camera3D>("CameraYaw/Camera3D");
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            _cameraWorldYaw -= mouseMotion.Relative.X * MouseSensitivity;
            _cameraPitch = Mathf.Clamp(
                _cameraPitch - mouseMotion.Relative.Y * MouseSensitivity,
                PitchMin, PitchMax);
            _camera.Rotation = new Vector3(_cameraPitch, 0f, 0f);
        }

        if (@event is InputEventKey key && key.Keycode == Key.Escape && key.Pressed)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity += GetGravity() * (float)delta;

        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
            velocity.Y = JumpVelocity;

        Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

        // Movement relative to camera's horizontal facing direction
        var camBasis = Basis.FromEuler(new Vector3(0, _cameraWorldYaw, 0));
        Vector3 camForward = -camBasis.Z;
        Vector3 camRight = camBasis.X;
        Vector3 direction = (camForward * -inputDir.Y + camRight * inputDir.X).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
            RotateTowardDirection(direction, (float)delta);
            SetAnimation(AnimationState.Walk);
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
            SetAnimation(AnimationState.Idle);
        }

        Velocity = velocity;
        MoveAndSlide();

        // Restore camera yaw world rotation after character body may have rotated
        _cameraYaw.GlobalRotation = new Vector3(0f, _cameraWorldYaw, 0f);
    }

    // --- Animation ---

    private enum AnimationState { Idle, Walk }

    private void SetAnimation(AnimationState state)
    {
        string animName = state switch
        {
            AnimationState.Walk => "ArmatureAction",
            _ => ""
        };

        if (_currentAnimation == animName) return;
        _currentAnimation = animName;

        if (animName == "")
            _animationPlayer.Stop();
        else
            _animationPlayer.Play(animName);
    }

    // --- Rotation ---

    private void RotateTowardDirection(Vector3 direction, float delta)
    {
        float targetAngle = Mathf.Atan2(direction.X, direction.Z);
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(Rotation.Y, targetAngle, RotationSpeed * delta),
            Rotation.Z
        );
    }
}
