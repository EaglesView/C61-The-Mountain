using Godot;
using System;
using static Utils.RayCastUtils;
public partial class Player : CharacterBody3D
{
	[Export] private Camera3D _cam;

	public const float Speed = 5.0f;
	public const float JumpVelocity = 4.5f;
	[ExportGroup("Controller Settings")]
	[Export] private float _mouseSensitivity = 0.002f;
	[Export] private float _controllerSensitivity = 2.5f; // radians / sec
	[Export] private float _interactionRange = 3.0f;
	required public RayCast3D _raycaster;
	//[Export] private Node3D _characterRig;
	public override void _Ready()
	{
		_raycaster = GetNode<RayCast3D>("Camera3D/RayCast3D");
		Input.MouseMode = Input.MouseModeEnum.Captured; //Cache le curseur
		//if (_characterRig == null) return;




	}
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			//Rotate le player en horizontal complet (on fera la tete plus tard isolé)
			RotateY(-mouseMotion.Relative.X * _mouseSensitivity);

			// Vertical tourne seulement la caméra pour l'instant
			_cam.RotateX(-mouseMotion.Relative.Y * _mouseSensitivity);
			_cam.Rotation = new Vector3(
				Mathf.Clamp(_cam.Rotation.X, Mathf.DegToRad(-80), Mathf.DegToRad(80)),
				_cam.Rotation.Y,
				_cam.Rotation.Z
			);
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

		if (Input.IsActionJustPressed("interact"))
		{
			Interactable? interactable = GetInteractableFromRaycast(_raycaster, _interactionRange);
			if (interactable != null)
			{
				interactable.Interact(this);
			}
			else
			{
				GetObjectTypeFromRaycast(_raycaster);
			}
		}

		// Basic movements from the godot boilerplate, to adapt to the game
		Vector2 aimDir = Input.GetVector("aim_left", "aim_right", "aim_up", "aim_down");
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
		//Gérer le aiming (controller ou souris)
		if (aimDir != Vector2.Zero)
		{
			RotateY(-aimDir.X * _controllerSensitivity * (float)delta);

			_cam.RotateX(-aimDir.Y * _controllerSensitivity * (float)delta);
			_cam.Rotation = new Vector3(
				Mathf.Clamp(_cam.Rotation.X, Mathf.DegToRad(-80), Mathf.DegToRad(80)),
				_cam.Rotation.Y,
				_cam.Rotation.Z
			);
		}
		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * Speed;
			velocity.Z = direction.Z * Speed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		}

		Velocity = velocity;
		MoveAndSlide();
	}
}
