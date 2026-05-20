using Godot;
public partial class FallingTile : AnimatableBody3D
{
    [Export] public Area3D DetectionArea;
    [Export] public MeshInstance3D TileMesh;

    private enum TileState { White, Orange, Red, Gone }
    private TileState _state = TileState.White;
    private StandardMaterial3D _mat = null!;
    private bool _active = false;

    public void Activate() => _active = true;

    private static readonly Color ColorWhite  = Colors.White;
    private static readonly Color ColorOrange = new Color(1f, 0.45f, 0f);
    private static readonly Color ColorRed    = new Color(0.95f, 0.1f, 0.1f);

    private void OnBodyEntered(Node3D body)
    {
        if (!_active) return;
        if (body is not Character) return;

        switch (_state)
        {
            case TileState.White:
                _state = TileState.Orange;
                _mat.AlbedoColor = ColorOrange;
                GetTree().CreateTimer(0.5).Timeout += OnOrangeTimerExpired;
                break;
            case TileState.Orange:
                TransitionToRed();
                break;
        }
    }

    private void OnOrangeTimerExpired()
    {
        if (_state != TileState.Orange) return;
        if (DetectionArea != null && DetectionArea.GetOverlappingBodies().Count > 0)
            TransitionToRed();
    }

    private void TransitionToRed()
    {
        if (_state is TileState.Red or TileState.Gone) return;
        _state = TileState.Red;
        _mat.AlbedoColor = ColorRed;
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            _state = TileState.Gone;
            QueueFree();
        };
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D { AlbedoColor = ColorWhite };
        if (TileMesh != null)
            TileMesh.SetSurfaceOverrideMaterial(0, _mat);
        if (DetectionArea != null)
            DetectionArea.BodyEntered += OnBodyEntered;
    }
}
