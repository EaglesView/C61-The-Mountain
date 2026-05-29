using Godot;

namespace Core.World;

public partial class BoatAutoAnim : Node3D
{
	[Export] private NodePath AnimationPlayerPath;
	[Export] private string AnimationName = "";

	public override void _Ready()
	{
		var animPlayer = ResolveAnimationPlayer();
		if (animPlayer is null)
		{
			GD.PushWarning("[BoatAutoAnim] AnimationPlayer introuvable sous la scene du bateau.");
			return;
		}

		string clip = AnimationName;
		if (string.IsNullOrEmpty(clip))
		{
			var list = animPlayer.GetAnimationList();
			if (list.Length == 0)
			{
				GD.PushWarning("[BoatAutoAnim] Aucune animation disponible sur l'AnimationPlayer.");
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
