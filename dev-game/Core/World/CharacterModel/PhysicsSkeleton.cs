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
    private PhysicalBoneSimulator3D? _boneSim;
    private List<PhysicalBone3D>? _physicsBones;
    private int _headBoneIdx;
    private int _mouthBoneIdx;
    private int _spine3BoneIdx;
    private int _spine4BoneIdx;
    public float HeadAngle = 0f;

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
		SetSpinePoseFromHead();
	}
	public void SetSpinePoseFromHead(){
	TargetSkeleton.SetBonePoseRotation(_spine3BoneIdx, new Quaternion(Vector3.Right, -HeadAngle * 1.0f));
	TargetSkeleton.SetBonePoseRotation(_spine4BoneIdx, new Quaternion(Vector3.Right, -HeadAngle * 0.4f));
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
			Basis skeletonInv = GlobalTransform.Basis.Inverse();
			Quaternion targetRot = (skeletonInv * TransformTarget.Basis).GetRotationQuaternion().Normalized();
			Quaternion currentRot = (skeletonInv * TransformCurrent.Basis).GetRotationQuaternion().Normalized();
			//Correction Bug Shortest Path, évite les rotations douteuses
			if (targetRot.Dot(currentRot) < 0f)
				currentRot = -currentRot;

			Quaternion rotDiff = (targetRot * currentRot.Inverse()).Normalized();
			//Le displacement angulaire est juste une fancy différence de rotation précise
			//Correction pour les erreurs de floating point
			float angle = rotDiff.GetAngle();
			Vector3 angularDisplacement = (angle > 1e-4f) ? rotDiff.GetAxis() * angle : Vector3.Zero;
			// Utiliser le localspace pour l'AngularVelocity et le Torque
            Vector3 localAngVel = skeletonInv * bone.AngularVelocity;
            Vector3 localTorque = HookesLaw(angularDisplacement, localAngVel, RotStiff, RotDamp);

			// Reconvertir en worldspace pour l'appliquer
			// ANGULAR APPLY
			bone.AngularVelocity += GlobalTransform.Basis * (localTorque * (float)delta);
		}

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		//Process roule apres physicsprocess(), et a cause de
		//         ProcessPriority = 1;
		// je peux set les angles et les forces que le player envoie avant le reste

		//Ajuster les bones en dessous de la tete selon la rotation du joueur

		//Mettre la position de la tete
		SetHeadPose(HeadAngle);


	}
}
