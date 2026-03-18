using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using static Utils.CharacterUtils;
public partial class PhysicsSkeleton : Skeleton3D
{
	//EXPORTS
	[ExportGroup("Physical Properties")]
	[Export] public Skeleton3D TargetSkeleton;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float LinearSpringStiffness = 1200.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float LinearSpringDamping = 40.0f;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float AngularSpringStiffness = 4000.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float AngularSpringDamping = 80.0f;
	private PhysicalBoneSimulator3D _boneSim;
	private List<PhysicalBone3D> _physicsBones;
	[Export] public required AnimationPlayer AnimPlayer;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_boneSim = GetNode<PhysicalBoneSimulator3D>("PhysicalBoneSimulator3D");
		_boneSim.PhysicalBonesStartSimulation();
		_boneSim.Active = true;
		_physicsBones = _boneSim.GetChildren()
		.OfType<PhysicalBone3D>()
		.ToList();
		AnimPlayer.Play("WalkAction_001");
	}

	public override void _PhysicsProcess(double delta)
	{
		foreach (PhysicalBone3D bone in _physicsBones)
		{
			Transform3D TransformTarget = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(bone.GetBoneId());
			Transform3D TransformCurrent = bone.GlobalTransform * bone.BodyOffset.AffineInverse();//* GetBoneGlobalPose(bone.GetBoneId());

			Vector3 PositionDifference = TransformTarget.Origin - TransformCurrent.Origin;
			Vector3 Force = HookesLaw(PositionDifference, bone.LinearVelocity, LinearSpringStiffness, LinearSpringDamping);
			bone.LinearVelocity += Force * (float)delta; //linear
			Basis skeletonInv = GlobalTransform.Basis.Inverse();

			Quaternion targetRot = (skeletonInv * TransformTarget.Basis).GetRotationQuaternion().Normalized();
			Quaternion currentRot = (skeletonInv * TransformCurrent.Basis).GetRotationQuaternion().Normalized();

			Quaternion rotDiff = (targetRot * currentRot.Inverse()).Normalized();
			Vector3 angularDisplacement = rotDiff.GetAxis() * rotDiff.GetAngle();

			// Also bring angular velocity into local space so Hooke's law is consistent
            Vector3 localAngVel = skeletonInv * bone.AngularVelocity;
            Vector3 localTorque = HookesLaw(angularDisplacement, localAngVel, AngularSpringStiffness, AngularSpringDamping);

            // Convert result back to world space before applying
            bone.AngularVelocity += GlobalTransform.Basis * (localTorque * (float)delta);
        }
    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }
}
