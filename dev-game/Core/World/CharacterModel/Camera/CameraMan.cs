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
	[Export(PropertyHint.Range, "0.0f,50.0f,1.0f")] private float _camDamping = 10.0f;
	[ExportSubgroup("Third Person Cam Properties")]
	[Export(PropertyHint.Range, "1.0f,99.0f,0.5f")] private float _cameraDistanceTP = 10.0f;

	private float _camDistance=0.0f;
	private Vector3 _camOffset = new Vector3(0.0f, 0.0f, 0.001f);

	public void SetCameraType(CameraType InCamType)
	{
		_camType = InCamType;
		switch(_camType){
		    case CameraType.FirstPerson:
			_camDistance = -0.0f;
		    break;
		    case CameraType.ThirdPerson:
			_camDistance = _cameraDistanceTP;
		    break;
		}
		SpringArmThirdPerson.SpringLength = _camDistance;
	}

	public Vector3 GetRaycastPointingVector(){
	 return -Raycaster.GlobalTransform.Basis.Z;
	}

	public void ComputeCameraPosition()
	{
		switch(_camType){
		    case CameraType.FirstPerson:
				_playerCamera.GlobalPosition = _playerCamera.GlobalPosition.Lerp(_player.GetHeadBonePosition()+_camOffset, _camDamping);
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
	ComputeCameraPosition();
	}
}
