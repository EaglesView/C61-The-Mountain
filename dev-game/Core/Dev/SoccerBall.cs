using Godot;
using Core.Network;

namespace Core.World;

public partial class SoccerBall : RigidBody3D
{
	private const float KickMultiplier = 4.0f;
	private const float MinKick = 4.0f;

	// Cadence d'envoi serveur→clients. Aligné sur NetworkManager.TickInterval
	// (1/20s) pour réutiliser la même fenêtre de bande passante que les
	// snapshots de joueurs.
	private const float ServerTickInterval = 1f / 20f;
	private float _serverTickAccum = 0f;

	// Lissage client : la dernière transform reçue du serveur. _PhysicsProcess
	// lerp la position locale vers cette cible plutôt que de la snapper, ce
	// qui étale le delta serveur/client sur la fenêtre d'un snapshot au lieu
	// d'apparaître comme un saut visible.
	private Vector3    _targetPos;
	private Quaternion _targetRot = Quaternion.Identity;
	private bool       _hasTarget;
	// Taux de convergence exponentiel&#160;: t = 1 - exp(-rate*dt). À rate=20
	// on est à ~63% en 50ms — la balle rattrape la cible dans la fenêtre d'un
	// snapshot, ce qui élimine le jitter sans introduire de retard perceptible.
	private const float SmoothingRate = 20.0f;
	// Au-delà de ce delta, on snap dur plutôt que de lerp&#160;: cas typique
	// d'un ResetBall après but, ou d'un spike réseau qui creuse un écart
	// énorme. Lerp sur 5m+ donnerait une balle qui «&#160;vole&#160;» visiblement.
	private const float HardSnapDistance = 5.0f;

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;

		var net = NetworkManager.Instance;
		if (net is not null && !Multiplayer.IsServer())
		{
			// Client&#160;: la balle est purement visuelle, sans simulation
			// locale. Sans ça, la physique du client intègre avec sa propre
			// vélocité entre deux snapshots et diverge de la trajectoire
			// serveur — chaque snapshot suivant ramène brutalement la balle
			// vers la position autoritaire, ce qui se voit comme du jitter.
			// FreezeMode=Kinematic garde la balle solide pour les collisions
			// joueur (le CharacterBody3D du joueur bute toujours dessus).
			Freeze = true;
			FreezeMode = FreezeModeEnum.Kinematic;
			net.BallStateReceived += OnBallStateReceived;
		}
	}

	public override void _ExitTree()
	{
		var net = NetworkManager.Instance;
		if (net is not null)
			net.BallStateReceived -= OnBallStateReceived;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.IsServer())
		{
			// Push 20Hz côté serveur. NetworkManager filtre en interne via
			// Role, mais on évite l'overhead de l'appel si on n'est pas
			// autorité (offline = IsServer true mais net.IsRunning false).
			var net = NetworkManager.Instance;
			if (net is null || !net.IsRunning) return;

			_serverTickAccum += (float)delta;
			if (_serverTickAccum < ServerTickInterval) return;
			_serverTickAccum -= ServerTickInterval;

			var state = new BallNetState(
				GlobalPosition,
				GlobalBasis.GetRotationQuaternion(),
				LinearVelocity,
				AngularVelocity);
			net.BroadcastBallState(state);
			return;
		}

		// Client&#160;: lissage exponentiel vers la dernière cible serveur.
		if (!_hasTarget) return;

		Vector3 current = GlobalPosition;
		float distSq = (_targetPos - current).LengthSquared();

		if (distSq > HardSnapDistance * HardSnapDistance)
		{
			// Snap dur pour reset/spike — lerp à cette échelle est visible.
			GlobalTransform = new Transform3D(new Basis(_targetRot), _targetPos);
			return;
		}

		float t = 1f - Mathf.Exp(-SmoothingRate * (float)delta);
		Vector3 newPos = current.Lerp(_targetPos, t);
		Quaternion newRot = GlobalBasis.GetRotationQuaternion().Slerp(_targetRot, t);
		GlobalTransform = new Transform3D(new Basis(newRot), newPos);
	}

	private void OnBallStateReceived(BallNetState s)
	{
		_targetPos = s.Position;
		_targetRot = s.Rotation;
		_hasTarget = true;
		// Vélocités ignorées côté client&#160;: la balle est kinematic, pas de
		// physique locale à piloter. Elles restent dans le packet pour
		// référence et usage futur (prédiction, interpolation à 2 snapshots).
	}

	private void OnBodyEntered(Node body)
	{
		// Seul le serveur applique l'impulsion&#160;: le snapshot 20Hz propage
		// ensuite la nouvelle linear/angular velocity aux clients via
		// NetworkManager.BroadcastBallState. IsServer() est true en offline,
		// donc le standalone passe.
		if (!Multiplayer.IsServer())
			return;

		if (body is not CharacterBody3D charBody)
			return;

		Vector3 dir = (GlobalPosition - charBody.GlobalPosition).Normalized();
		float speed = charBody.Velocity.Length();
		ApplyCentralImpulse(dir * Mathf.Max(speed * KickMultiplier, MinKick));
	}
}
