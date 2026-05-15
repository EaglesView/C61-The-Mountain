using Godot;
using Utils;

/// <summary>
/// Surface d'eau&#160;: tout personnage qui touche l'Area3D enfant meurt par noyade.
/// La détection se fait par <c>body_entered</c>&#160;; seul l'authority du
/// <see cref="Character"/> déclenche la mort, ce qui évite les doublons côté
/// serveur dédié et côté répliques distantes (pilotées par snapshots).
/// </summary>
public partial class Water : MeshInstance3D
{
	[Export] public Area3D? WaterArea;

	private static Character? _FindCharacter(Node? InNode)
	{
		while (InNode != null)
		{
			if (InNode is Character c) return c;
			InNode = InNode.GetParent();
		}
		return null;
	}

	private void _OnBodyEntered(Node InBody)
	{
		Character? character = _FindCharacter(InBody);
		if (character is null) return;
		// Seul l'authority déclenche la mort. Sur les autres pairs (serveur dédié
		// inclus), la réplique est pilotée par les snapshots&#160;: c'est l'authority
		// qui a la vraie position, donc c'est lui qui décide.
		if (!character.IsMultiplayerAuthority()) return;
		if (character is Player player)
			player.RequestDeath(CharacterUtils.DeathReason.Drowned);
		else
			character.Die(CharacterUtils.DeathReason.Drowned);
	}

	public override void _Ready()
	{
		WaterArea ??= GetNodeOrNull<Area3D>("WaterArea");
		if (WaterArea is null) return;
		WaterArea.BodyEntered += _OnBodyEntered;
	}
}
