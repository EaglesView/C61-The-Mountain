using Godot;
using System.Collections.Generic;
using Core.Shared.StateMachine;
using Core.Stats;
using Core.Stats.Conditions;

namespace Core.World;

public sealed partial class SoccerModeController : Node3D, IPhase, IGameMode
{
	private enum Phase { Idle, PostGoal, Done }

	[Export] private PackedScene? CurrentLevel;
	[Export] private NodePath PlayerSpawnerPath = "PlayerSpawner";
	[Export] private bool GoalZoneAIsBlue = true;
	[Export] private int GoalsToWin = 3;
	[Export] private NodePath ScoreLabelPath = "HUD/ScoreLabel";
	[Export] private NodePath CountdownLabelPath = "HUD/CountdownLabel";

	private bool _entered;
	private StateMachine<Phase>? _fsm;
	private Node? _playersContainer;
	private PlayerSpawner? _playerSpawner;
	private RigidBody3D? _ball;
	private Vector3 _ballInitialPos;
	private Label? _scoreLabel;
	private Label? _countdownLabel;

	private int _scoreBlue;
	private int _scoreRed;
	private float _postGoalTimer;
	private const float PostGoalDelay = 3.0f;

	// Mirror serveur→clients de l'attribution d'équipe. Toujours mis à jour via
	// NetSetPlayerTeam pour que tous les pairs aient le même dict, condition
	// nécessaire au respawn cohérent et à la balance d'équipes côté serveur.
	private readonly Dictionary<int, int> _teamsByPeer = new();

	private static readonly IReadOnlyList<WeightedCondition> _subwinningConditions = new[]
	{
		new WeightedCondition(new LastSurvivorCondition(), 1.0f),
	};

	public string DisplayName => "Soccer Dev";
	public PackedScene Level => CurrentLevel ?? ResourceLoader.Load<PackedScene>("res://Core/Dev/soccer_dev.tscn");
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);
	public float RemainingSeconds => 0f;
	public IReadOnlyList<WeightedCondition> SubwinningConditions => _subwinningConditions;

	public override void _Ready()
	{
		_scoreLabel = GetNodeOrNull<Label>(ScoreLabelPath);
		_countdownLabel = GetNodeOrNull<Label>(CountdownLabelPath);
		_ball = GetNodeOrNull<RigidBody3D>("SoccerBall");
		if (_ball is not null)
			_ballInitialPos = _ball.GlobalPosition;

		if (Multiplayer.GetPeers().Length == 0)
			Enter();
	}

	public override void _Process(double delta)
	{
		if (Multiplayer.GetPeers().Length == 0)
			Tick((float)delta);
	}

	public void Enter()
	{
		if (_entered) return;
		_entered = true;
		_scoreBlue = 0;
		_scoreRed = 0;
		_teamsByPeer.Clear();

		_playerSpawner = GetNodeOrNull<PlayerSpawner>(PlayerSpawnerPath);
		BindPlayersContainer();
		UpdateScoreUI();

		_fsm = new StateMachine<Phase>(Phase.Idle, OnPhaseEnter, null);
		// La FSM n'invoque pas OnEnter pour l'état initial (cf. StateMachine.cs:44-47),
		// donc on déclenche manuellement le setup de Idle — même pattern qu'ObbyController:124.
		OnPhaseEnter(Phase.Idle);
	}

	public void Tick(float InDelta)
	{
		if (!_entered || _fsm is null) return;

		_fsm.Tick(InDelta);

		if (_fsm.Is(Phase.PostGoal))
		{
			_postGoalTimer -= InDelta;
			int secs = Mathf.CeilToInt(Mathf.Max(0f, _postGoalTimer));
			if (_countdownLabel is not null)
				_countdownLabel.Text = secs > 0 ? $"Reset dans {secs}..." : "!!!";

			if (_postGoalTimer <= 0.0f)
			{
				if (_countdownLabel is not null)
					_countdownLabel.Visible = false;

				// Le compteur a été armé sur tous les pairs par NetSyncGoal (CallLocal=true),
				// donc la transition locale ici reste synchrone à la frame près sur tous
				// les pairs. ResetBall() est gardé serveur-seul à l'intérieur de OnPhaseEnter.
				bool finalReached = _scoreBlue >= GoalsToWin || _scoreRed >= GoalsToWin;
				_fsm.TransitionTo(finalReached ? Phase.Done : Phase.Idle);
			}
		}
	}

	public void Exit()
	{
		if (!_entered) return;
		_entered = false;
		ClearTeamsOnPlayers();
		UnbindPlayersContainer();
		_teamsByPeer.Clear();
		_fsm = null;
	}

	public void LoadLevel() { }

	private void OnPhaseEnter(Phase InPhase)
	{
		if (InPhase == Phase.Idle)
			ResetBall();
	}

	// Appelé par GoalZone (serveur-seul, cf. GoalZone.cs). Le serveur broadcast
	// ensuite via NetSyncGoal pour appliquer le score+phase de façon cohérente
	// sur tous les pairs.
	public void OnGoalScored(int InScoringTeamId)
	{
		if (_fsm is null) return;
		if (_fsm.Is(Phase.PostGoal) || _fsm.Is(Phase.Done)) return;
		if (!Multiplayer.IsServer()) return;

		Rpc(MethodName.NetSyncGoal, InScoringTeamId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NetSyncGoal(int InScoringTeamId)
	{
		if (_fsm is null) return;
		if (_fsm.Is(Phase.PostGoal) || _fsm.Is(Phase.Done)) return;

		if (InScoringTeamId == 1)
			_scoreBlue++;
		else if (InScoringTeamId == 2)
			_scoreRed++;

		UpdateScoreUI();

		bool finalReached = _scoreBlue >= GoalsToWin || _scoreRed >= GoalsToWin;
		if (_countdownLabel is not null)
		{
			_countdownLabel.Visible = true;
			if (finalReached)
			{
				string winner = _scoreBlue >= GoalsToWin ? "BLEU" : "ROUGE";
				_countdownLabel.Text = $"VICTOIRE ÉQUIPE {winner} !";
			}
			else
			{
				string scorer = InScoringTeamId == 1 ? "BLEU" : "ROUGE";
				_countdownLabel.Text = $"BUT ! Équipe {scorer} !";
			}
		}

		_postGoalTimer = PostGoalDelay;
		_fsm.TransitionTo(Phase.PostGoal);
	}

	private void UpdateScoreUI()
	{
		if (_scoreLabel is not null)
			_scoreLabel.Text = $"Bleu  {_scoreBlue}  -  {_scoreRed}  Rouge";
	}

	private void ResetBall()
	{
		// Seul le serveur écrit l'état physique : le MultiplayerSynchronizer
		// de la balle propage ensuite position/rotation/vélocités aux clients.
		if (!Multiplayer.IsServer()) return;
		if (_ball is null) return;

		var targetPos = _ballInitialPos;
		var ballRid = _ball.GetRid();
		Callable.From(() =>
		{
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.Transform,
				new Transform3D(Basis.Identity, targetPos));
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.LinearVelocity, Vector3.Zero);
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.AngularVelocity, Vector3.Zero);
		}).CallDeferred();
	}

	private List<Player> GetAllPlayers()
	{
		var list = new List<Player>();
		if (_playersContainer is not null)
		{
			foreach (var node in _playersContainer.GetChildren())
				if (node is Player p) list.Add(p);
		}
		if (GetNodeOrNull<Player>("Player") is Player standalone)
			list.Add(standalone);
		return list;
	}

	private void ClearTeamsOnPlayers()
	{
		foreach (var player in GetAllPlayers())
		{
			player.SetTeam(0);
			if (player.HasMeta("team_id"))
				player.RemoveMeta("team_id");
		}
	}

	private void BindPlayersContainer()
	{
		_playersContainer = GetNodeOrNull("../../Players");
		if (_playersContainer is null)
		{
			if (GetNodeOrNull<Player>("Player") is Player standalonePlayer)
				TryRegisterPlayer(standalonePlayer);
			return;
		}

		foreach (var node in _playersContainer.GetChildren())
			TryRegisterPlayer(node);
		_playersContainer.ChildEnteredTree += OnPlayersChildEntered;
	}

	private void UnbindPlayersContainer()
	{
		if (_playersContainer is not null)
			_playersContainer.ChildEnteredTree -= OnPlayersChildEntered;
		_playersContainer = null;
	}

	private void OnPlayersChildEntered(Node InNode) => TryRegisterPlayer(InNode);

	private void TryRegisterPlayer(Node InNode)
	{
		if (InNode is not Player player) return;

		// Si on a déjà reçu l'équipe pour ce peer (cas typique : NetSetPlayerTeam
		// arrivé avant le spawn local du Player), on applique et on rend la main.
		if (_teamsByPeer.TryGetValue(player.PeerId, out int existingTeamId))
		{
			ApplyTeamToPlayer(player, existingTeamId);
			return;
		}

		// Hors-serveur : on attend que le serveur diffuse l'attribution via
		// NetSetPlayerTeam. Note : si un client rejoint après que le serveur a
		// déjà assigné des équipes, il ne recevra pas les NetSetPlayerTeam
		// passés — pas un problème immédiat (cf. _allPlayersReady gate dans
		// GameController). Si ça devient un, ajouter un ClientRequestTeamTable
		// AnyPeer RPC qui rejoue chaque entrée vers le sender.
		if (!Multiplayer.IsServer()) return;

		int suggestedTeamId = ResolveTeamFromSpawn(player.GlobalPosition);
		int blueCount = 0;
		int redCount = 0;
		foreach (var team in _teamsByPeer.Values)
		{
			if (team == 1) blueCount++;
			else if (team == 2) redCount++;
		}

		int teamId;
		if (blueCount < redCount)
			teamId = 1;
		else if (redCount < blueCount)
			teamId = 2;
		else
			teamId = suggestedTeamId;

		// CallLocal=true : le serveur applique l'équipe via le même chemin que
		// les clients, ce qui supprime le besoin d'une branche locale ici.
		Rpc(MethodName.NetSetPlayerTeam, player.PeerId, teamId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NetSetPlayerTeam(int InPeerId, int InTeamId)
	{
		_teamsByPeer[InPeerId] = InTeamId;
		foreach (var p in GetAllPlayers())
		{
			if (p.PeerId == InPeerId)
			{
				ApplyTeamToPlayer(p, InTeamId);
				break;
			}
		}
	}

	private void ApplyTeamToPlayer(Player InPlayer, int InTeamId)
	{
		InPlayer.SetTeam(InTeamId);
		InPlayer.SetMeta("team_id", InTeamId);
	}

	private int ResolveTeamFromSpawn(Vector3 InPos)
	{
		if (_playerSpawner is null)
			return InPos.Z < 0.0f ? 1 : 2;

		var points = _playerSpawner.GetSpawnPoints();
		if (points.Count == 0)
			return InPos.Z < 0.0f ? 1 : 2;

		int bestIndex = 0;
		float bestDist = float.MaxValue;
		for (int i = 0; i < points.Count; i++)
		{
			float d = points[i].GlobalPosition.DistanceSquaredTo(InPos);
			if (d < bestDist)
			{
				bestDist = d;
				bestIndex = i;
			}
		}

		int half = points.Count / 2;
		if (GoalZoneAIsBlue)
			return bestIndex < half ? 1 : 2;
		return bestIndex < half ? 2 : 1;
	}

}
