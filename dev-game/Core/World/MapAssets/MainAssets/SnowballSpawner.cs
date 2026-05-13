using Godot;
using System.Collections.Generic;

public partial class SnowballSpawner : Node3D
{
	[ExportGroup("Spawn Settings")]
	[Export(PropertyHint.Range, "0.1f,30.0f,0.1f,suffix:sec")] public float SpawnInterval = 2.0f;
	[Export(PropertyHint.Range, "1,100,1")]                     public int   MaxBalls       = 10;
	[Export(PropertyHint.Range, "1.0f,60.0f,0.1f,suffix:sec")] public float BallLifetime   = 8.0f;
	[Export]                                                     public bool  Active         = true;

	[ExportGroup("Ball Properties")]
	[Export(PropertyHint.Range, "0.1f,500.0f,0.1f,suffix:kg")]  public float BallMass   = 5.0f;
	[Export(PropertyHint.Range, "1.0f,200.0f,1.0f")]            public float ThrowForce = 18.0f;
	// Direction is in the spawner's local space — rotate the node in the editor to aim.
	[Export]                                                     public Vector3 ThrowDirection = Vector3.Forward;
	// Random cone spread in radians added to each throw (0 = perfectly straight).
	[Export(PropertyHint.Range, "0.0f,1.5f,0.01f,suffix:rad")] public float Spread = 0.0f;

	[ExportGroup("Mesh & Collision")]
	// Assign any Mesh resource (SphereMesh, BoxMesh, imported .glb mesh, etc.)
	// Leave empty to fall back to a sphere of radius BallRadius.
	[Export]                                                     public Mesh?           BallMesh          = null;
	[Export(PropertyHint.Range, "0.05f,5.0f,0.05f,suffix:m")]  public float           BallRadius        = 0.35f;
	// Assign a custom shape; leave empty to auto-generate a SphereShape3D from BallRadius.
	[Export]                                                     public Shape3D?        CollisionShape    = null;
	[Export]                                                     public PhysicsMaterial? PhysicsMaterial  = null;
	// Godot physics layers (1-based bit flags, same as the Layer/Mask fields in the inspector)
	[Export(PropertyHint.Layers3DPhysics)]                      public uint CollisionLayer = 1;
	[Export(PropertyHint.Layers3DPhysics)]                      public uint CollisionMask  = 1;

	private float _timer = 0.0f;
	private readonly Queue<RigidBody3D> _balls = new();
	private readonly RandomNumberGenerator _rng = new();

	public override void _PhysicsProcess(double delta)
	{
		if (!Active) return;
		_timer -= (float)delta;
		if (_timer > 0f) return;
		_timer = SpawnInterval;
		_SpawnBall();
	}

	private void _SpawnBall()
	{
		while (_balls.Count >= MaxBalls)
			if (_balls.TryDequeue(out var old)) old?.QueueFree();

		var ball = new RigidBody3D
		{
			Mass             = BallMass,
			PhysicsMaterialOverride = PhysicsMaterial,
			CollisionLayer   = CollisionLayer,
			CollisionMask    = CollisionMask,
		};

		var col = new CollisionShape3D();
		col.Shape = CollisionShape ?? new SphereShape3D { Radius = BallRadius };
		ball.AddChild(col);

		var meshInst = new MeshInstance3D();
		if (BallMesh != null)
		{
			meshInst.Mesh = BallMesh;
		}
		else
		{
			var fallback = new SphereMesh { Radius = BallRadius, Height = BallRadius * 2f };
			fallback.Material = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.93f, 1.0f) };
			meshInst.Mesh = fallback;
		}
		ball.AddChild(meshInst);

		// GlobalPosition n'est valide qu'une fois la balle dans l'arbre — sinon
		// get_global_transform échoue (assert !is_inside_tree()).
		GetParent().AddChild(ball);
		ball.GlobalPosition = GlobalPosition;

		Vector3 dir = (GlobalBasis * ThrowDirection.Normalized()).Normalized();
		if (Spread > 0f)
		{
			dir += new Vector3(
				_rng.RandfRange(-Spread, Spread),
				_rng.RandfRange(-Spread, Spread),
				_rng.RandfRange(-Spread, Spread)
			);
			dir = dir.Normalized();
		}
		ball.LinearVelocity = dir * ThrowForce;

		_balls.Enqueue(ball);

		var lifeTimer = GetTree().CreateTimer(BallLifetime);
		lifeTimer.Timeout += () => { if (IsInstanceValid(ball)) ball.QueueFree(); };
	}
}
