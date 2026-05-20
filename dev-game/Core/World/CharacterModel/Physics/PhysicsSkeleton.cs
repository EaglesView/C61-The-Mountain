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
    [Export(PropertyHint.Range, "0.0f,200.0f,1.0f")] public float AngularSpringDamping = 80.0f;
    [Export(PropertyHint.Range, "0.0f,5.0f,0,1f")] public float RagdollGraceTime = 1.0f;

    /// <summary>
    /// Grace appliquée à la sortie du ragdoll (transition IsRagdoll true→false).
    /// Donne le temps aux springs de ramener les os physiques vers la pose
    /// animée avant que le seuil de force ne puisse re-déclencher un ragdoll.
    /// Sans cette grace étendue, un personnage qui Recover depuis un ragdoll
    /// fortement éparpillé reboucle en Ragdoll dès la fin de RagdollGraceTime,
    /// et reste bloqué visuellement même si la FSM passe par Recovering.
    /// </summary>
    [Export(PropertyHint.Range, "0.0f,10.0f,0.1f")] public float RagdollExitGraceTime = 3.0f;

    private float _graceTime = 0.0f;
    private bool _wasRagdollLastFrame;
    protected static float forceThreshold = 500.0f;
    ///<summary>Définit si le personnage est en ragdoll ou pas</summary>
    [Export] public bool IsRagdoll = false;
    ///<summary>Flag levé par PhysicsSkeleton, lu et reset par Character._PhysicsProcess</summary>
    public bool RagdollTriggered { get; set; } = false;

    private float _gestureBlend = 0f;


    private PhysicalBoneSimulator3D? _boneSim;
    private List<PhysicalBone3D>? _physicsBones;
    private PhysicalBone3D? _spinePhysBone;
    private PhysicalBone3D? _headPhysBone;

    // ── Remote ragdoll correction (set by remote Player each tick) ────────────
    public bool RemoteCorrection = false;
    public Vector3 RemoteSpineTarget = Vector3.Zero;
    public float RemoteHeadPitch = 0f;
    public float RemoteHeadYaw = 0f;
    private const float SpineCorrectK = 15f;
    private const float HeadCorrectK = 8f;
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


    private void _SetHeadPose(Skeleton3D InPhysicsSkeleton, float InHeadXAngle)
    {
        var headRot = new Quaternion(Vector3.Right, -InHeadXAngle);
        var mouthRot = new Quaternion(Vector3.Right, -InHeadXAngle + (MathF.PI / 2.0f));
        InPhysicsSkeleton.SetBonePoseRotation(_headBoneIdx, headRot);
        InPhysicsSkeleton.SetBonePoseRotation(_mouthBoneIdx, mouthRot);
    }
    private void _SetSpinePoseFromHead(Skeleton3D InTargetSkeleton, float InHeadXAngle)
    {
        InTargetSkeleton.SetBonePoseRotation(_spine3BoneIdx, new Quaternion(Vector3.Right, -InHeadXAngle * 1.0f));
        InTargetSkeleton.SetBonePoseRotation(_spine4BoneIdx, new Quaternion(Vector3.Right, -InHeadXAngle * 0.4f));
    }
    private void _ArmPoint(double InDelta)
    {
        Vector3 localDir = GetLocalAimDir(TargetSkeleton, ArmPointDir, _arm1ParentIdx);

        Quaternion animPose = TargetSkeleton.GetBonePoseRotation(_arm1RIdx);
        Quaternion pointPose = CalculatePointingRot(localDir);
        Quaternion blended = animPose.Slerp(pointPose, _gestureBlend);
        TargetSkeleton.SetBonePoseRotation(_arm1RIdx, blended);
        _gestureBlend = Mathf.MoveToward(_gestureBlend, Aiming ? 1f : 0f, 30f * (float)InDelta);
    }
    private void _ArmsRaise(double InDelta)
    {
        // Get body-relative up from the spine
        Transform3D spineWorld = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(_spine3BoneIdx);
        Vector3 bodyUp = spineWorld.Basis.Y;
        Vector3 localDirTarget = GetLocalAimDir(TargetSkeleton, bodyUp, _arm1ParentIdx);
        Vector3 localDirTargetR2 = GetLocalAimDir(TargetSkeleton, bodyUp, _arm1RIdx);
        Vector3 localDirTargetR3 = GetLocalAimDir(TargetSkeleton, bodyUp, _arm2RIdx);
        Vector3 localDirTargetL2 = GetLocalAimDir(TargetSkeleton, bodyUp, _arm1LIdx);
        Vector3 localDirTargetL3 = GetLocalAimDir(TargetSkeleton, bodyUp, _arm2LIdx);

        Quaternion animR = TargetSkeleton.GetBonePoseRotation(_arm1RIdx);
        Quaternion animL = TargetSkeleton.GetBonePoseRotation(_arm1LIdx);
        Quaternion target = CalculatePointingRot(localDirTarget);
        Quaternion targetR2 = CalculatePointingRot(localDirTargetR2);
        Quaternion targetR3 = CalculatePointingRot(localDirTargetR3);
        Quaternion targetL2 = CalculatePointingRot(localDirTargetL2); // if symmetric, same idx
        Quaternion targetL3 = CalculatePointingRot(localDirTargetL3); // if symmetric, same idx

        TargetSkeleton.SetBonePoseRotation(_arm1RIdx, animR.Slerp(target, _gestureBlend));
        //TargetSkeleton.SetBonePoseRotation(_arm2RIdx, animR.Slerp(targetR2, _gestureBlend));
        //TargetSkeleton.SetBonePoseRotation(_arm3RIdx, animR.Slerp(targetR3, _gestureBlend));
        TargetSkeleton.SetBonePoseRotation(_arm1LIdx, animL.Slerp(target, _gestureBlend));
        //TargetSkeleton.SetBonePoseRotation(_arm2LIdx, animL.Slerp(targetL2, _gestureBlend));
        //TargetSkeleton.SetBonePoseRotation(_arm3LIdx, animL.Slerp(targetL3, _gestureBlend));
        _gestureBlend = Mathf.MoveToward(_gestureBlend, ArmsUp ? 1f : 0f, 30f * (float)InDelta);
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
            string bname = GetBoneName(bone.GetBoneId());
            bonesToSimulate.Add(new StringName(bname));
            if (bname == "Spine.001") _spinePhysBone = bone;
            if (bname == "Head.001") _headPhysBone = bone;
        }
        _boneSim.PhysicalBonesStartSimulation(bonesToSimulate);

        // Au spawn, les bones physiques et animés peuvent être momentanément
        // désync (téléport du parent vs. bones déjà en simulation). Amorcer la
        // grace empêche un pic de force sur la première frame de déclencher
        // RagdollTriggered et d'éjecter le personnage.
        _graceTime = RagdollGraceTime;
    }

    // ── Snapshot helpers (called by authoritative Character.SnapshotState) ────

    public Vector3 GetSpinePhysicsWorldPosition()
    {
        if (_spinePhysBone == null) return GlobalPosition;
        return (_spinePhysBone.GlobalTransform * _spinePhysBone.BodyOffset.AffineInverse()).Origin;
    }

    public Vector3 GetSpinePhysicsLinearVelocity()
        => _spinePhysBone?.LinearVelocity ?? Vector3.Zero;

    public (float pitch, float yaw) GetHeadPhysicsWorldAngles()
    {
        if (_headPhysBone == null) return (HeadAngle, 0f);
        Vector3 euler = (_headPhysBone.GlobalTransform * _headPhysBone.BodyOffset.AffineInverse())
                        .Basis.GetEuler();
        return (euler.X, euler.Y);
    }

    // Kick all bones with the authoritative spine velocity when ragdoll starts remotely
    public void ApplyRagdollKick(Vector3 spineVelocity)
    {
        if (_physicsBones == null) return;
        foreach (var bone in _physicsBones)
            bone.LinearVelocity = spineVelocity;
    }

    /// <summary>
    /// Applique une force de flottaison sur chaque os physique selon sa
    /// profondeur sous <paramref name="InWaterSurfaceY"/>. Annule la gravité
    /// (appliquée deux fois: moteur + boucle ragdoll) en proportion de
    /// l'immersion pour que la poussée ne soit pas masquée.
    /// </summary>
    public void ApplyBuoyancy(float InWaterSurfaceY, float InStrength, float InMaxDepth, float InDrag, float InDelta)
    {
        if (_physicsBones == null) return;
        float invMaxDepth = InMaxDepth > 0f ? 1f / InMaxDepth : 1f;
        float verticalDamp = Mathf.Clamp(1f - InDrag * InDelta, 0f, 1f);
        float lateralDamp = Mathf.Clamp(1f - InDrag * 0.4f * InDelta, 0f, 1f);
        foreach (PhysicalBone3D bone in _physicsBones)
        {
            // Position physique réelle du rigid body: GlobalTransform corrigé par
            // BodyOffset (le node PhysicalBone3D et son corps sont décalés selon
            // la pose de repos). Sans cette correction la profondeur est toujours
            // fausse — c'est la même formule que pour les ressorts (cf. plus bas).
            Vector3 bonePhysPos = (bone.GlobalTransform * bone.BodyOffset.AffineInverse()).Origin;
            float depth = InWaterSurfaceY - bonePhysPos.Y;
            if (depth <= 0f) continue;
            float submersion = Mathf.Clamp(depth * invMaxDepth, 0f, 1f);
            Vector3 v = bone.LinearVelocity;
            // Annule la gravité du frame (×2: moteur + PhysicsSkeleton._PhysicsProcess)
            // proportionnellement à la fraction immergée, sinon la poussée est noyée.
            Vector3 g = bone.GetGravity();
            v -= g * InDelta * 2f * submersion;
            // Poussée d'Archimède simplifiée: proportionnelle à la profondeur.
            v.Y += submersion * InStrength * InDelta;
            // Amortissement: plus fort sur Y pour stopper le bobbing,
            // plus doux sur XZ pour ne pas geler les mouvements latéraux.
            v.Y *= verticalDamp;
            v.X *= lateralDamp;
            v.Z *= lateralDamp;
            bone.LinearVelocity = v;
            bone.AngularVelocity *= lateralDamp;
            //GD.Print($"depth={depth:F3} surfaceY={InWaterSurfaceY:F3} boneY={bonePhysPos.Y:F3} submerged={submersion:F2}");

        }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_physicsBones == null) return;

        if (IsRagdoll)
        {
            _graceTime = RagdollGraceTime;
        }
        else if (_wasRagdollLastFrame)
        {

            _graceTime = RagdollExitGraceTime;
            RagdollTriggered = false;
        }
        _wasRagdollLastFrame = IsRagdoll;

        if (_graceTime > 0f)
        {
            _graceTime -= (float)delta;
        }
        foreach (PhysicalBone3D bone in _physicsBones)
        {
            Vector3 gravity = bone.GetGravity();
            bone.LinearVelocity += gravity * (float)delta;
            //if (Aiming && bone.GetBoneId() == _arm1RIdx){
            //ignorer le bras droit pour linstant si aiming
            if (IsRagdoll) continue;
            //continue;
            //}
            float LinStiff = bone.HasMeta("LinearStiffness") ? (float)bone.GetMeta("LinearStiffness") : LinearSpringStiffness;
            float LinDamp = bone.HasMeta("LinearDamping") ? (float)bone.GetMeta("LinearDamping") : LinearSpringDamping;
            float RotStiff = bone.HasMeta("AngularStiffness") ? (float)bone.GetMeta("AngularStiffness") : AngularSpringStiffness;
            float RotDamp = bone.HasMeta("AngularDamping") ? (float)bone.GetMeta("AngularDamping") : AngularSpringDamping;
            //LINEAR START
            //On ramasse les transformations du rig animé et ceux du rig physique
            Transform3D TransformTarget = TargetSkeleton.GlobalTransform * TargetSkeleton.GetBoneGlobalPose(bone.GetBoneId());
            Transform3D TransformCurrent = bone.GlobalTransform * bone.BodyOffset.AffineInverse();//* GetBoneGlobalPose(bone.GetBoneId());
                                                                                                  //On ramasse la différence, et on applique une force selon sa distance et vélocité actuelle
            Vector3 PositionDifference = TransformTarget.Origin - TransformCurrent.Origin;

            Vector3 Force = HookesLaw(PositionDifference, bone.LinearVelocity, LinStiff, LinDamp);
            // Comparaison sur la magnitude réelle.
            if (!IsRagdoll && Force.LengthSquared() > forceThreshold * forceThreshold && _graceTime <= 0f)
            {
                RagdollTriggered = true;
            }
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
        if (IsRagdoll)
        {
            int rootBone = FindBone("Spine.001");
            int ikBone = FindBone("Controller.IK");
            Transform3D poseRoot = GetBoneGlobalPose(rootBone);
            SetBoneGlobalPose(ikBone, poseRoot);
        }

        // Remote correction: gently steer spine position and head rotation
        // toward the authoritative values without overriding local physics.
        if (IsRagdoll && RemoteCorrection)
        {
            if (_spinePhysBone != null)
            {
                Transform3D cur = _spinePhysBone.GlobalTransform * _spinePhysBone.BodyOffset.AffineInverse();
                _spinePhysBone.LinearVelocity +=
                    (RemoteSpineTarget - cur.Origin) * SpineCorrectK * (float)delta;
            }

            if (_headPhysBone != null)
            {
                Transform3D cur = _headPhysBone.GlobalTransform * _headPhysBone.BodyOffset.AffineInverse();
                Quaternion target = Quaternion.FromEuler(new Vector3(RemoteHeadPitch, RemoteHeadYaw, 0f));
                Quaternion current = cur.Basis.GetRotationQuaternion().Normalized();
                if (target.Dot(current) < 0f) current = -current;
                Quaternion diff = (target * current.Inverse()).Normalized();
                if (diff.W < 0f) diff = -diff;
                float angle = diff.GetAngle();
                if (angle > 1e-4f)
                    _headPhysBone.AngularVelocity += diff.GetAxis() * angle * HeadCorrectK * (float)delta;
            }
        }

    }
    ///<summary>
    ///     Permet de Prendre la pose de la tête du personnage
    /// </summary>
    public Transform3D GetPoseTargetSkel(bool InIsPhysicsSkeleton = false)
    {
        int physicBone = TargetSkeleton.FindBone("Head.001");
        return GetPoseFromIdx(InIsPhysicsSkeleton ? this : TargetSkeleton, InIsPhysicsSkeleton ? physicBone : _headBoneIdx);
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        //Process roule apres physicsprocess(), et a cause de
        //         ProcessPriority = 1;
        // je peux set les angles et les forces que le player envoie avant le reste



        if (Aiming) _ArmPoint(delta);
        if (ArmsUp) _ArmsRaise(delta);
        _SetHeadPose(this, HeadAngle);
        _SetSpinePoseFromHead(TargetSkeleton, HeadAngle);


    }
}
