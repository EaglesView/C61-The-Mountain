using System;
using Godot;

public partial class Eolienne : Node3D
{
	public enum EAxe { X, Y, Z }

	[Export] public float VitesseRotation = 1.0f;
	[Export] public EAxe AxeRotation = EAxe.X;
	[Export] public NodePath PaleChemin = "PALE";
	
	// Randomization settings
	[Export] public bool RandomizeInitialRotation = true;
	[Export] public float VariabiliteVitesse = 0.2f; // Amount of speed fluctuation

	private Node3D _pale;
	private RandomNumberGenerator _rng = new RandomNumberGenerator();
	private float _currentSpeed;

	public override void _Ready()
	{
		_pale = GetNodeOrNull<Node3D>(PaleChemin);
		if (_pale == null)
		{
			GD.PushWarning($"Eolienne : noeud PALE introuvable.");
			return;
		}

		_rng.Randomize(); // Seed the RNG
		_currentSpeed = VitesseRotation;

		if (RandomizeInitialRotation)
		{
			// Randomize starting angle between 0 and 2*PI
			float angleDepart = _rng.RandfRange(0, Mathf.Tau);
			AppliquerRotation(angleDepart);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Optional: Smoothly vary the speed to simulate wind gusting
		// Use a small probability or Perlin noise for more natural transitions
		if (_rng.Randf() < 0.01f) 
		{
			_currentSpeed = VitesseRotation + _rng.RandfRange(-VariabiliteVitesse, VariabiliteVitesse);
		}

		FaireTournerPales((float)delta);
	}

	private void FaireTournerPales(float InDelta)
	{
		if (_pale == null) return;
		float angle = _currentSpeed * InDelta;
		AppliquerRotation(angle);
	}

	private void AppliquerRotation(float angle)
	{
		switch (AxeRotation)
		{
			case EAxe.X: _pale.RotateX(angle); break;
			case EAxe.Y: _pale.RotateY(angle); break;
			case EAxe.Z: _pale.RotateZ(angle); break;
		}
	}
}
