using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

using static Utils.CharacterUtils;
public partial class PhysicsSkeleton : Skeleton3D
{
	//EXPORTS
	[ExportGroup("Physical Properties")]
	[Export] public required Skeleton3D TargetSkeleton;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float LinearSpringStiffness = 1200.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float LinearSpringDamping = 40.0f;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float AngularSpringStiffness = 4000.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float AngularSpringDamping = 80.0f;
	private PhysicalBoneSimulator3D _boneSim;
	private List<PhysicalBone3D> _physicsBones;
	private int _headBoneIdx;
	private int _mouthBoneIdx;
	public float HeadAngle = 0f;

	[Export] public required AnimationPlayer AnimPlayer;

	/// <summary>
	/// Oriente la tête du personnage selon l'angle vertical de la caméra.
    /// xAngle en radians.
    /// </summary>
    public void SetHeadPose(float xAngle)
    {
        GD.Print($"SetHeadPose: angle={xAngle}, headIdx={_headBoneIdx}");

        var headRot = new Quaternion(Vector3.Right, -xAngle);
        var mouthRot = new Quaternion(Vector3.Right, -xAngle + (MathF.PI / 2.0f));

        TargetSkeleton.SetBonePoseRotation(_headBoneIdx, headRot);
        TargetSkeleton.SetBonePoseRotation(_mouthBoneIdx, mouthRot);
        SetBonePoseRotation(_headBoneIdx, headRot);
        SetBonePoseRotation(_mouthBoneIdx, mouthRot);
        GD.Print($"Pose after set: {GetBonePoseRotation(_headBoneIdx)}");

    }
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        ProcessPriority = 1;

        _boneSim = GetNode<PhysicalBoneSimulator3D>("PhysicalBoneSimulator3D");
        _headBoneIdx = TargetSkeleton.FindBone("Head.001");

        _mouthBoneIdx = TargetSkeleton.FindBone("Mouth.001");
        _physicsBones = _boneSim.GetChildren()
        .OfType<PhysicalBone3D>()
        .ToList();
        var bonesToSimulate = new Godot.Collections.Array<StringName>();
        foreach (PhysicalBone3D bone in _physicsBones)
        {
            //les physicalsbones sappelles part_000
            if (!bone.Name.ToString().Contains("Head_001") && !bone.Name.ToString().Contains("Mouth_001"))
            {
                GD.Print(bone.Name);
                bonesToSimulate.Add(new StringName(GetBoneName(bone.GetBoneId())));
            }

        }
        _boneSim.PhysicalBonesStartSimulation(bonesToSimulate);
        //_boneSim.PhysicalBonesStopSimulation(new Godot.Collections.Array<StringName> { "Head.001", "Mouth.001" }); _boneSim.Active = true;
        //AnimPlayer.Play("WalkAction_001");
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

            // into localspace
            Vector3 localAngVel = skeletonInv * bone.AngularVelocity;
            Vector3 localTorque = HookesLaw(angularDisplacement, localAngVel, AngularSpringStiffness, AngularSpringDamping);

            // Convert result back to world space before applying
            bone.AngularVelocity += GlobalTransform.Basis * (localTorque * (float)delta);
        }

    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        SetHeadPose(HeadAngle);

    }
}
