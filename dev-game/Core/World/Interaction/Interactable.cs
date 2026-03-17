using Godot;

public abstract partial class Interactable : StaticBody3D
{
    public abstract void Interact(Node3D interactor);
}
