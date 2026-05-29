using Godot;
using System;
using System.Collections.Generic;
using Core.Shared.StateMachine;
using Core.Stats;
using Core.Stats.Conditions;
using Utils;
namespace Core.World;

/// <summary>
/// Mode de jeu «&#160;Obby / Racing&#160;». Quatre phases&#160;:
/// <list type="bullet">
/// <item><c>Idle</c>&#160;: sas d'attente, rien ne bouge.</item>
/// <item><c>Normal</c> (Easy)&#160;: course classique, le volcan est muet, pas de fumée.</item>
/// <item><c>Hard</c>&#160;: le volcan crache des balles, les particules de fumée émettent
/// et le shader pulsé <c>smoke_volume</c> 0 → <see cref="SmokePulsePeak"/> → 0 en boucle.
/// Pas de timer de fin&#160;: la phase ne s'achève que sur condition de victoire.</item>
/// <item><c>Done</c>&#160;: round terminé.</item>
/// </list>
/// Conditions de fin (<see cref="CheckObbyWin"/>)&#160;: soit tous les joueurs sont morts,
/// soit <c>min(FinishThreshold, totalJoueursAuStart)</c> ont franchi la ligne d'arrivée.
/// </summary>
public sealed partial class ObbyController : Node3D, IPhase, IGameMode
{
	[Export] public PackedScene CurrentLevel;

	/// <summary>Aire 3D de la ligne d'arrivée. Si nulle au _Ready, fallback sur "GameAssets/FinishLineArea".</summary>
	[Export] public Area3D FinishLine;

	/// <summary>Durée du sas d'attente avant le début de la phase Normal.</summary>
	[Export] public float WaitingDuration = 5f;
	/// <summary>Durée de la phase Normal (course «&#160;facile&#160;» sans volcan).</summary>
	[Export] public float NormalDuration = 30f;
	/// <summary>Nombre de finisseurs requis pour clore le round. Cappé à <c>totalPlayersAtStart</c>.</summary>
	[Export] public int FinishThreshold = 3;
	/// <summary>Durée d'un cycle complet 0→peak→0 du pulse de fumée (secondes).</summary>
	[Export] public float SmokePulsePeriod = 4f;
	/// <summary>Valeur de pic du pulse <c>smoke_volume</c>. Le shader plafonne à 2.0.</summary>
	[Export] public float SmokePulsePeak = 0.5f;

	private enum Phase { Idle, Normal, Hard, Done }
	private StateMachine<Phase> _fsm = null;
	private TimeElapsedCondition<Phase> _normalTimer = null;

	private SnowballSpawner _volcano = null;
	private GpuParticles3D _smokeParticles = null;
	private MeshInstance3D _smokeMesh = null;
	private ShaderMaterial _smokeMaterial = null;
	private Tween _smokeTween = null;

	// Suivi des joueurs vivants — même pattern que les autres controllers&#160;:
	// les Players spawnés sous map_container/Players sont enregistrés à la volée
	// et retirés sur Character.Died.
	private readonly HashSet<int> _alivePeers = new();
	private int _deathCount;
	private Node _playersContainer;

	// Tracking de la ligne d'arrivée. _finishedPeers évite les double-comptes pour
	// un même peer (Area3D peut re-signaler si le joueur ressort/rerentre).
	private readonly HashSet<int> _finishedPeers = new();
	private readonly Dictionary<int, float> _finishTimeByPeer = new();
	private int _totalPlayersAtStart;

	/// <summary>
	/// Conditions évaluées en fin de partie pour ce mode. Obby récompense d'abord
	/// la vitesse à la ligne d'arrivée (poids fort), puis le dernier survivant comme
	/// arbitre&#160;; les autres conditions servent de sous-gagnants secondaires.
	/// </summary>
	private static readonly IReadOnlyList<WeightedCondition> _subwinningConditions = new[]
	{
		new WeightedCondition(new FastestToFinishLineCondition(), 1.5f),
		new WeightedCondition(new LastSurvivorCondition(), 0.8f),
		new WeightedCondition(new FastestToDieCondition(), 0.6f),
		new WeightedCondition(new MostJumpsCondition(), 0.3f),
		new WeightedCondition(new MostRagdolledCondition(), 0.3f),
	};

	public string DisplayName => "Racing";
	public PackedScene Level => CurrentLevel;
	public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);
	public IReadOnlyList<WeightedCondition> SubwinningConditions => _subwinningConditions;

	/// <summary>
	/// Temps restant avant la fin de la phase Normal&#160;: pendant <c>Idle</c>/<c>Normal</c>
	/// on renvoie le restant du timer Normal&#160;; pendant <c>Hard</c> il n'y a pas de
	/// chrono (la phase s'achève uniquement sur condition de victoire) — on renvoie 0.
	/// </summary>
	public float RemainingSeconds
	{
		get
		{
			if (_fsm is null) return NormalDuration;
			if (_fsm.Is(Phase.Idle)) return NormalDuration;
			if (_fsm.Is(Phase.Normal)) return _normalTimer?.Remaining ?? NormalDuration;
			return 0f;
		}
	}

	public void Enter()
	{
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		_finishedPeers.Clear();
		_finishTimeByPeer.Clear();
		BindPlayersContainer();

		// Snapshot après bind&#160;: en standalone (parent absent) on garde 1 pour ne
		// pas désactiver toute condition de fin sur le Player hardcodé de la scène.
		_totalPlayersAtStart = Math.Max(1, _alivePeers.Count);

		_normalTimer = new TimeElapsedCondition<Phase>(NormalDuration);

		_fsm = new StateMachine<Phase>(Phase.Idle, OnPhaseEnter, null);
		_fsm.When(Phase.Idle, new TimeElapsedCondition<Phase>(WaitingDuration), Phase.Normal);
		_fsm.When(Phase.Normal, _normalTimer, Phase.Hard);
		// Court-circuit&#160;: si la condition de victoire est déjà remplie avant la fin
		// de Normal (tous morts, ou seuil de finisseurs atteint), on saute Hard.
		_fsm.When(Phase.Normal, new PredicateCondition<Phase>(CheckObbyWin), Phase.Done);
		// Hard n'a pas de timer&#160;: seule la condition de victoire termine la phase.
		_fsm.When(Phase.Hard, new PredicateCondition<Phase>(CheckObbyWin), Phase.Done);

		// onEnter n'est pas invoqué pour l'état initial par la machine.
		OnPhaseEnter(Phase.Idle);
	}

	public void Tick(float InDelta) => _fsm?.Tick(InDelta);

	public void Exit()
	{
		_smokeTween?.Kill();
		_smokeTween = null;
		if (_volcano is not null) _volcano.Active = false;
		if (_smokeParticles is not null) { _smokeParticles.Emitting = false; _smokeParticles.Visible = false; }
		_smokeMaterial?.SetShaderParameter("smoke_volume", 0f);
		UnbindPlayersContainer();
		_alivePeers.Clear();
		_deathCount = 0;
		_finishedPeers.Clear();
		_finishTimeByPeer.Clear();
		_fsm = null;
	}

	public void LoadLevel() { }

	private void OnPhaseEnter(Phase InPhase)
	{
		switch (InPhase)
		{
			case Phase.Idle:
			case Phase.Normal:
				// Volcan + fumée muets pendant l'attente et la phase Easy.
				if (_volcano is not null) _volcano.Active = false;
				if (_smokeParticles is not null) { _smokeParticles.Emitting = false; _smokeParticles.Visible = false; }
				_smokeTween?.Kill();
				_smokeTween = null;
				_smokeMaterial?.SetShaderParameter("smoke_volume", 0f);
				break;

			case Phase.Hard:
				if (_volcano is not null) _volcano.Active = true;
				if (_smokeParticles is not null) { _smokeParticles.Visible = true; _smokeParticles.Emitting = true; }
				StartSmokePulse();
				break;

			case Phase.Done:
				if (_volcano is not null) _volcano.Active = false;
				if (_smokeParticles is not null) { _smokeParticles.Emitting = false; _smokeParticles.Visible = false; }
				_smokeTween?.Kill();
				_smokeTween = null;
				_smokeMaterial?.SetShaderParameter("smoke_volume", 0f);
				break;
		}
	}

	/// <summary>
	/// Démarre (ou redémarre) le tween en boucle 0 → <see cref="SmokePulsePeak"/> → 0
	/// du paramètre <c>smoke_volume</c>. <see cref="Tween.SetLoops()"/> sans argument
	/// = boucle infinie. Annule toujours un tween précédent pour éviter le cumul.
	/// </summary>
	private void StartSmokePulse()
	{
		if (_smokeMaterial is null) return;
		_smokeTween?.Kill();
		_smokeTween = CreateTween().SetLoops();
		float half = MathF.Max(0.05f, SmokePulsePeriod * 0.5f);
		_smokeTween.TweenMethod(
			Callable.From<float>(v => _smokeMaterial?.SetShaderParameter("smoke_volume", v)),
			0f, SmokePulsePeak, half);
		_smokeTween.TweenMethod(
			Callable.From<float>(v => _smokeMaterial?.SetShaderParameter("smoke_volume", v)),
			SmokePulsePeak, 0f, half);
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
		p.Died += OnPlayerDied;
	}

	private void OnPlayerDied(int InPeerId, CharacterUtils.DeathReason InReason)
	{
		if (_alivePeers.Remove(InPeerId)) _deathCount++;
	}

	/// <summary>
	/// Handler du signal <see cref="Area3D.BodyEntered"/> de <see cref="FinishLine"/>.
	/// Remonte la hiérarchie pour trouver un <see cref="Player"/> (les CharacterBody3D
	/// physiques sont des descendants), filtre les doublons et les morts, puis pose
	/// le temps de finition sur les stats authority via <c>RecordFinish</c>.
	/// </summary>
	private void OnFinishLineEntered(Node3D InBody)
	{
		if (_fsm is null) return;
		if (_fsm.Is(Phase.Idle) || _fsm.Is(Phase.Done)) return;

		Player p = FindPlayerAncestor(InBody);
		if (p is null) return;
		if (!_finishedPeers.Add(p.PeerId)) return;

		float t = GameController.GameElapsedSeconds;
		_finishTimeByPeer[p.PeerId] = t;
		p.RecordFinish(t);
		GD.Print($"[ObbyController] OnFinishLineEntered peer={p.PeerId} t={t:F1}s finishers={_finishedPeers.Count}/{Math.Min(FinishThreshold, _totalPlayersAtStart)}");
	}

	private static Player FindPlayerAncestor(Node InNode)
	{
		Node cur = InNode;
		while (cur is not null)
		{
			if (cur is Player p) return p;
			cur = cur.GetParent();
		}
		return null;
	}

	/// <summary>
	/// Vrai dès qu'on a atteint le seuil de finisseurs (cappé sur le nombre de
	/// joueurs au début), ou que tous les joueurs sont morts. Le cap garantit
	/// que la condition reste atteignable en solo / petit lobby.
	/// </summary>
	private bool CheckObbyWin()
	{
		if (_totalPlayersAtStart <= 0) return false;
		int threshold = Math.Min(FinishThreshold, _totalPlayersAtStart);
		if (_finishedPeers.Count >= threshold) return true;
		return _deathCount > 0 && _alivePeers.Count == 0;
	}

	public override void _Ready()
	{
		_volcano = GetNodeOrNull<SnowballSpawner>("Volcano Balls");
		if (_volcano is null) GD.PrintErr("[ObbyController] 'Volcano Balls' introuvable.");
		else _volcano.Active = false; // état initial garanti

		_smokeParticles = GetNodeOrNull<GpuParticles3D>("Volcano Balls/GPUParticles3D");
		if (_smokeParticles is not null) { _smokeParticles.Emitting = false; _smokeParticles.Visible = false; }

		_smokeMesh = GetNodeOrNull<MeshInstance3D>("Volcano Balls/MeshInstance3D");
		if (_smokeMesh is not null)
		{
			// On duplique le matériel pour ne pas muter une ressource potentiellement
			// partagée (la ShaderMaterial est un SubResource du tscn, mais on reste
			// défensif au cas où le mesh serait réutilisé ailleurs).
			var src = _smokeMesh.GetActiveMaterial(0) as ShaderMaterial;
			if (src is not null)
			{
				_smokeMaterial = (ShaderMaterial)src.Duplicate();
				_smokeMesh.MaterialOverride = _smokeMaterial;
				_smokeMaterial.SetShaderParameter("smoke_volume", 0f);
			}
		}

		if (FinishLine is null)
			FinishLine = GetNodeOrNull<Area3D>("GameAssets/FinishLineArea");
		if (FinishLine is not null) FinishLine.BodyEntered += OnFinishLineEntered;
		else GD.PrintErr("[ObbyController] FinishLine introuvable (export non assigné et 'GameAssets/FinishLineArea' absent).");

		// Cas offline / scène ouverte en standalone&#160;: démarre la FSM nous-mêmes.
		if (Multiplayer.GetPeers().Length == 0) Enter();
	}

	public override void _Process(double InDelta)
	{
		if (Multiplayer.GetPeers().Length == 0) Tick((float)InDelta);
	}
}
