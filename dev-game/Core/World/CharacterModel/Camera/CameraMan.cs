/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:              CameraMan.cs                         |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Permet de contrôler la caméra en plusieurs vues  |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;
using static Utils.CameraUtils;

public partial class CameraMan : Node3D
{
    /// ····································
    /// : _____  _____  ___  ___ _____ ___ :
    /// :| __\ \/ | _ \/ _ \| _ |_   _/ __|:
    /// :| _| >  <|  _| (_) |   / | | \__ \:
    /// :|___/_/\_|_|  \___/|_|_\ |_| |___/:
    /// ····································
    [ExportCategory("PlayerCameraAttributes")]
    [ExportGroup("Nodes")]
    [Export] private Camera3D _playerCamera;
    [Export] private Player _player;
    [Export] public required RayCast3D Raycaster;
    [Export] public required SpringArm3D SpringArmThirdPerson;
    [Export] private Marker3D _fpCamMarker;
    [Export] private Marker3D _tpCamMarker;
    [Export] private Vector3 _fpCamOffset = new Vector3(0f, 0f, -0.15f);
    [ExportGroup("Camera Properties")]
    [Export] private CameraType _camType = CameraType.ThirdPerson;

    [ExportSubgroup("First Person Cam Properties")]
    /// <summary>PRIVÉ - Lissage du suivi de la tête en vue FP (plus grand = plus lent).</summary>
    [Export(PropertyHint.Range, "0.0f,50.0f,1.0f")] private float _camDamping = 0.2f;

    [ExportSubgroup("Third Person Cam Properties")]
    [Export(PropertyHint.Range, "1.0f,99.0f,0.5f")] private float _cameraDistanceTP = 10.0f;
    /// <summary>PRIVÉ - Décalage vertical du pivot de la caméra TP par rapport au centre du personnage.</summary>
    [Export(PropertyHint.Range, "0.0f,5.0f,0.1f")] private float _tpHeightOffset = 1.5f;
    [Export(PropertyHint.Range, "-90.0f,0.0f,0.05f")] private float _tpPitchMin = -60.0f;
    [Export(PropertyHint.Range, "0.0f,90.0f,0.05f")] private float _tpPitchMax = 30.0f;

    [ExportSubgroup("Transition")]
    /// <summary>PRIVÉ - Vitesse de la transition entre les modes de caméra (unités/seconde).</summary>
    [Export(PropertyHint.Range, "1.0f,20.0f,0.5f")] private float _transitionSpeed = 8.0f;

    // Rotation TP indépendante du corps du personnage
    private float _tpYaw = 0.0f;
    private float _tpPitch = 0.0f;

    // Progression de la transition : 0 = FP, 1 = TP
    private float _lerpT = 0.0f;
    private float _lerpTarget = 0.0f;

    // ── Spectator mode ────────────────────────────────────────────────────────
    /// <summary>
    /// Quand <c>true</c>&#160;: ignore la branche FP/transition de ComputeCameraPosition
    /// et oriente la caméra autour de <see cref="_spectatorPivot"/> (orbit-cam
    /// piloté par <see cref="RotateCameraTP"/>). Engagé/désengagé par
    /// <see cref="EnterSpectator"/>/<see cref="ExitSpectator"/>.
    /// </summary>
    private bool _isSpectating;

    /// <summary>
	/// Cible courante de l'orbit-cam en mode spectateur. Peut être un Marker3D
	/// du niveau (ancre de départ) ou un autre Player vivant. <c>null</c> autorisé&#160;:
	/// la caméra orbite alors autour de Vector3.Zero (fallback contractuel).
	/// </summary>
	private Node3D? _spectatorPivot;

	/// <summary>Mode de caméra actif.</summary>
	public CameraType CamType => _camType;

	/// <summary>Indique si la caméra est en mode spectateur (orbit-cam libre).</summary>
	public bool IsSpectating => _isSpectating;

	/// <summary>
	/// Permet de changer le type de camera à un type précis.
	/// Lance la transition vers le nouveau mode.
	/// </summary>
	/// <param name="InCamType">Le type de caméra à activer.</param>
	public void SetCameraType(CameraType InCamType)
	{
		// Le mode spectateur n'a de sens qu'en TP&#160;: regarder à travers les yeux
		// d'un cadavre (ou d'un autre joueur dont on n'a pas la HeadAngle locale)
        // est incohérent. On force TP et on logue le refus en debug.
        if (_isSpectating && InCamType == CameraType.FirstPerson)
        {
            GD.Print("[CameraMan] SetCameraType(FirstPerson) ignoré : mode spectateur actif.");
            return;
        }
        _camType = InCamType;
        // Synchronise le yaw TP avec la rotation du corps à chaque changement.
		// En spectateur on garde le yaw courant (l'orbit-cam est piloté par la
		// souris&#160;; sync avec le yaw du joueur mort n'apporterait rien).
        if (!_isSpectating) _tpYaw = _player.Rotation.Y;
        _lerpTarget = (_camType == CameraType.ThirdPerson) ? 1.0f : 0.0f;
    }

    /// <summary>
    /// Bascule la caméra en mode spectateur&#160;: force TP, bloque les retours
    /// en FP, et autorise <see cref="ComputeCameraPosition"/> à orbiter autour
	/// d'un pivot externe (au lieu de suivre le corps du joueur local).
	/// La transition FP→TP du lerp est sautée (on met <c>_lerpT</c> à 1f
	/// directement) pour éviter un slide d'un demi-seconde au moment de la
    /// mort où la caméra glisserait vers le ragdoll avant de prendre le pivot.
    /// </summary>
    public void EnterSpectator()
    {
        _isSpectating = true;
        _camType = CameraType.ThirdPerson;
        _lerpT = 1f;
        _lerpTarget = 1f;
    }

	/// <summary>Sort du mode spectateur. Pas appelé en pratique pour l'instant
	/// (la phase Game se termine avant qu'on en ait besoin), mais expose le
    /// symétrique pour les futurs respawns mid-round.</summary>
    public void ExitSpectator()
    {
        _isSpectating = false;
    }

    /// <summary>
    /// Définit autour de quel Node3D la caméra spectateur doit orbiter. Accepte
    /// un Marker3D (ancre de niveau) ou un Player (réplique vivante). <c>null</c>
    /// fait orbiter autour de Vector3.Zero — fallback contractuel quand aucun
	/// marker n'est défini dans le niveau.
	/// </summary>
	public void SetSpectatorPivot(Node3D? InTarget)
	{
		_spectatorPivot = InTarget;
	}

	/// <summary>
	/// Permet de changer la caméra au prochain mode disponible dans la liste fournie.
	/// </summary>
	/// <param name="InCameras">Les modes de caméra disponibles à cycler.</param>
	/// <param name="InOverrideCameraType">
	/// Si <c>true</c>, retourne à l'index 0 si la caméra actuelle n'est pas dans la liste.
	/// Sinon, logue une erreur.
	/// </param>
	public void SetNextCamera(CameraType[] InCameras, bool InOverrideCameraType = false)
	{
		int index = 0;
		bool found = false;
		foreach (CameraType camera in InCameras)
		{
			if (_camType == camera)
			{
				found = true;
				SetCameraType(index + 1 >= InCameras.Length ? InCameras[0] : InCameras[index + 1]);
				break;
			}
			index++;
		}
		if (!found)
		{
			if (InOverrideCameraType)
				SetCameraType(InCameras[0]);
			else
				GD.PrintErr("GAME_ERROR: AUCUNE CAMÉRA TROUVÉ COMPATIBLE AVEC LA CAMÉRA ACTUELLE DANS LA LISTE. Veuillez ajouter le paramètre InOverrideCameraType à true pour désactiver cette erreur.");
		}
	}

	/// <summary>
	/// Applique un delta de rotation à la caméra TP selon le mouvement de la souris.
	/// En FP, cette méthode ne fait rien — la rotation FP est gérée par le Player via RotateY.
	/// </summary>
	/// <param name="InDeltaX">Déplacement horizontal de la souris (mouvement gauche/droite).</param>
	/// <param name="InDeltaY">Déplacement vertical de la souris (mouvement haut/bas).</param>
	/// <param name="InSensitivity">Sensibilité à appliquer au delta.</param>
	public void RotateCameraTP(float InDeltaX, float InDeltaY, float InSensitivity)
	{
		if (_camType != CameraType.ThirdPerson) return;
		_tpYaw -= InDeltaX * InSensitivity;
		_tpPitch += InDeltaY * InSensitivity;
		_tpPitch = Mathf.Clamp(_tpPitch, Mathf.DegToRad(_tpPitchMin), Mathf.DegToRad(_tpPitchMax));
	}

	/// <summary>
	/// Retourne la direction forward de la caméra TP projetée sur le plan horizontal (XZ).
	/// Utilisé par le Player pour calculer le mouvement relatif à la caméra.
	/// </summary>
	public Vector3 GetCameraForwardFlat()
	{
		return new Vector3(-Mathf.Sin(_tpYaw), 0f, -Mathf.Cos(_tpYaw)).Normalized();
	}

	/// <summary>
	/// Retourne la direction right de la caméra TP sur le plan horizontal.
	/// Utilisé par le Player pour le déplacement latéral relatif à la caméra.
	/// </summary>
	public Vector3 GetCameraRightFlat()
	{
		var forward = GetCameraForwardFlat();
		return new Vector3(forward.Z, 0f, -forward.X);
	}

	/// <summary>Retourne la direction du raycaster de la caméra.</summary>
	public Vector3 GetRaycastPointingVector()
	{
		return -Raycaster.GlobalTransform.Basis.Z;
	}

	/// <summary>
	/// Calcule et applique la position et l'orientation de la caméra selon le mode actif.
	/// Appelé à chaque frame dans <see cref="_Process"/>.
	/// </summary>
	/// <param name="delta">Temps écoulé depuis la dernière frame en secondes.</param>
	public void ComputeCameraPosition(float delta)
	{
		// Mode spectateur&#160;: branche dédiée qui orbite autour d'un pivot externe
		// (Marker3D du niveau ou autre Player). On saute totalement la mise à
		// jour du marqueur FP et la lecture de l'os de tête du _player (le
		// _player local est mort et son squelette ne fournit plus une cible
		// utile). RotateCameraTP continue de piloter _tpYaw / _tpPitch via la
		// souris exactement comme en jeu normal.
		if (_isSpectating)
		{
			Vector3 pivot = _spectatorPivot is not null
				? _spectatorPivot.GlobalPosition
				: Vector3.Zero;
			SpringArmThirdPerson.GlobalPosition = pivot + Vector3.Up * _tpHeightOffset;
			SpringArmThirdPerson.GlobalRotation = new Vector3(_tpPitch, _tpYaw, 0f);

			Vector3 tpPosS = _tpCamMarker.GlobalPosition;
			Vector3 pivotPosS = SpringArmThirdPerson.GlobalPosition;
			Basis tpBasisS = tpPosS.DistanceTo(pivotPosS) > 0.01f
				? new Transform3D(Basis.Identity, tpPosS).LookingAt(pivotPosS, Vector3.Up).Basis
				: Basis.Identity;
			_playerCamera.GlobalPosition = tpPosS;
			_playerCamera.GlobalBasis = tpBasisS;
			return;
		}

		// --- Mise à jour du marqueur FP (suit l'os de la tête avec lissage) ---
		Vector3 headPos = _player.PhysicsSkelton.GetNode<CollisionShape3D>("PhysicalBoneSimulator3D/Physical Bone Spine_001/CollisionShape3D").GlobalPosition;
		_fpCamMarker.GlobalPosition = _fpCamMarker.GlobalPosition.Lerp(
			headPos,
			Mathf.Clamp(_camDamping * delta, 0f, 1f)
		);

		// --- Mise à jour du pivot du SpringArm (maintient le marqueur TP à jour en permanence) ---
		SpringArmThirdPerson.GlobalPosition = headPos + Vector3.Up * _tpHeightOffset;
		SpringArmThirdPerson.GlobalRotation = new Vector3(_tpPitch, _tpYaw, 0f);

		// --- Avancement de la transition ---
		_lerpT = Mathf.MoveToward(_lerpT, _lerpTarget, _transitionSpeed * delta);

		// --- Base FP : orientation corps + tête ---
		Quaternion fpYaw = new Quaternion(Vector3.Up, _player.Rotation.Y);
		Quaternion fpPitch = new Quaternion(Vector3.Right, -_player.GetHeadAngle());
		Basis fpBasis = new Basis(fpYaw * fpPitch);

		// --- Base TP : regarde vers le pivot du SpringArm depuis le marqueur TP ---
		Vector3 tpPos = _tpCamMarker.GlobalPosition;
		Vector3 pivotPos = SpringArmThirdPerson.GlobalPosition;
		Basis tpBasis = tpPos.DistanceTo(pivotPos) > 0.01f
			? new Transform3D(Basis.Identity, tpPos).LookingAt(pivotPos, Vector3.Up).Basis
			: fpBasis;

		// --- Application à la caméra ---
		_playerCamera.GlobalPosition = (_fpCamMarker.GlobalPosition + fpBasis * _fpCamOffset).Lerp(tpPos, _lerpT);
		_playerCamera.GlobalBasis = fpBasis.Slerp(tpBasis, _lerpT);
	}

	public override void _Ready()
	{
		SpringArmThirdPerson.SpringLength = _cameraDistanceTP;
		SetCameraType(_camType);
	}

	public override void _Process(double delta)
	{
		ComputeCameraPosition((float)delta);
	}
}
