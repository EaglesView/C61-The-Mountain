using Godot;
using Core.Network.Rooms;

/// <summary>
/// Slot d'affichage 3D dans une SubViewport de l'écran Winning. Sert pour le
/// gagnant principal (slide 1) et les sous-gagnants (slide 2). Le node lui-même
/// reste vide&#160;: la méthode <see cref="Show"/> instancie dynamiquement un
/// penguin frais comme <b>enfant de la SubViewport parente</b> (sibling de ce
/// node) avec le chapeau du peer demandé attaché au bone <c>Head.001</c> de la
/// squelette animée. La SubViewport conserve sa caméra et ses lumières&#160;:
/// seul le contenu «&#160;personnage&#160;» est reconstruit par appel.
///
/// On vise volontairement la squelette <i>animée</i> (<c>Armature/Skeleton3D</c>)
/// et non le rig physique comme <see cref="Player._SpawnHat"/> le fait en jeu&#160;:
/// le penguin de l'UI n'a pas de PhysicsSkeleton — il n'est là que pour être
/// rendu, et le rest pose suffit pour positionner le chapeau correctement.
/// </summary>
public partial class WinnerDisplay : Node3D
{
	/// <summary>
	/// Scène du penguin instanciée à chaque <see cref="Show"/>. Laissée en
	/// export pour permettre de la swap depuis l'éditeur si besoin (variantes
	/// d'animation, mannequin custom…). Fallback&#160;: la scène standard via
	/// ResourceLoader.
	/// </summary>
	[Export] private PackedScene _penguinSceneAsset;

	private const string PenguinScenePath = "res://Core/World/CharacterModel/Physics/penguin_01.tscn";
	private const string SkeletonPath = "Armature/Skeleton3D";
	private const string HeadBoneName = "Head.001";

	private Node3D _currentPenguin;

	/// <summary>
	/// Reconstruit le penguin du peer dans la SubViewport parente, avec son
	/// chapeau résolu depuis <see cref="LobbyState.LastHats"/>. Idempotent&#160;:
	/// un appel ultérieur libère l'instance précédente avant d'en créer une
	/// nouvelle.
	/// </summary>
	public void Show(int InPeerId)
	{
		ClearCurrent();

		var parent = GetParent();
		if (parent is null)
		{
			GD.PrintErr("[WinnerDisplay] Aucune SubViewport parente — Show ignoré.");
			return;
		}

		var penguinScene = _penguinSceneAsset ?? ResourceLoader.Load<PackedScene>(PenguinScenePath);
		if (penguinScene is null)
		{
			GD.PrintErr($"[WinnerDisplay] Scène penguin introuvable&#160;: {PenguinScenePath}");
			return;
		}

		var penguin = penguinScene.Instantiate<Node3D>();
		parent.AddChild(penguin);
		_currentPenguin = penguin;

		string hatId = ResolveHatId(InPeerId);
		AttachHat(penguin, hatId);
	}

	/// <summary>Libère l'instance courante et son chapeau. Sans effet si rien n'a été affiché.</summary>
	public void Clear() => ClearCurrent();

	public override void _ExitTree() => ClearCurrent();

	private void ClearCurrent()
	{
		if (_currentPenguin is null) return;
		if (IsInstanceValid(_currentPenguin)) _currentPenguin.QueueFree();
		_currentPenguin = null;
	}

	private static string ResolveHatId(int InPeerId)
	{
		var hats = LobbyState.LastHats;
		if (hats is not null && hats.TryGetValue(InPeerId, out var id) && !string.IsNullOrEmpty(id))
			return id;
		return HatRegistry.DefaultHatId;
	}

	private static void AttachHat(Node3D InPenguinRoot, string InHatId)
	{
		var hatDef = HatRegistry.Get(InHatId) ?? HatRegistry.Get(HatRegistry.DefaultHatId);
		if (hatDef is null || string.IsNullOrEmpty(hatDef.ScenePath)) return;

		var skeleton = InPenguinRoot.GetNodeOrNull<Skeleton3D>(SkeletonPath);
		if (skeleton is null)
		{
			GD.PrintErr($"[WinnerDisplay] Skeleton3D introuvable à '{SkeletonPath}' — chapeau ignoré.");
			return;
		}

		var hatScene = ResourceLoader.Load<PackedScene>(hatDef.ScenePath);
		if (hatScene is null)
		{
			GD.PrintErr($"[WinnerDisplay] Scène chapeau introuvable&#160;: {hatDef.ScenePath}");
			return;
		}

		var attachment = new BoneAttachment3D
		{
			Name = "HatAttachment",
			BoneName = HeadBoneName,
		};
		skeleton.AddChild(attachment);
		attachment.AddChild(hatScene.Instantiate<Node3D>());
	}
}
