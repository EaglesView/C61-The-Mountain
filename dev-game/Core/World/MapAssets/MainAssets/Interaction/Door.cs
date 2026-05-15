using Godot;

public partial class Door : Node3D, IActivatable
{
    [ExportGroup("Door Settings")]
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float Height = 3.0f;
    [Export(PropertyHint.Range, "0.1f,5.0f,0.05f")] public float Duration = 1.5f;

    private bool _isOpen = false;

    public void Activate()
    {
        if (_isOpen) return;
        _isOpen = true;

        var tween = CreateTween();
        tween.TweenProperty(this, "position:y", Position.Y + Height, Duration)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
    }
}
