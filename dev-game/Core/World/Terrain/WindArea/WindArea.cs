using Core.Network;
using Godot;
using System.Collections.Generic;

public partial class WindArea : Node3D
{
	enum WindState { Aggressive, Normal }

	[Export] public Area3D WindZone;
	[Export(PropertyHint.Range, "0.1f,1000.0f,0.1f,suffix:m")] public Vector2 BoxSize = new Vector2(1.0f, 1.0f);
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaTotal = 6.0f;
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f")] public float WindForce = 6.0f;
	[Export] public Vector3 WindDirection = new Vector3(0f, 0f, 6f);
	[Export(PropertyHint.Range, "0.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaAggressive = 1.0f;

	private float _timeToStop;
	private WindState _currentState;
	private readonly HashSet<Character> _bodiesInZone = new();
	private CollisionShape3D? _collisionShape;
	private FogVolume? _fogVolume;

	public override void _Ready()
	{
		_currentState = WindState.Normal;
		_timeToStop = DurationAreaTotal;
		WindZone.BodyEntered += _OnZoneBodyEntered;
		WindZone.BodyExited += _OnZoneBodyExited;

		_collisionShape = WindZone.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		_fogVolume = WindZone.GetNodeOrNull<FogVolume>("FogVolume");
		_ApplyBoxSize();
	}

	private void _ApplyBoxSize()
	{
		if (_collisionShape?.Shape is BoxShape3D box)
			box.Size = new Vector3(BoxSize.X, box.Size.Y, BoxSize.Y);
		if (_fogVolume != null)
			_fogVolume.Size = new Vector3(BoxSize.X, _fogVolume.Size.Y, BoxSize.Y);
	}

	private void _OnZoneBodyEntered(Node3D body)
	{
		if (body is not Character character) return;
		if (character.GetCurrentState() == Utils.CharacterUtils.CharacterState.Paused) return;

		_bodiesInZone.Add(character);

		switch (_currentState)
		{
			case WindState.Normal:
				character.Velocity += WindForce * WindDirection;
				break;
			case WindState.Aggressive:
				_ApplyAggressiveKick(character);
				break;
		}
	}

	private void _OnZoneBodyExited(Node3D body)
	{
		if (body is Character character)
			_bodiesInZone.Remove(character);
	}

	private void _ApplyAggressiveKick(Character character)
	{
		if (character.PeerId != NetworkManager.Instance.LocalPeerId) return;
		if (character.GetCurrentState() == Utils.CharacterUtils.CharacterState.Ragdoll) return;
		character.PhysicsSkelton.ApplyRagdollKick(WindForce * WindDirection);
		character.TransitionTo(Utils.CharacterUtils.CharacterState.Ragdoll);
	}

	public override void _Process(double delta)
	{
		_timeToStop -= (float)delta;

		if (_timeToStop <= 0f)
		{
			QueueFree();
			return;
		}

		WindState next = _timeToStop <= DurationAreaAggressive ? WindState.Aggressive : WindState.Normal;

		if (next == WindState.Aggressive && _currentState != WindState.Aggressive)
		{
			foreach (var character in _bodiesInZone)
				_ApplyAggressiveKick(character);
		}

		_currentState = next;
	}
}
