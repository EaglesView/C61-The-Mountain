using Godot;
using Godot.Collections;
using System.Collections.Generic;

public partial class PlayerSpawner : Node3D
{
	[Export] private Array<Marker3D> _spawnPoints = new();
	private int _nextSpawnIndex = 0;

	public override void _Ready()
	{
		if (_spawnPoints.Count > 0) return;
		foreach (var child in GetChildren())
		{
			if (child is Marker3D marker)
				_spawnPoints.Add(marker);
		}
		if (_spawnPoints.Count == 0)
			GD.PrintErr("[PlayerSpawner] No spawn points found — add Marker3D children or assign them in the inspector.");
	}

	public Vector3 GetNextSpawnPoint()
	{
		if (_spawnPoints.Count == 0) return Vector3.Zero;
		var point = _spawnPoints[_nextSpawnIndex];
		_nextSpawnIndex = (_nextSpawnIndex + 1) % _spawnPoints.Count;
		return point.GlobalPosition;
	}

	public Vector3 GetNextSpawnPointWithTeam(out int OutTeamId)
	{
		if (_spawnPoints.Count == 0)
		{
			OutTeamId = 0;
			return Vector3.Zero;
		}

		int index = _nextSpawnIndex;
		var point = _spawnPoints[index];
		_nextSpawnIndex = (_nextSpawnIndex + 1) % _spawnPoints.Count;

		int half = _spawnPoints.Count / 2;
		OutTeamId = index < half ? 1 : 2;
		return point.GlobalPosition;
	}

	public IReadOnlyList<Marker3D> GetSpawnPoints() => _spawnPoints;
}
