using Godot;

public partial class Eolienne : Node3D
{
	/// <summary>Axe local de rotation des pales.</summary>
	public enum EAxe { X, Y, Z }

	[Export] public float VitesseRotation = 1.0f; // radians par seconde
	[Export] public EAxe AxeRotation = EAxe.Z;
	[Export] public NodePath PaleChemin = "PALE";

	private Node3D _pale;

	/// <summary>Applique la rotation des pales autour de l'axe local choisi.</summary>
	private void FaireTournerPales(float InDelta)
	{
		if (_pale == null)
			return;

		float angle = VitesseRotation * InDelta;
		switch (AxeRotation)
		{
			case EAxe.X: _pale.RotateX(angle); break;
			case EAxe.Y: _pale.RotateY(angle); break;
			case EAxe.Z: _pale.RotateZ(angle); break;
		}
	}

	public override void _Ready()
	{
		_pale = GetNodeOrNull<Node3D>(PaleChemin);
		if (_pale == null)
			GD.PushWarning($"Eolienne : noeud PALE introuvable au chemin '{PaleChemin}'.");
	}

	public override void _PhysicsProcess(double delta)
	{
		FaireTournerPales((float)delta);
	}
}
