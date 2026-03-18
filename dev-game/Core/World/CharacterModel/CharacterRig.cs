using Godot;
using System;

public partial class CharacterRig : Node3D
{
	[ExportGroup("Dev")]
	[Export] private bool _ragdollActive = true;
	public required PhysicalBoneSimulator3D BoneSimulator;
	public required AnimationPlayer AnimPlayer;
	// ------ PRIVATE VARS TO THE RIG FOR EASY ACCESS
	private Skeleton3D _skeleton;
	private int _headBoneIdx;
	private int _mouthBoneIdx;

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

	/// <summary>
	/// Oriente la tête du personnage selon l'angle vertical de la caméra.
	/// xAngle en radians.
	/// </summary>
	public void SetHeadPose(float xAngle)
	{
		_skeleton.SetBonePoseRotation(_headBoneIdx, new Quaternion(Vector3.Right, -xAngle));
		_skeleton.SetBonePoseRotation(_mouthBoneIdx, new Quaternion(Vector3.Right, -xAngle + (MathF.PI / 2.0f)));
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_skeleton = GetNode<Skeleton3D>("Armature/Skeleton3D");
		BoneSimulator = GetNode<PhysicalBoneSimulator3D>("Armature/Skeleton3D/PhysicalBoneSimulator3D");
		_headBoneIdx = _skeleton.FindBone("Head.001");
		_mouthBoneIdx = _skeleton.FindBone("Mouth.001");
		SetRagdoll(_ragdollActive);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{

	}
}
