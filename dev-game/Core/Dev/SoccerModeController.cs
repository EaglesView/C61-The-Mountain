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
	private Phase _phase = Phase.Idle;
	private Node? _playersContainer;
	private PlayerSpawner? _playerSpawner;
	private RigidBody3D? _ball;
	private Vector3 _ballInitialPos;
	private Label? _scoreLabel;
	private Label? _countdownLabel;

	private int _scoreBlue;
	private int _scoreRed;
	private bool _finalGoalReached;
	private float _postGoalTimer;
	private const float PostGoalDelay = 3.0f;

	private readonly Dictionary<int, int> _teamsByPeer = new();

	private static readonly IReadOnlyList<WeightedCondition> _subwinningConditions = new[]
	{
		new WeightedCondition(new LastSurvivorCondition(), 1.0f),
	};

	public string DisplayName => "Soccer Dev";
	public PackedScene Level => CurrentLevel ?? ResourceLoader.Load<PackedScene>("res://Core/Dev/soccer_dev.tscn");
	public bool IsDone => _phase == Phase.Done;
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
		_finalGoalReached = false;
		_phase = Phase.Idle;
		_teamsByPeer.Clear();

		_playerSpawner = GetNodeOrNull<PlayerSpawner>(PlayerSpawnerPath);
		BindPlayersContainer();
		UpdateScoreUI();
	}

	public void Tick(float InDelta)
	{
		if (!_entered) return;

		if (_phase == Phase.PostGoal)
		{
			_postGoalTimer -= InDelta;
			int secs = Mathf.CeilToInt(Mathf.Max(0f, _postGoalTimer));
			if (_countdownLabel is not null)
				_countdownLabel.Text = secs > 0 ? $"Reset dans {secs}..." : "!!!";

			if (_postGoalTimer <= 0.0f)
			{
				ResetBall();
				if (_countdownLabel is not null)
					_countdownLabel.Visible = false;
				_phase = _finalGoalReached ? Phase.Done : Phase.Idle;
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
	}

	public void LoadLevel() { }

  // claude qui a gerer ici. Tres simple quand meme mais viens de claude a 80%
	public void OnGoalScored(int InScoringTeamId)
	{
		if (_phase == Phase.PostGoal || _phase == Phase.Done) return;

		if (InScoringTeamId == 1)
			_scoreBlue++;
		else if (InScoringTeamId == 2)
			_scoreRed++;

		UpdateScoreUI();

		if (_scoreBlue >= GoalsToWin || _scoreRed >= GoalsToWin)
		{
			_finalGoalReached = true;
			string winner = _scoreBlue >= GoalsToWin ? "BLEU" : "ROUGE";
			if (_countdownLabel is not null)
			{
				_countdownLabel.Visible = true;
				_countdownLabel.Text = $"VICTOIRE ÉQUIPE {winner} !";
			}
			_phase = Phase.PostGoal;
			_postGoalTimer = PostGoalDelay;
			return;
		}

		string scorer = InScoringTeamId == 1 ? "BLEU" : "ROUGE";
		if (_countdownLabel is not null)
		{
			_countdownLabel.Visible = true;
			_countdownLabel.Text = $"BUT ! Équipe {scorer} !";
		}
		_phase = Phase.PostGoal;
		_postGoalTimer = PostGoalDelay;
	}

	private void UpdateScoreUI()
	{
		if (_scoreLabel is not null)
			_scoreLabel.Text = $"Bleu  {_scoreBlue}  -  {_scoreRed}  Rouge";
	}

	private void ResetBall()
	{
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
		if (_teamsByPeer.TryGetValue(player.PeerId, out int existingTeamId))
		{
			player.SetTeam(existingTeamId);
			player.SetMeta("team_id", existingTeamId);
			return;
		}

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

		_teamsByPeer[player.PeerId] = teamId;
		player.SetTeam(teamId);
		player.SetMeta("team_id", teamId);
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
