using Godot;

namespace Core.World;

/// <summary>
/// Joue automatiquement une animation de la bouee et force sa lecture en boucle.
/// </summary>
public partial class BouerAutoAnim : Node3D
{
	[Export] private NodePath? AnimationPlayerPath;
	[Export] private string AnimationName = "";

	/// <summary>
	/// Recherche l'AnimationPlayer cible, choisit une animation et la joue en boucle.
	/// </summary>
	public override void _Ready()
	{
		var animPlayer = ResolveAnimationPlayer();
		if (animPlayer is null)
		{
			GD.PushWarning("[BouerAutoAnim] AnimationPlayer introuvable sous la scene de la bouee.");
			return;
		}

		string clip = AnimationName;
		if (string.IsNullOrEmpty(clip))
		{
			var list = animPlayer.GetAnimationList();
			if (list.Length == 0)
			{
				GD.PushWarning("[BouerAutoAnim] Aucune animation disponible sur l'AnimationPlayer.");
				return;
			}
			clip = list[0];
		}

		var anim = animPlayer.GetAnimation(clip);
		if (anim is not null && anim.LoopMode == Animation.LoopModeEnum.None)
			anim.LoopMode = Animation.LoopModeEnum.Linear;

		animPlayer.Play(clip);
	}

	private AnimationPlayer? ResolveAnimationPlayer()
	{
		if (AnimationPlayerPath != null && !AnimationPlayerPath.IsEmpty)
			return GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);

		return FindAnimationPlayer(this);
	}

	private static AnimationPlayer? FindAnimationPlayer(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is AnimationPlayer ap)
				return ap;

			var nested = FindAnimationPlayer(child);
			if (nested is not null)
				return nested;
		}
		return null;
	}
}
