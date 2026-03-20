/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:          PhysicsSkeleton.cs                       |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Permet de contrôler les rigs client pour avoir   |
/// |  Un Active Ragdoll avec physiques et animations.            |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using static Utils.CharacterUtils;

/// <summary>
/// Skelette Multibone pour le Active Ragdoll. Contrôle la physique
/// entre les animations et le ragdoll, ne contrôle pas les animations
/// en soi.
/// </summary>
public partial class PhysicsSkeleton : Skeleton3D
{
	/// ····································
	/// : _____  _____  ___  ___ _____ ___ :
	/// :| __\ \/ | _ \/ _ \| _ |_   _/ __|:
	/// :| _| >  <|  _| (_) |   / | | \__ \:
	/// :|___/_/\_|_|  \___/|_|_\ |_| |___/:
	/// ····································
	[ExportGroup("Physical Properties")]
	[Export] public required Skeleton3D TargetSkeleton;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float LinearSpringStiffness = 1200.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float LinearSpringDamping = 40.0f;
	[Export(PropertyHint.Range, "0.0f,10000.0f,1,0f")] public float AngularSpringStiffness = 4000.0f;
	[Export(PropertyHint.Range, "0.0f,200.0f,1,0f")] public float AngularSpringDamping = 80.0f;
	[Export] public bool IsRagdoll = false;

	public enum ArmGesture { None, Pointing, Raising }// for future me
	public ArmGesture CurrentGesture = ArmGesture.None;
	private float _gestureBlend = 0f;



	private PhysicalBoneSimulator3D? _boneSim;
	private List<PhysicalBone3D>? _physicsBones;
	public bool Aiming = false;
	public bool ArmsUp = false;
	// SPECIAL BONES
	private int _headBoneIdx;
	private int _mouthBoneIdx;
	private int _spine3BoneIdx;
	private int _spine4BoneIdx;
	private int _arm1RIdx, _arm2RIdx, _arm3RIdx; // bone indices, filled in _Ready
	private int _arm1LIdx, _arm2LIdx, _arm3LIdx; // bone indices, filled in _Ready
	private int _arm1ParentIdx; // parent of upper arm


	public float HeadAngle = 0f;
	public Vector3 ArmPointDir = Vector3.Forward;
	public Vector3 ArmUpDir = Vector3.Up;
	[Export] public required AnimationPlayer AnimPlayer;

	/// <summary>
	/// Oriente la tête du personnage selon l'angle vertical de la caméra.
	/// xAngle en radians.
	/// </summary>
	public void SetHeadPose(float InHeadXAngle)
	{
		var headRot = new Quaternion(Vector3.Right, -InHeadXAngle);
		var mouthRot = new Quaternion(Vector3.Right, -InHeadXAngle + (MathF.PI / 2.0f));
		SetBonePoseRotation(_headBoneIdx, headRot);
		SetBonePoseRotation(_mouthBoneIdx, mouthRot);
	}
	public Transform3D GetHeadPose(){
	    // returns the head bone pose in world space
		return TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(_headBoneIdx);
	}
	public void SetSpinePoseFromHead(float InHeadXAngle){
	TargetSkeleton.SetBonePoseRotation(_spine3BoneIdx, new Quaternion(Vector3.Right, -InHeadXAngle * 1.0f));
	TargetSkeleton.SetBonePoseRotation(_spine4BoneIdx, new Quaternion(Vector3.Right, -InHeadXAngle * 0.4f));
	}
	public Vector3 GetLocalAimDir(Vector3 InWorldAimDir, int parentBoneIdx)
	{
		Transform3D parentGlobalTransform = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(parentBoneIdx);
		return parentGlobalTransform.Basis.Inverse() * InWorldAimDir.Normalized();
	}
	public void ArmPoint(double InDelta){
		Quaternion animPose   = TargetSkeleton.GetBonePoseRotation(_arm1RIdx);
		Quaternion pointPose  = CalculatePointingRot(ArmPointDir,_arm1ParentIdx);
		Quaternion blended    = animPose.Slerp(pointPose, _gestureBlend);
		TargetSkeleton.SetBonePoseRotation(_arm1RIdx, blended);
		_gestureBlend = Mathf.MoveToward(_gestureBlend, Aiming ? 1f : 0f, 30f * (float)InDelta);
	}
	public void ArmsRaise(double InDelta)
  {
	  // Get body-relative up from the spine
	  Transform3D spineWorld = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(_spine3BoneIdx);
	  Vector3 bodyUp = spineWorld.Basis.Y;

	  Quaternion animR  = TargetSkeleton.GetBonePoseRotation(_arm1RIdx);
	  Quaternion animL  = TargetSkeleton.GetBonePoseRotation(_arm1LIdx);
	  Quaternion target = CalculatePointingRot(bodyUp, _arm1ParentIdx);
	  Quaternion targetR2 = CalculatePointingRot(bodyUp, _arm1RIdx);
	  Quaternion targetR3 = CalculatePointingRot(bodyUp, _arm2RIdx);
	  Quaternion targetL2 = CalculatePointingRot(bodyUp, _arm1LIdx); // if symmetric, same idx
	  Quaternion targetL3 = CalculatePointingRot(bodyUp, _arm2LIdx); // if symmetric, same idx

	  TargetSkeleton.SetBonePoseRotation(_arm1RIdx, animR.Slerp(target, _gestureBlend));
	  //TargetSkeleton.SetBonePoseRotation(_arm2RIdx, animR.Slerp(targetR2, _gestureBlend));
	  //TargetSkeleton.SetBonePoseRotation(_arm3RIdx, animR.Slerp(targetR3, _gestureBlend));
	  TargetSkeleton.SetBonePoseRotation(_arm1LIdx, animL.Slerp(target, _gestureBlend));
	  //TargetSkeleton.SetBonePoseRotation(_arm2LIdx, animL.Slerp(targetL2, _gestureBlend));
	  //TargetSkeleton.SetBonePoseRotation(_arm3LIdx, animL.Slerp(targetL3, _gestureBlend));
	  _gestureBlend = Mathf.MoveToward(_gestureBlend, ArmsUp ? 1f : 0f, 30f * (float)InDelta);
  }
	public Quaternion CalculatePointingRot(Vector3 worldAimDir,int parentBoneIdx)
	{
		// Step 1: get the direction in the arm's parent bone local space
		Vector3 localDir = GetLocalAimDir(worldAimDir,parentBoneIdx); // your existing method

		// Step 2: bone points along +Y, we want +Y to align with localDir
		Vector3 from = Vector3.Up;
		Vector3 to   = localDir.Normalized();

		// Step 3: the rotation axis is perpendicular to both vectors
		Vector3 axis = from.Cross(to);

		// Guard: if from and to are already aligned (or exactly opposite), no rotation needed
		if (axis.LengthSquared() < 1e-6f)
		  return Quaternion.Identity;

		// Step 4: the angle between them
		float angle = from.AngleTo(to);

		return new Quaternion(axis.Normalized(), angle);
	}
	/// ···········································
	/// : _    ___ ___ ___ _____   _____ _    ___ :
	/// :| |  |_ _| __| __/ __\ \ / / __| |  | __|:
	/// :| |__ | || _|| _| (__ \ V | (__| |__| _| :
	/// :|____|___|_| |___\___| |_| \___|____|___|:
	/// ···········································
	public override void _Ready()
	{
		ProcessPriority = 1;

		_boneSim = GetNode<PhysicalBoneSimulator3D>("PhysicalBoneSimulator3D");
		//TARGET BONES FOR SPECIFIC CONTROLS
		// SPINE ET HEAD POUR LA ROTATION DE LA TETE
		// ARMS pour POINTER
		_headBoneIdx = TargetSkeleton.FindBone("Head.001");
		_spine3BoneIdx = TargetSkeleton.FindBone("Spine.003");
		_spine4BoneIdx = TargetSkeleton.FindBone("Spine.004");
		_mouthBoneIdx = TargetSkeleton.FindBone("Mouth.001");
		_arm1RIdx = TargetSkeleton.FindBone("Arm.001.R");
		_arm2RIdx = TargetSkeleton.FindBone("Arm.002.R");
		_arm3RIdx = TargetSkeleton.FindBone("Arm.003.R");
		_arm1LIdx = TargetSkeleton.FindBone("Arm.001.L");
		_arm2LIdx = TargetSkeleton.FindBone("Arm.002.L");
		_arm3LIdx = TargetSkeleton.FindBone("Arm.003.L");
		_arm1ParentIdx = TargetSkeleton.FindBone("Spine.003");
		//physics bones
		//
		_physicsBones = _boneSim.GetChildren()
		.OfType<PhysicalBone3D>()
		.ToList();
		var bonesToSimulate = new Godot.Collections.Array<StringName>();
		foreach (PhysicalBone3D bone in _physicsBones)
		{

		bonesToSimulate.Add(new StringName(GetBoneName(bone.GetBoneId())));

		}
		_boneSim.PhysicalBonesStartSimulation(bonesToSimulate);
	}
	public override void _PhysicsProcess(double delta)
	{
	if(IsRagdoll) return;
		if (_physicsBones == null) return;
		foreach (PhysicalBone3D bone in _physicsBones)
		{
			//if (Aiming && bone.GetBoneId() == _arm1RIdx){
				//ignorer le bras droit pour linstant si aiming

				//continue;
				//}
			float LinStiff = bone.HasMeta("LinearStiffness")  ? (float)bone.GetMeta("LinearStiffness")  : LinearSpringStiffness;
			float LinDamp = bone.HasMeta("LinearDamping")    ? (float)bone.GetMeta("LinearDamping")    : LinearSpringDamping;
			float RotStiff =  bone.HasMeta("AngularStiffness") ? (float)bone.GetMeta("AngularStiffness") : AngularSpringStiffness;
			float RotDamp = bone.HasMeta("AngularDamping")   ? (float)bone.GetMeta("AngularDamping")   : AngularSpringDamping;
			//LINEAR START
			//On ramasse les transformations du rig animé et ceux du rig physique
			Transform3D TransformTarget = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(bone.GetBoneId());
			Transform3D TransformCurrent = bone.GlobalTransform * bone.BodyOffset.AffineInverse();//* GetBoneGlobalPose(bone.GetBoneId());
			//On ramasse la différence, et on applique une force selon sa distance et vélocité actuelle
			Vector3 PositionDifference = TransformTarget.Origin - TransformCurrent.Origin;

			Vector3 Force = HookesLaw(PositionDifference, bone.LinearVelocity, LinStiff, LinDamp);
			// LINEAR APPLY
			bone.LinearVelocity += Force * (float)delta; //linear
			//ANGULAR START
			Quaternion targetRot = TransformTarget.Basis.GetRotationQuaternion();
			Quaternion currentRot = TransformCurrent.Basis.GetRotationQuaternion();
			//Correction Bug Shortest Path, évite les rotations douteuses
			if (targetRot.Dot(currentRot) < 0f)
				currentRot = -currentRot;

			Quaternion rotDiff = (targetRot * currentRot.Inverse()).Normalized();
			if (rotDiff.W < 0f) rotDiff = -rotDiff;
			//Le displacement angulaire est juste une fancy différence de rotation précise
			//Correction pour les erreurs de floating point
			float angle = rotDiff.GetAngle();

			Vector3 angularDisplacement = (angle > 1e-4f) ? rotDiff.GetAxis() * angle : Vector3.Zero;
			// ANGULAR APPLY — world space, pas besoin de conversion
			Vector3 worldTorque = HookesLaw(angularDisplacement, bone.AngularVelocity, RotStiff, RotDamp);
			bone.AngularVelocity += worldTorque * (float)delta;
		}

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		//Process roule apres physicsprocess(), et a cause de
		//         ProcessPriority = 1;
		// je peux set les angles et les forces que le player envoie avant le reste



		if(Aiming)ArmPoint(delta);
		if(ArmsUp)ArmsRaise(delta);
		SetHeadPose(HeadAngle);
		SetSpinePoseFromHead(HeadAngle);


	}
}
