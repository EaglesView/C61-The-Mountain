using Godot;
using Core.Network.Rooms;
using static Utils.CharacterUtils;

/// <summary>
/// Slot 3D partagé entre Profile / Lobby / Winning. Instancie un
/// <see cref="Character"/> en mode <see cref="CharacterState.Preview"/> (FSM
/// gelée, pas de locomotion) et expose une API simple pour&#160;:
/// <list type="bullet">
///   <item>Échanger le chapeau&#160;: <see cref="SetHat"/>.</item>
///   <item>Pointer la tête vers une cible&#160;: <see cref="SetHeadTarget"/>
///         (radians, yaw rotate le root, pitch écrit dans HeadAngle).</item>
///   <item>Brancher le suivi souris depuis le SubViewportContainer hôte&#160;:
///         <see cref="BindMouseInput"/>.</item>
/// </list>
/// Le mode <see cref="InteractionMode.NetworkedRemote"/> est un placeholder pour
/// la future réplication de pose en lobby (RPC unreliable 15&#160;Hz)&#160;; il ne
/// fait rien de plus que NetworkedRemote ne consomme pas l'input local.
/// </summary>
public partial class Preview : Node3D
{
	public enum InteractionMode
	{
		///<summary>Penguin idle, aucune réaction. Défaut.</summary>
		Static,
		///<summary>Hover souris sur le SubViewportContainer ⇒ tête pointe vers le curseur (yaw/pitch clampés). Émet <see cref="HeadTargetChanged"/>. Pour le lobby&#160;: vue head-track naturelle.</summary>
		MouseLook,
		///<summary>Drag LMB sur le SubViewportContainer ⇒ rotation accumulée libre (yaw illimité, pitch clampé). Pour Profile&#160;: inspection 360°.</summary>
		DragOrbit,
		///<summary>La tête est pilotée par <see cref="SetHeadTarget"/> (RPC d'un peer distant). L'input local est ignoré.</summary>
		NetworkedRemote,
	}

	[Export] public InteractionMode Mode { get; set; } = InteractionMode.Static;
	[Export] public PackedScene? CharacterScene;
	[Export(PropertyHint.Range, "0,90,1")] public float MaxYawDegrees = 45f;
	[Export(PropertyHint.Range, "0,60,1")] public float MaxPitchDegrees = 25f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float HeadFollowSpeed = 10f;

	/// <summary>
	/// Émis uniquement en <see cref="InteractionMode.MouseLook"/> à chaque
	/// mouvement souris. Permet à l'hôte de la scène (LobbyScene) de relayer la
	/// pose via RPC sans que Preview ait à connaître le réseau.
	/// </summary>
	[Signal] public delegate void HeadTargetChangedEventHandler(float yaw, float pitch);

	private const string DefaultCharacterPath = "res://Core/World/CharacterModel/Character/Character.tscn";

	private Character? _character;
	private SubViewportContainer? _inputSource;
	private float _targetYaw, _targetPitch;
	private float _currentYaw, _currentPitch;
	private bool _isDragging;
	private Vector2 _lastDragPos;

	// 0.008 rad/px ≈ 0.46°/px — feel calibré sur l'ancien ProfileScene._cameraPivot.RotateY.
	private const float DragYawPerPixel = 0.008f;
	private const float DragPitchPerPixel = 0.005f;

	public override void _Ready()
	{
		SpawnCharacter(LobbyState.SelectedHatId);
	}

	/// <summary>
	/// Instancie le penguin avec le chapeau demandé. Idempotent&#160;: libère
	/// l'instance précédente. Appeler depuis l'hôte (Lobby slot par peer) au
	/// lieu de re-construire toute la SubViewport.
	/// </summary>
	public void SpawnCharacter(string hatId)
	{
		ClearCharacter();
		var scene = CharacterScene ?? ResourceLoader.Load<PackedScene>(DefaultCharacterPath);
		if (scene is null)
		{
			GD.PrintErr($"[Preview] Character scene introuvable&#160;: {DefaultCharacterPath}");
			return;
		}

		var character = scene.Instantiate<Character>();
		// IsPreview DOIT être set avant AddChild: c'est Character._Ready qui lit
		// le flag pour court-circuiter AddToGroup et figer la FSM.
		character.IsPreview = true;
		AddChild(character);
		_character = character;
		AttachHat(character, hatId);
	}

	/// <summary>Échange le chapeau sans reconstruire le penguin.</summary>
	public void SetHat(string hatId)
	{
		if (_character?.PhysicsSkelton is null) return;
		_character.PhysicsSkelton.GetNodeOrNull("HatAttachment")?.QueueFree();
		AttachHat(_character, hatId);
	}

	/// <summary>
	/// Cible angulaire de la tête, en radians, dans le repère local du penguin.
	/// Clampée par <see cref="MaxYawDegrees"/>/<see cref="MaxPitchDegrees"/>.
	/// Mode <see cref="InteractionMode.NetworkedRemote"/>&#160;: appelé depuis le
	/// handler RPC. Mode <see cref="InteractionMode.MouseLook"/>&#160;: utilisé
	/// indirectement via <see cref="BindMouseInput"/>.
	/// </summary>
	public void SetHeadTarget(float yaw, float pitch)
	{
		float yMax = Mathf.DegToRad(MaxYawDegrees);
		float pMax = Mathf.DegToRad(MaxPitchDegrees);
		_targetYaw = Mathf.Clamp(yaw, -yMax, yMax);
		_targetPitch = Mathf.Clamp(pitch, -pMax, pMax);
	}

	/// <summary>
	/// Helper pour Winning&#160;: résout le chapeau du peer dans
	/// <see cref="LobbyState.LastHats"/> et (re)construit le penguin. Tolérant
	/// au peer inconnu&#160;: chapeau par défaut. Idempotent.
	/// </summary>
	public void Show(int peerId)
	{
		string hatId = HatRegistry.DefaultHatId;
		var hats = LobbyState.LastHats;
		if (hats is not null && hats.TryGetValue(peerId, out var id) && !string.IsNullOrEmpty(id))
			hatId = id;
		if (_character is null) SpawnCharacter(hatId);
		else SetHat(hatId);
	}

	/// <summary>Libère le penguin. Utilisé par WinningSlide2 quand un slot devient vide.</summary>
	public void Clear() => ClearCharacter();

	/// <summary>
	/// Branche le suivi souris sur le SubViewportContainer parent. Sans effet
	/// hors modes <see cref="InteractionMode.MouseLook"/> et
	/// <see cref="InteractionMode.DragOrbit"/>. À appeler une seule fois après
	/// que la SubViewport ait son container hôte.
	/// </summary>
	public void BindMouseInput(SubViewportContainer container)
	{
		if (Mode != InteractionMode.MouseLook && Mode != InteractionMode.DragOrbit) return;
		_inputSource = container;
		container.GuiInput += OnContainerInput;
	}

	private void OnContainerInput(InputEvent @event)
	{
		if (_inputSource is null) return;

		if (Mode == InteractionMode.MouseLook && @event is InputEventMouseMotion hover)
		{
			var size = _inputSource.Size;
			if (size.X <= 0 || size.Y <= 0) return;
			// Coordonnées locales du container → NDC [-1,1]. nx>0 = souris à
			// droite ⇒ on veut que le penguin tourne vers la droite ⇒ yaw
			// négatif (le penguin regarde la caméra à -Z, yaw positif
			// l'éloignerait de notre droite à l'écran).
			float nx = Mathf.Clamp(hover.Position.X / size.X * 2f - 1f, -1f, 1f);
			float ny = Mathf.Clamp(hover.Position.Y / size.Y * 2f - 1f, -1f, 1f);
			SetHeadTarget(
				-nx * Mathf.DegToRad(MaxYawDegrees),
				 ny * Mathf.DegToRad(MaxPitchDegrees));
			EmitSignal(SignalName.HeadTargetChanged, _targetYaw, _targetPitch);
			return;
		}

		if (Mode == InteractionMode.DragOrbit)
		{
			if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				_isDragging = mb.Pressed;
				if (_isDragging) _lastDragPos = mb.Position;
			}
			else if (@event is InputEventMouseMotion mm && _isDragging)
			{
				Vector2 d = mm.Position - _lastDragPos;
				_lastDragPos = mm.Position;
				// Yaw accumule sans bornes — l'utilisateur doit pouvoir tourner
				// 360° pour inspecter. Pitch reste clampé pour ne pas mettre la
				// tête sur les épaules en arrière.
				_targetYaw -= d.X * DragYawPerPixel;
				float pMax = Mathf.DegToRad(MaxPitchDegrees);
				_targetPitch = Mathf.Clamp(_targetPitch + d.Y * DragPitchPerPixel, -pMax, pMax);
				EmitSignal(SignalName.HeadTargetChanged, _targetYaw, _targetPitch);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_character is null || Mode == InteractionMode.Static) return;
		// Suivi exponentiel vers la cible. HeadFollowSpeed est en "1/s logique"
		// (10 ≈ 90&#160;% atteint en ~0.25s); le clamp empêche un dt énorme de
		// faire dépasser la cible quand le focus revient sur la fenêtre.
		float t = Mathf.Clamp((float)delta * HeadFollowSpeed, 0f, 1f);
		_currentYaw = Mathf.Lerp(_currentYaw, _targetYaw, t);
		_currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, t);
		_character.Rotation = new Vector3(0f, -_currentYaw, 0f); //reversed
		if (_character.PhysicsSkelton != null)
			_character.PhysicsSkelton.HeadAngle = -_currentPitch; //reversed
	}

	public override void _ExitTree() => ClearCharacter();

	private void ClearCharacter()
	{
		if (_character is null) return;
		if (IsInstanceValid(_character)) _character.QueueFree();
		_character = null;
	}

	// Mirror de Player._SpawnHat (Player.cs:226-249). On vise la PhysicsSkeleton
	// — qui est elle-même un Skeleton3D — pour rester cohérent avec le rig en
	// jeu&#160;: si on changeait l'offset/scale d'un chapeau, Player et Preview
	// le verraient pareillement.
	private static void AttachHat(Character character, string hatId)
	{
		var hatDef = HatRegistry.Get(hatId) ?? HatRegistry.Get(HatRegistry.DefaultHatId);
		if (hatDef is null || string.IsNullOrEmpty(hatDef.ScenePath)) return;
		if (character.PhysicsSkelton is null) return;

		var hatScene = ResourceLoader.Load<PackedScene>(hatDef.ScenePath);
		if (hatScene is null)
		{
			GD.PrintErr($"[Preview] Scène chapeau introuvable&#160;: {hatDef.ScenePath}");
			return;
		}

		var attachment = new BoneAttachment3D { Name = "HatAttachment", BoneName = "Head.001" };
		character.PhysicsSkelton.AddChild(attachment);
		var hatNode = hatScene.Instantiate<Node3D>();
		hatNode.Position = HatRegistry.GlobalOffset + hatDef.Offset;
		hatNode.Scale = HatRegistry.GlobalScale * hatDef.Scale;
		attachment.AddChild(hatNode);
	}
}
