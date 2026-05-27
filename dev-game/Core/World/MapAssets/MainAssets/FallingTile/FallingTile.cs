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

	private static readonly Color ColorWhite = new Color(1f, 1f, 1f, 0.5f);

	private static readonly Color ColorOrange = new Color(1f, 0.45f, 0f, 0.5f);
	private static readonly Color ColorRed = new Color(0.95f, 0.1f, 0.1f, 0.5f);

	private void OnBodyEntered(Node3D body)
	{
		if (!_active) return;
		if (body is not Character) return;

		switch (_state)
		{
			case TileState.White:
				_state = TileState.Orange;
				if (_mat != null) _mat.AlbedoColor = ColorOrange;
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
		if (_mat != null) _mat.AlbedoColor = ColorRed;
		GetTree().CreateTimer(0.5).Timeout += () =>
		{
			_state = TileState.Gone;
			QueueFree();
		};
	}

	public override void _Ready()
	{
		if (TileMesh != null)
		{
			var activeMat = TileMesh.GetActiveMaterial(0);
			if (activeMat != null)
			{
				_mat = (StandardMaterial3D)activeMat.Duplicate();
				TileMesh.SetSurfaceOverrideMaterial(0, _mat);
			}
		}
		if (DetectionArea != null)
			DetectionArea.BodyEntered += OnBodyEntered;
	}
}
