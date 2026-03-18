using Godot;
namespace Utils;

public static class CharacterUtils
{
    public static Vector3 HookesLaw(Vector3 InDisplacement, Vector3 InCurrentVelocity, float InStiffness, float InDamping)
    {
        return (InStiffness * InDisplacement) - (InDamping * InCurrentVelocity);
    }
}
