using Godot;

namespace Core.World;
// pas grand chose ici mais l'ia a aider aussi
public partial class GoalZone : Area3D
{
	[Export] public string goal_name = "Goal";
	[Export] public int defending_team_id = 1;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node body)
	{
		if (body.Name != "SoccerBall")
			return;

		// Authority-only: en multi, le synchronizer de la balle réplique sa
		// position sur tous les pairs, donc BodyEntered fire aussi côté clients.
		// On laisse uniquement le serveur valider et propager le but via RPC.
		// IsServer() est true en offline, donc le flow standalone passe aussi.
		if (!Multiplayer.IsServer())
			return;

		int scoringTeamId = 3 - defending_team_id;

		Node controller = GetParent();
		if (controller.HasMethod("OnGoalScored"))
			controller.Call("OnGoalScored", scoringTeamId);
	}
}
