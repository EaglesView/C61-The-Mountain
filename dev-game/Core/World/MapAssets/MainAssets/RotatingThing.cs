using Godot;
public partial class RotatingThing : AnimatableBody3D
{
    [Export] public float RotationSpeed = 1.0f; // radians per second
    [Export] public float RagdollForceThreshold = 8.0f;

    public override void _PhysicsProcess(double delta)
    {
        // Rotate around Y axis — this is what AnimatableBody3D tracks
        // to compute its angular/linear velocity for collisions
        RotateY(RotationSpeed * (float)delta);
    }
}
