using Godot;
using System;

public partial class CharacterRig : Node3D
{
	[ExportGroup("Dev")]
	[Export] private bool _ragdollActive = true;
	public required PhysicalBoneSimulator3D BoneSimulator;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		BoneSimulator = GetNode<PhysicalBoneSimulator3D>("Armature/Skeleton3D/PhysicalBoneSimulator3D");
		SetRagdoll(_ragdollActive);

	}
	/// <summary>
	/// Permet de rendre le character ragdoll ou pas selon la condition
	/// </summary>
	public void SetRagdoll(bool active)
	{
		_ragdollActive = active;
		if (active)
			BoneSimulator.PhysicalBonesStartSimulation();
		else
			BoneSimulator.PhysicalBonesStopSimulation();
	}
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{

	}
}
