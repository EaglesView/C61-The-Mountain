using Godot;
using System.Collections.Generic;
using Core.Shared.StateMachine;
using Core.Stats;
using Core.Stats.Conditions;
using Utils;
namespace Core.World;

/// <summary>
/// Mode de jeu «&#160;Rotating Barrel&#160;». Calque la même structure de phases que
/// <see cref="FallingTilesController"/>&#160;:
/// <list type="bullet">
/// <item><c>Idle</c>&#160;: les joueurs se posent sur la plateforme, le baril est immobile.</item>
/// <item><c>Normal</c>&#160;: le baril tourne à sa vitesse de base.</item>
/// <item><c>Hard</c>&#160;: à mi-parcours, on accélère pour corser le tout.</item>
/// <item><c>Done</c>&#160;: fin du round, le baril ralentit jusqu'à l'arrêt.</item>
/// </list>
/// </summary>
public sealed partial class RotatingBarrelController : Node3D, IPhase, IGameMode
{
	[Export] public PackedScene CurrentLevel;

	/// <summary>Durée du sas d'attente avant que le baril commence à tourner.</summary>
	[Export] public float WaitingDuration = 5f;
	/// <summary>Durée de la phase Normal (vitesse de base).</summary>
	[Export] public float NormalDuration = 30f;
	/// <summary>Durée de la phase Hard (vitesse accélérée).</summary>
	[Export] public float HardDuration = 30f;
	/// <summary>Vitesse de rotation pendant la phase Normal (rad/s).</summary>
	[Export] public float NormalSpeed = 0.4f;
	/// <summary>Vitesse de rotation pendant la phase Hard (rad/s).</summary>
	[Export] public float HardSpeed = 1.0f;
	/// <summary>Durée du tween de changement de vitesse (secondes).</summary>
	[Export] public float SpeedTransitionDuration = 1.0f;

	private enum Phase { Idle, Normal, Hard, Done }
	private StateMachine<Phase> _fsm = null;
	private RotatingThing _barrel = null;
	private TimeElapsedCondition<Phase> _normalTimer = null;
	private TimeElapsedCondition<Phase> _hardTimer = null;
	private Tween _activeTween = null;

	// Mêmes mécaniques de suivi qu'en falling tiles&#160;: on observe les Players
	// spawnés sous map_container/Players et on décrémente <see cref="_alivePeers"/>
	// sur chaque <see cref="Character.Died"/>. Permet de couper le round dès que
	// la condition «&#160;dernier debout&#160;»/«&#160;tout le monde mort&#160;» est atteinte,
	// sans attendre la fin du timer.
	private readonly HashSet<int> _alivePeers = new();
	private int _deathCount;
	private Node _playersContainer;

	/// <summary>
	/// Conditions évaluées en fin de partie pour ce mode. Rotating-barrel récompense
	/// les longues survies sur le tonneau (LongestRagdoller + MostRagdolled poids
	/// élevés)&#160;; FastestToDie reste un secondaire informatif.
	/// </summary>
	private static readonly IReadOnlyList<WeightedCondition> _subwinningConditions = new[]
	{
		new WeightedCondition(new LastSurvivorCondition(), 1.0f),
		new WeightedCondition(new MostRagdolledCondition(), 1.5f),
		new WeightedCondition(new LongestRagdollerCondition(), 1.2f),
		new WeightedCondition(new FastestToDieCondition(), 0.8f),
		new WeightedCondition(new MostJumpsCondition(), 0.3f),
	};

	public string DisplayName => "Rotating Barrel";
	public PackedScene Level => CurrentLevel;
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);
	public IReadOnlyList<WeightedCondition> SubwinningConditions => _subwinningConditions;

	/// <summary>
	/// Temps restant avant la fin du round, somme des phases Normal et Hard.
	/// Pendant <c>Idle</c> retourne la durée totale jouable&#160;; pendant <c>Done</c>, 0.
	/// </summary>
	public float RemainingSeconds
	{
		get
		{
			if (_fsm is null) return NormalDuration + HardDuration;
			if (_fsm.Is(Phase.Idle)) return NormalDuration + HardDuration;
			if (_fsm.Is(Phase.Normal)) return (_normalTimer?.Remaining ?? NormalDuration) + HardDuration;
			if (_fsm.Is(Phase.Hard)) return _hardTimer?.Remaining ?? 0f;
			return 0f;
		}
	}

	public void Enter()
	{
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		BindPlayersContainer();

		_normalTimer = new TimeElapsedCondition<Phase>(NormalDuration);
		_hardTimer = new TimeElapsedCondition<Phase>(HardDuration);

		_fsm = new StateMachine<Phase>(Phase.Idle, OnPhaseEnter, null);
		_fsm.When(Phase.Idle, new TimeElapsedCondition<Phase>(WaitingDuration), Phase.Normal);
		_fsm.When(Phase.Normal, _normalTimer, Phase.Hard);
		_fsm.When(Phase.Hard, _hardTimer, Phase.Done);
		// Court-circuit «&#160;last penguin standing&#160;»&#160;: dès que tout le monde
		// est tombé (solo) ou qu'il ne reste plus qu'un survivant (multi), on
		// finit immédiatement le round sans attendre l'expiration des timers.
		_fsm.When(Phase.Normal, new PredicateCondition<Phase>(CheckLastStanding), Phase.Done);
		_fsm.When(Phase.Hard, new PredicateCondition<Phase>(CheckLastStanding), Phase.Done);

		// onEnter n'est pas invoqué pour l'état initial par la machine&#160;: on déclenche
		// le setup d'Idle nous-mêmes (cf. <see cref="StateMachine{TState}"/>).
		OnPhaseEnter(Phase.Idle);
	}

	public void Tick(float InDelta) => _fsm?.Tick(InDelta);

	public void Exit()
	{
		_activeTween?.Kill();
		_activeTween = null;
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		_fsm = null;
	}

	public void LoadLevel() { }

	private void OnPhaseEnter(Phase InPhase)
	{
		switch (InPhase)
		{
			case Phase.Idle: TweenBarrelSpeed(0f); break;
			case Phase.Normal: TweenBarrelSpeed(NormalSpeed); break;
			case Phase.Hard: TweenBarrelSpeed(HardSpeed); break;
			case Phase.Done: TweenBarrelSpeed(0f); break;
		}
	}

	/// <summary>
	/// Tween la propriété <c>RotationSpeed</c> du baril vers <paramref name="InTarget"/>.
	/// Annule tout tween en cours pour éviter de cumuler les transitions.
	/// </summary>
	private void TweenBarrelSpeed(float InTarget)
	{
		if (_barrel is null) return;
		_activeTween?.Kill();
		_activeTween = CreateTween();
		_activeTween.TweenProperty(_barrel, "RotationSpeed", InTarget, SpeedTransitionDuration);
	}

	/// <summary>
	/// Localise <c>map_container/Players</c> et abonne les <see cref="Character.Died"/>
	/// des joueurs déjà présents&#160;; branche aussi <see cref="OnPlayersChildEntered"/>
	/// pour les spawns ultérieurs. Tolère un parent absent (scène ouverte en standalone).
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
		GD.Print($"[RotatingBarrelController] TryRegisterPlayer peer={p.PeerId} stateAtRegister={p.GetCurrentState()} pos={p.GlobalPosition}");
		p.Died += OnPlayerDied;
	}

	private void OnPlayerDied(int InPeerId, CharacterUtils.DeathReason InReason)
	{
		GD.Print($"[RotatingBarrelController] OnPlayerDied peer={InPeerId} reason={InReason} aliveBefore={_alivePeers.Count} deathBefore={_deathCount} fsmPhase={_fsm?.Current}");
		if (_alivePeers.Remove(InPeerId)) _deathCount++;
	}

	/// <summary>
	/// Vrai dès qu'au moins une mort a eu lieu ET qu'il reste au plus un joueur
	/// vivant&#160;: couvre le solo (la seule mort termine) et le multi (dernier
	/// pingouin debout).
	/// </summary>
	private bool CheckLastStanding() => _deathCount > 0 && _alivePeers.Count <= 1;

	public override void _Ready()
	{
		_barrel = GetNodeOrNull<RotatingThing>("Map/RotatingThing");
		if (_barrel is null) GD.PrintErr("[RotatingBarrelController] 'Map/RotatingThing' introuvable.");

		// Cas offline / scène ouverte en standalone&#160;: démarre la FSM nous-mêmes.
		if (Multiplayer.GetPeers().Length == 0) Enter();
	}

	public override void _Process(double InDelta)
	{
		if (Multiplayer.GetPeers().Length == 0) Tick((float)InDelta);
	}
}
