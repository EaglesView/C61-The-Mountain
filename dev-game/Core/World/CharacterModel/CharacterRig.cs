using Godot;
using System;

public partial class CharacterRig : Node3D
{
	private PhysicalBoneSimulator3D _boneSimulator;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_boneSimulator = GetNode<PhysicalBoneSimulator3D>("Armature/Skeleton3D/PhysicalBoneSimulator3D");


	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		_boneSimulator.PhysicalBonesStartSimulation();
	}
}
