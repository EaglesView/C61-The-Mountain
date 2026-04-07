using Godot;

public partial class BasicButtonInteractable : Interactable
{
    [Export] public Node3D? Target;

    public override void Interact(Node3D interactor)
    {
        if (Target is IActivatable activatable)
        {
            activatable.Activate();
        }
        else
        {
            GD.PrintErr("PLUG TON NODE MY GUY");
        }
    }
}
