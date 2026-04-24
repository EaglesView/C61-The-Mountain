using Godot;
using System.Linq;

public partial class WindZoneDcm : Node3D
{
	[Export] PackedScene WindAreaScene;
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public float SpawnInterval = 10.0f;
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f")] public float WindForce = 6.0f;
	[Export] public Vector3 WindDirection = new Vector3(0f, 0f, 6f);
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaTotal = 6.0f;
	[Export(PropertyHint.Range, "0.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaAggressive = 1.0f;
	[Export(PropertyHint.Range, "1,1024,1")] public int SpawnCount = 1;

	private float _spawnTimer = 0f;
	private float SubInterval => SpawnInterval / SpawnCount;
	private BoxShape3D _spawnBox;

	public override void _Ready()
	{
		if (WindAreaScene == null)
			GD.PushError("Packed scene du vent, est ou ?");

		var area = GetChildren().OfType<Area3D>().FirstOrDefault();
		var shape = area?.GetChildren().OfType<CollisionShape3D>().FirstOrDefault();
		_spawnBox = shape?.Shape as BoxShape3D;

		if (_spawnBox == null)
			GD.PushWarning("[WindZoneDcm] Aucun BoxShape3D trouvé — les WindAreas vont spawn à l'origine.");
	}

	public override void _Process(double delta)
	{
		if (WindAreaScene == null) return;

		_spawnTimer += (float)delta;
		if (_spawnTimer >= SubInterval)
		{
			_spawnTimer = 0f;
			_SpawnWindArea();
		}
	}

	private void _SpawnWindArea()
	{
		var area = WindAreaScene.Instantiate<WindArea>();
		area.WindForce = WindForce;
		area.WindDirection = WindDirection;
		area.DurationAreaTotal = DurationAreaTotal;
		area.DurationAreaAggressive = DurationAreaAggressive;

		if (_spawnBox != null)
		{
			Vector3 half = _spawnBox.Size / 2f;
			area.Position = new Vector3(
				(float)GD.RandRange(-half.X, half.X),
				0f,
				(float)GD.RandRange(-half.Z, half.Z)
			);
		}

		AddChild(area);
	}
}
