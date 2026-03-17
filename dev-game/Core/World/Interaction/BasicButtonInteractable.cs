using Godot;

public partial class BasicButtonInteractable : Interactable
{

    public override void Interact(Node3D interactor)
    {
        GD.Print("pressed piton");
    }
}
