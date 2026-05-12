using Godot;

/// <summary>
/// Root du <c>world.tscn</c> — main scene au démarrage. Sert uniquement de
/// hôte pour le <c>LoginScreen</c>; toute la logique de spawn/réseau a été
/// déplacée dans <see cref="Core.World.GameController"/>.
/// </summary>
public partial class World : Node3D
{
}
