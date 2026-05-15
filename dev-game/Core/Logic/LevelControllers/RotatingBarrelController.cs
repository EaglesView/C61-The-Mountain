using Godot;
using Core.Shared.StateMachine;
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

	public string DisplayName => "Rotating Barrel";
	public PackedScene Level => CurrentLevel;
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);

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
		_normalTimer = new TimeElapsedCondition<Phase>(NormalDuration);
		_hardTimer = new TimeElapsedCondition<Phase>(HardDuration);

		_fsm = new StateMachine<Phase>(Phase.Idle, OnPhaseEnter, null);
		_fsm.When(Phase.Idle, new TimeElapsedCondition<Phase>(WaitingDuration), Phase.Normal);
		_fsm.When(Phase.Normal, _normalTimer, Phase.Hard);
		_fsm.When(Phase.Hard, _hardTimer, Phase.Done);

		// onEnter n'est pas invoqué pour l'état initial par la machine&#160;: on déclenche
		// le setup d'Idle nous-mêmes (cf. <see cref="StateMachine{TState}"/>).
		OnPhaseEnter(Phase.Idle);
	}

	public void Tick(float InDelta) => _fsm?.Tick(InDelta);

	public void Exit()
	{
		_activeTween?.Kill();
		_activeTween = null;
		_fsm = null;
	}

	public void LoadLevel() { }
	public void GetRelevantStats() { }

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
