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

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;

		// Côté client : on écoute les snapshots autoritaires du serveur. Le
		// guard IsServer() est true en offline, donc le standalone ne s'abonne
		// pas — pas de packet à recevoir de toute façon.
		var net = NetworkManager.Instance;
		if (net is not null && !Multiplayer.IsServer())
			net.BallStateReceived += OnBallStateReceived;
	}

	public override void _ExitTree()
	{
		var net = NetworkManager.Instance;
		if (net is not null)
			net.BallStateReceived -= OnBallStateReceived;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Push 20 Hz côté serveur. NetworkManager filtre en interne via Role,
		// mais on évite l'overhead de l'appel si on n'est pas autorité.
		if (!Multiplayer.IsServer()) return;
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
	}

	private void OnBallStateReceived(BallNetState s)
	{
		// Snap autoritaire sur l'état serveur. La vélocité reçue continue de
		// piloter la physique locale entre deux paquets (interpolation
		// implicite à 20Hz). CallDeferred pour respecter le serveur de
		// physique — même contrainte que ResetBall dans SoccerModeController.
		var ballRid = GetRid();
		var pos = s.Position;
		var rot = s.Rotation;
		var lin = s.LinearVelocity;
		var ang = s.AngularVelocity;
		Callable.From(() =>
		{
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.Transform,
				new Transform3D(new Basis(rot), pos));
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.LinearVelocity, lin);
			PhysicsServer3D.BodySetState(ballRid, PhysicsServer3D.BodyState.AngularVelocity, ang);
		}).CallDeferred();
	}

	private void OnBodyEntered(Node body)
	{
		// Seul le serveur applique l'impulsion : le snapshot 20Hz propage
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
