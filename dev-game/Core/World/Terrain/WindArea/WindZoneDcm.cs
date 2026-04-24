using Godot;
using System;

public partial class WindZoneDcm : Node3D
{
	[Export] PackedScene WindAreaScene;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (WindAreaScene == null)
		{
			GD.PushError("Packed scene du vent, est ou ?");
			return;

		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
