using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class ConveyorBelt : Node3D
{
	[Export] public NodePath StartPointPath;
	[Export] public NodePath EndPointPath;

	[Export] public NodePath StartRollerPath;
	[Export] public NodePath EndRollerPath;
	[Export] public NodePath TopBeltPath;
	[Export] public NodePath BottomBeltPath;
	[Export] public NodePath BeltAreaPath;

	[Export] public float BeltVerticalOffset = 0.35f;

	[Export] public float VisualSpeed = 1.0f;
	[Export] public float RollerSpinMultiplier = 4.0f;

	[Export] public float BeltForce = 4.0f;

	[Export] public Vector3 StartPosition
	{
		get => _startPosition;
		set
		{
			_startPosition = value;
			if (_startPoint != null)
			{
				_startPoint.Position = value;
				UpdateBelt();
			}
		}
	}

	[Export] public Vector3 EndPosition
	{
		get => _endPosition;
		set
		{
			_endPosition = value;
			if (_endPoint != null)
			{
				_endPoint.Position = value;
				UpdateBelt();
			}
		}
	}

	private Vector3 _startPosition;
	private Vector3 _endPosition;

	private Node3D? _startPoint;
	private Node3D? _endPoint;
	private Node3D? _startRoller;
	private Node3D? _endRoller;
	private Node3D? _topBelt;
	private Node3D? _bottomBelt;
	private Area3D? _beltArea;

	private float _uvOffset = 0.0f;
	private readonly List<Node3D> _bodiesInside = new();

	public override void _Ready()
	{
		CacheNodes();
		ConnectAreaSignals();
		UpdateBelt();
		UpdateVisualMotion(0.0f);
	}

	public override void _Process(double delta)
	{
		if (_startPoint == null || _endPoint == null || _topBelt == null || _bottomBelt == null)
			CacheNodes();

		if (Engine.IsEditorHint())
			UpdateBelt();

		UpdateVisualMotion((float)delta);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint())
			return;

		if (_startPoint == null || _endPoint == null)
			return;

		Vector3 diff = _endPoint.GlobalPosition - _startPoint.GlobalPosition;
		if (diff.LengthSquared() <= 0.0001f)
			return;

		Vector3 forward = diff.Normalized();

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
				Vector3 beltVelocity = forward * BeltForce * (float)delta;
				character.GlobalPosition += new Vector3(beltVelocity.X, 0, beltVelocity.Z);
			}
			else if (body is RigidBody3D rigid)
			{
				rigid.ApplyCentralForce(forward * BeltForce);
			}
		}
	}

	private void CacheNodes()
	{
		_startPoint = GetNodeOrNull<Node3D>(StartPointPath);
		_endPoint = GetNodeOrNull<Node3D>(EndPointPath);
		_startRoller = GetNodeOrNull<Node3D>(StartRollerPath);
		_endRoller = GetNodeOrNull<Node3D>(EndRollerPath);
		_topBelt = GetNodeOrNull<Node3D>(TopBeltPath);
		_bottomBelt = GetNodeOrNull<Node3D>(BottomBeltPath);
		_beltArea = GetNodeOrNull<Area3D>(BeltAreaPath);

		if (_startPoint != null && _startPosition != Vector3.Zero)
			_startPoint.Position = _startPosition;
		else if (_startPoint != null)
			_startPosition = _startPoint.Position;

		if (_endPoint != null && _endPosition != Vector3.Zero)
			_endPoint.Position = _endPosition;
		else if (_endPoint != null)
			_endPosition = _endPoint.Position;
	}

	private void ConnectAreaSignals()
	{
		if (_beltArea == null)
			return;

		_beltArea.BodyEntered += OnBodyEntered;
		_beltArea.BodyExited += OnBodyExited;
	}

	private void UpdateBelt()
	{
		if (_startPoint == null || _endPoint == null || _topBelt == null || _bottomBelt == null)
			return;

		Vector3 start = _startPoint.GlobalPosition;
		Vector3 end = _endPoint.GlobalPosition;

		Vector3 diff = end - start;
		float length = diff.Length();

		if (length <= 0.001f)
			return;

		Vector3 center = (start + end) * 0.5f;
		Vector3 forward = diff.Normalized();

		Vector3 right = Vector3.Up.Cross(forward).Normalized();
		if (right.LengthSquared() <= 0.0001f)
			right = Vector3.Right;

		Vector3 localUp = forward.Cross(right).Normalized();

		Basis rollerBasis = new Basis(forward, right, localUp);
		if (_startRoller != null)
		{
			_startRoller.GlobalPosition = start;
			Vector3 s = _startRoller.Scale;
			_startRoller.GlobalBasis = rollerBasis;
			_startRoller.Scale = s;
		}
		if (_endRoller != null)
		{
			_endRoller.GlobalPosition = end;
			Vector3 s = _endRoller.Scale;
			_endRoller.GlobalBasis = rollerBasis;
			_endRoller.Scale = s;
		}

		Vector3 verticalOffset = localUp * BeltVerticalOffset;

		UpdateSingleBelt(_topBelt, center + verticalOffset, forward, localUp, length);
		UpdateSingleBelt(_bottomBelt, center - verticalOffset, forward, localUp, length);

		UpdateArea(center + verticalOffset, forward, localUp, length);
	}

	private void UpdateSingleBelt(Node3D belt, Vector3 position, Vector3 forward, Vector3 localUp, float length)
	{
		belt.GlobalPosition = position;

		Vector3 right = localUp.Cross(forward).Normalized();
		Basis basis = new Basis(right, localUp, forward);
		basis *= Basis.FromEuler(new Vector3(0.0f, -Mathf.Pi / 2.0f, 0.0f));

		belt.GlobalBasis = basis;

		Vector3 scale = belt.Scale;
		scale.X = length;
		belt.Scale = scale;
	}

	private void UpdateArea(Vector3 position, Vector3 forward, Vector3 localUp, float length)
	{
		if (_beltArea == null)
			return;

		_beltArea.GlobalPosition = position;

		Vector3 right = localUp.Cross(forward).Normalized();
		_beltArea.GlobalBasis = new Basis(right, localUp, forward);

		CollisionShape3D? collisionShape = _beltArea.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collisionShape?.Shape is BoxShape3D box)
		{
			Vector3 size = box.Size;
			size.Z = length;
			box.Size = size;
		}
	}

	private void UpdateVisualMotion(float delta)
	{
		_uvOffset += VisualSpeed * delta;

		UpdateBeltMaterial(_topBelt, -_uvOffset);
		UpdateBeltMaterial(_bottomBelt, _uvOffset);

		float rollerSpin = VisualSpeed * RollerSpinMultiplier * delta * -1.0f;

		_startRoller?.RotateObjectLocal(Vector3.Up, rollerSpin);
		_endRoller?.RotateObjectLocal(Vector3.Up, rollerSpin);
	}

	private void UpdateBeltMaterial(Node3D? beltNode, float offset)
	{
		if (beltNode is not MeshInstance3D meshInstance)
			return;

		if (meshInstance.MaterialOverride is ShaderMaterial shaderMat)
			shaderMat.SetShaderParameter("scroll_offset", offset);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (!_bodiesInside.Contains(body))
			_bodiesInside.Add(body);
	}

	private void OnBodyExited(Node3D body)
	{
		_bodiesInside.Remove(body);
	}
}