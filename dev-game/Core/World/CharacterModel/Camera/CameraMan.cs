/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:              Camera.cs                            |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Permet de contrôler la caméra en plusieurs vues  |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;
using System;
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
    [Export] private Node3D? _camTarget;
    [ExportGroup("Camera Properties")]
    [Export] private bool _enabled = true;//Disabled pour cutscenes?
    [Export] private CameraType _camType = CameraType.FirstPerson;

    [ExportSubgroup("First Person Cam Properties")]
    [Export(PropertyHint.Range, "0.0f,50.0f,1.0f")] private float _camDamping = 0.2f;
    [ExportSubgroup("Third Person Cam Properties")]
    [Export(PropertyHint.Range, "1.0f,99.0f,0.5f")] private float _cameraDistanceTP = 10.0f;

    private float _camDistance = 0.0f;
    private Vector3 _camOffset = new Vector3(0.0f, 0.0f, 0.001f);

    ///<summary>
    /// Permet de changer le type de camera à un type précis.
    /// </summary>
    public void SetCameraType(CameraType InCamType)
    {
        _camType = InCamType;
        switch (_camType)
        {
            case CameraType.FirstPerson:
                _camDistance = -0.0f;
                break;
            case CameraType.ThirdPerson:
                _camDistance = _cameraDistanceTP;
                break;
        }
        SpringArmThirdPerson.SpringLength = _camDistance;
    }
    ///<summary>
    /// Permet de changer la caméra au prochain mode disponible. Les modes disponibles sont envoyés par paramètre, et changement
    /// selon la caméra actuelle. Si la caméra actuelle n'est pas dans les paramètres, alors un erreur est lancé, a moins qu'on
    /// ajoute le paramètre InOverrideCameraType a true, qui va donner la caméra a l'index 0 si la caméra actuelle n'est pas dans
    /// la liste.
    /// </summary>
    public void SetNextCamera(CameraType[] InCameras, bool InOverrideCameraType = false)
    {
        int index = 0; //list pas IEnumerable
        bool found = false;
        foreach (CameraType camera in InCameras)
        {
            if (_camType == camera)
            {
                found = true;
                //la prochaine caméra doit être utilisé
                // vérifier la fin de liste avant.
                if (InCameras.Length <= index)
                {
                    _camType = InCameras[0]; //première cam si fin de liste
                }
                else
                {
                    _camType = InCameras[index + 1]; //la prochaine si pas fin de liste
                }
            }
            index++;
        }
        //validation pour voir si la caméra a été trouvé
        if (!found)
        {
            if (InOverrideCameraType)
            {
                _camType = InCameras[0]; //defaults a la premiere camera
            }
            else
            {
                GD.PrintErr("GAME_ERROR: AUCUNE CAMÉRA TROUVÉ COMPATIBLE AVEC LA CAMÉRA ACTUELLE DANS LA LISTE. Veuillez ajouter le paramètre InOverrideCameraType à true pour désactiver cette erreur.");
            }
        }
    }
    public Vector3 GetRaycastPointingVector()
    {
        return -Raycaster.GlobalTransform.Basis.Z;
    }

    public void ComputeCameraPosition(float delta)
    {
        switch (_camType)
        {
            case CameraType.FirstPerson:
                Vector3 headPos = _player.GetHeadBonePosition();
                Quaternion yaw = new Quaternion(Vector3.Up, _player.Rotation.Y + Mathf.Pi); //ajouter pi pour inverser la rotation
                Quaternion pitch = new Quaternion(Vector3.Right, -_player.GetHeadAngle());
                Basis camBasis = new Basis(yaw * pitch);
                _playerCamera.GlobalBasis = camBasis;
                _playerCamera.GlobalPosition = _playerCamera.GlobalPosition.Lerp(
                    headPos + camBasis * _camOffset,
                    Mathf.Clamp(_camDamping * delta, 0f, 1f)
                );
                break;
            case CameraType.ThirdPerson:
                _camDistance = _cameraDistanceTP;
                break;
        }
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        SetCameraType(_camType);

    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        ComputeCameraPosition((float)delta);
    }
}
