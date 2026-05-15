using Godot;
using System;

public partial class ModelFan : Node3D
{
	private Blade _blade;

	public override void _Ready()
	{
		_blade = GetNodeOrNull<Blade>("blade");

		if (_blade == null)
		{
			GD.PushError("pas de fan");
		}
	}

	public void SetBladeSpeed(float speed)
	{
		if (_blade == null)
		{
			_blade = GetNodeOrNull<Blade>("blade");
		}

		if (_blade != null)
		{
			_blade.Speed = speed;
		}
	}
}