using Godot;
using System;
using System.Collections.Generic;
using Core.Shared.StateMachine;
using Core.Stats;
using Core.Stats.Conditions;
using Utils;
namespace Core.World;

public sealed partial class FallingTilesController : Node3D, IPhase, IGameMode
{
	[Export] public PackedScene CurrentLevel;

	private enum Phase { Idle, Active, Done }
	private StateMachine<Phase>? _fsm;
	private Area3D? _deathZone;
	private bool _tilesActivated;

	// Suivi des joueurs vivants pour la condition «&#160;last penguin standing&#160;».
	// Les joueurs sont enregistrés à la volée quand ils apparaissent sous
	// <c>map_container/Players</c> (spawn par MultiplayerSpawner ou SpawnOffline),
	// et retirés sur l'évènement <see cref="Character.Died"/> — qui est rejoué sur
	// tous les peers via <c>NetApplyDeath</c>, donc la fenêtre converge.
	private readonly HashSet<int> _alivePeers = new();
	private int _deathCount;
	private Node? _playersContainer;

	/// <summary>
	/// Conditions évaluées en fin de partie pour ce mode. Falling-tiles privilégie
	/// l'ordre des morts (FastestToDie poids fort) et conserve LastSurvivor comme
	/// arbitre principal. Les autres conditions sont là pour les sous-gagnants.
	/// </summary>
	private static readonly IReadOnlyList<WeightedCondition> _subwinningConditions = new[]
	{
		new WeightedCondition(new LastSurvivorCondition(), 1.0f),
		new WeightedCondition(new FastestToDieCondition(), 1.5f),
		new WeightedCondition(new MostJumpsCondition(), 0.5f),
		new WeightedCondition(new MostRagdolledCondition(), 0.3f),
	};

	public string DisplayName => "Falling Tiles";
	public PackedScene Level => CurrentLevel!;
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);
	// Mode piloté par condition de victoire&#160;: pas de chrono à afficher.
	public float RemainingSeconds => 0f;
	public IReadOnlyList<WeightedCondition> SubwinningConditions => _subwinningConditions;

	public override void _Ready()
	{

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
		// Idempotent&#160;: Enter peut être rappelé (offline, le level _Ready amorce la
		// FSM puis GameController la relance à l'entrée en Playing). On débranche
		// d'abord les abonnements de l'éventuelle session précédente.
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		_tilesActivated = false;
		BindPlayersContainer();
		_fsm = new StateMachine<Phase>(Phase.Idle);
		_fsm.When(Phase.Idle, new TimeElapsedCondition<Phase>(5f), Phase.Active);
		_fsm.When(Phase.Active, new PredicateCondition<Phase>(() => CheckWinCondition()), Phase.Done);
	}

	public void Tick(float InDelta)
	{
		_fsm?.Tick(InDelta);
		if (!_tilesActivated && _fsm != null && _fsm.Is(Phase.Active))
		{
			_tilesActivated = true;
			GetNodeOrNull<TileGrid>("TileGrid")?.Activate();
		}
	}

	public void Exit()
	{
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		_fsm = null;
	}

	public void LoadLevel() { }

	/// <summary>
	/// Localise le conteneur <c>map_container/Players</c> (sibling de notre slot
	/// <c>Map</c>) et abonne les <see cref="Character.Died"/> des joueurs déjà
	/// présents&#160;; branche <see cref="OnPlayersChildEntered"/> pour les spawns
	/// ultérieurs. Tolère un parent absent (scène ouverte en standalone).
	/// </summary>
	private void BindPlayersContainer()
	{
		_playersContainer = GetNodeOrNull("../../Players");
		if (_playersContainer is null) return;
		foreach (var node in _playersContainer.GetChildren()) TryRegisterPlayer(node);
		_playersContainer.ChildEnteredTree += OnPlayersChildEntered;
	}

	private void UnbindPlayersContainer()
	{
		if (_playersContainer is null) return;
		_playersContainer.ChildEnteredTree -= OnPlayersChildEntered;
		foreach (var node in _playersContainer.GetChildren())
			if (node is Player p) p.Died -= OnPlayerDied;
		_playersContainer = null;
	}

	private void OnPlayersChildEntered(Node InNode) => TryRegisterPlayer(InNode);

	private void TryRegisterPlayer(Node InNode)
	{
		if (InNode is not Player p) return;
		if (!_alivePeers.Add(p.PeerId)) return;
		p.Died += OnPlayerDied;
	}

	private void OnPlayerDied(int InPeerId, CharacterUtils.DeathReason _)
	{
		if (_alivePeers.Remove(InPeerId)) _deathCount++;
	}

	/// <summary>
	/// Fin du round dès qu'au moins une mort a été enregistrée ET qu'il reste
	/// au plus un joueur vivant&#160;: couvre le mode solo (la seule mort
	/// déclenche la fin) comme le multijoueur (dernier pingouin debout).
	/// </summary>
	private bool CheckWinCondition() => _deathCount > 0 && _alivePeers.Count <= 1;
}
