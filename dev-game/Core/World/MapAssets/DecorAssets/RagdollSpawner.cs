using Godot;
using System;
using System.Collections.Generic;
using static Utils.CharacterUtils;
public partial class RagdollSpawner : Node3D
{
	/// <summary>Scène <c>Character.tscn</c> à instancier en mode ragdoll permanent.</summary>
	[Export] public PackedScene RagdollScene;
	[Export] public Marker3D ThrowDirection;
	[Export] public float LaunchForce = 10.0f;
	[Export] public float SpawnInterval = 2.0f;
	[Export] public float DespawnHeight = -20.0f;
	/// <summary>Durée de vie maximale (s) — filet de sécurité si le ragdoll
	/// reste coincé au-dessus de <see cref="DespawnHeight"/>.</summary>
	[Export] public float MaxLifetime = 10.0f;
	[Export] public bool AutoSpawn = true;
	/// <summary>Décalage latéral max (mètres) appliqué à la cible avant
	/// le calcul de la direction. Sway gauche/droite uniquement.</summary>
	[Export] public float LateralSpread = 1.0f;
	private float _spawnAccumulator = 0f;
	private readonly List<(Character Char, ulong SpawnMsec)> _liveRagdolls = new();

	/// <summary>
	/// Décale la cible perpendiculairement à la direction de tir, dans le plan
	/// horizontal, par une quantité aléatoire dans [-LateralSpread, +LateralSpread].
	/// </summary>
	private Vector3 GetSwayedTarget(Vector3 InOrigin, Vector3 InTarget)
	{
		if (LateralSpread <= 0f) return InTarget;
		Vector3 direction = (InTarget - InOrigin).Normalized();
		Vector3 right = direction.Cross(Vector3.Up);
		if (right.LengthSquared() < 1e-6f) return InTarget; // tir vertical, pas de "côté"
		right = right.Normalized();
		float offset = (GD.Randf() * 2f - 1f) * LateralSpread;
		return InTarget + right * offset;
	}

	/// <summary>
	/// Instancie un Character, le force en ragdoll permanent et l'éjecte
	/// vers <see cref="ThrowDirection"/>. Aucun input ni réseau n'est branché.
	/// </summary>
	public void FireRagdoll()
	{
		if (RagdollScene == null)
		{
			GD.PushWarning($"{Name}: RagdollScene non assigné.");
			return;
		}
		if (ThrowDirection == null)
		{
			GD.PushWarning($"{Name}: ThrowDirection non assigné.");
			return;
		}

		Vector3 spawnPos = GlobalPosition;
		Character character = RagdollScene.Instantiate<Character>();

		// Empêche la FSM de téléporter le perso à SpawnPosition quand il tombe.
		character.FallLimit = -1_000_000f;
		character.SpawnPosition = spawnPos;

		// Pré-positionne via Position locale AVANT AddChild : sinon
		// PhysicalBonesStartSimulation (dans PhysicsSkeleton._Ready) fige les
		// PhysicalBone3D à l'origine du monde et ils tombent depuis (0,0,0).
		// Local == global ici car GetTree().Root a une transform identité.
		character.Position = spawnPos;
		GetTree().Root.AddChild(character);

		// Force l'état ragdoll dès l'entrée dans l'arbre. EnterState désactive
		// la capsule et lève PhysicsSkelton.IsRagdoll, donc plus de FSM active.
		character.TransitionTo(CharacterState.Ragdoll);
		Vector3 swayedTarget = GetSwayedTarget(spawnPos, ThrowDirection.GlobalPosition);
		Vector3 direction = (swayedTarget - spawnPos).Normalized();
		character.PhysicsSkelton.ApplyRagdollKick(direction * LaunchForce);

		_liveRagdolls.Add((character, Time.GetTicksMsec()));
	}

	public override void _PhysicsProcess(double InDelta)
	{
		// Spawn périodique
		if (AutoSpawn && SpawnInterval > 0f)
		{
			_spawnAccumulator += (float)InDelta;
			if (_spawnAccumulator >= SpawnInterval)
			{
				_spawnAccumulator = 0f;
				FireRagdoll();
			}
		}

		// Despawn sous le seuil de hauteur OU au-delà de MaxLifetime (filet de
		// sécurité). On lit la position de la colonne physique : le
		// CharacterBody3D ne bouge pas en ragdoll, seuls les os.
		ulong now = Time.GetTicksMsec();
		ulong maxAgeMsec = (ulong)Mathf.Max(0f, MaxLifetime * 1000f);
		for (int i = _liveRagdolls.Count - 1; i >= 0; i--)
		{
			var (c, spawnMsec) = _liveRagdolls[i];
			if (!IsInstanceValid(c) || !c.IsInsideTree())
			{
				_liveRagdolls.RemoveAt(i);
				continue;
			}
			float y = IsInstanceValid(c.PhysicsSkelton)
				? c.PhysicsSkelton.GetSpinePhysicsWorldPosition().Y
				: c.GlobalPosition.Y;
			bool tooLow = y < DespawnHeight;
			bool tooOld = MaxLifetime > 0f && (now - spawnMsec) >= maxAgeMsec;
			if (tooLow || tooOld)
			{
				_liveRagdolls.RemoveAt(i);
				c.QueueFree();
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_accept")) // Spacebar / enter
		{
			FireRagdoll();
		}
	}
}
