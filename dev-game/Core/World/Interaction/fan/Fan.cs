using Godot;
using System;
using System.Collections.Generic;

public partial class Fan : Node3D
{
	[Export]
	public float WindForce = 15.0f;

	[Export]
	public float BladeSpeedMultiplier = 0.5f;

	[Export]
	public NodePath ModelFanPath;

	[Export]
	public NodePath AreaPath;

	[Export]
	public NodePath MarkerPath;

	private ModelFan _modelFan;
	private Area3D _area;
	private Marker3D _marker;

	private readonly List<Node3D> _bodiesInside = new();

	public override void _Ready()
	{
		_modelFan = GetNodeOrNull<ModelFan>(ModelFanPath);
		_area = GetNodeOrNull<Area3D>(AreaPath);
		_marker = GetNodeOrNull<Marker3D>(MarkerPath);

		if (_modelFan == null)
		{
			GD.PushError("plug ta fan");
			return;
		}

		if (_area == null)
		{
			GD.PushError("plug ton area");
			return;
		}

		if (_marker == null)
		{
			GD.PushError("plug ton marker");
			return;
		}

		float bladeSpeed = WindForce * BladeSpeedMultiplier;
		_modelFan.SetBladeSpeed(bladeSpeed);

		_area.BodyEntered += OnBodyEntered;
		_area.BodyExited += OnBodyExited;
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 pushDirection = -_marker.GlobalTransform.Basis.Z.Normalized();

		for (int i = _bodiesInside.Count - 1; i >= 0; i--)
		{
			Node3D body = _bodiesInside[i];

			if (!IsInstanceValid(body))
			{
				_bodiesInside.RemoveAt(i);
				continue;
			}

			if (body is CharacterBody3D character)
			{
				character.Velocity += pushDirection * WindForce * (float)delta;
			}
			else if (body is RigidBody3D rigid)
			{
				rigid.ApplyCentralForce(pushDirection * WindForce);
			}
		}
	}

	private void OnBodyEntered(Node body)
	{
		if (body is Node3D node3D && !_bodiesInside.Contains(node3D))
		{
			_bodiesInside.Add(node3D);
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body is Node3D node3D)
		{
			_bodiesInside.Remove(node3D);
		}
	}
}