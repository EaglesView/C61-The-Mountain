using Godot;

public partial class PressurePlate : Node3D
{
	[Export] public AnimatableBody3D Button;
	[Export] public Area3D DetectionArea;
	[Export] public OmniLight3D Light;

	[Export] public float PressDepth = 0.04f;
	[Export] public float AnimationDuration = 0.1f;

	[Signal] public delegate void ActivatedEventHandler();
	[Signal] public delegate void DeactivatedEventHandler();

	private int _bodiesInside = 0;
	private bool _isPressed = false;
	private Vector3 _buttonRestPosition;

	public override void _Ready()
	{
		_buttonRestPosition = Button.Position;
		DetectionArea.BodyEntered += OnBodyEntered;
		DetectionArea.BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node3D body)
	{
		_bodiesInside++;
		if (!_isPressed)
			Press();
	}

	private void OnBodyExited(Node3D body)
	{
		_bodiesInside = Mathf.Max(0, _bodiesInside - 1);
		if (_bodiesInside == 0 && _isPressed)
			Release();
	}

	private void Press()
	{
		_isPressed = true;
		if (Light != null)
			Light.Visible = true;
		var mesh = Button.GetNode<MeshInstance3D>("MeshInstance3D");
		var mat = mesh.GetActiveMaterial(0) as StandardMaterial3D;
		if (mat != null)
		{
			mat.EmissionEnabled = true;
		}
		var tween = CreateTween();
		tween.TweenProperty(Button, "position", _buttonRestPosition + Vector3.Down * PressDepth, AnimationDuration);
		EmitSignal(SignalName.Activated);
	}

	private void Release()
	{
		_isPressed = false;
		if (Light != null)
			Light.Visible = false;
		var mesh = Button.GetNode<MeshInstance3D>("MeshInstance3D");
		var mat = mesh.GetActiveMaterial(0) as StandardMaterial3D;
		if (mat != null)
			mat.EmissionEnabled = false;
		var tween = CreateTween();
		tween.TweenProperty(Button, "position", _buttonRestPosition, AnimationDuration);
		EmitSignal(SignalName.Deactivated);
	}
}
