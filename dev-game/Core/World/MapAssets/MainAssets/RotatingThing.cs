using Godot;

public partial class RotatingThing : AnimatableBody3D
{
	[Export] public float RotationSpeed = 1.0f; // radians per second
	[Export] public float SlowedSpeed = 0.01f;
	[Export] public float RagdollForceThreshold = 8.0f;
	[Export] public float TransitionDuration = 0.5f;

	private float _baseSpeed;

	public override void _Ready()
	{
		_baseSpeed = RotationSpeed;
	}

	public override void _PhysicsProcess(double delta)
	{
		RotateY(RotationSpeed * (float)delta);
	}

	public void SlowDown()
	{
		var tween = CreateTween();
		tween.TweenProperty(this, "RotationSpeed", SlowedSpeed, TransitionDuration);
	}

	public void ResumeSpeed()
	{
		var tween = CreateTween();
		tween.TweenProperty(this, "RotationSpeed", _baseSpeed, TransitionDuration);
	}
}
