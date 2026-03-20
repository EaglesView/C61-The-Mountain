using Godot;
namespace Utils;

public static class CharacterUtils
{
    public enum MovementState
    {
        Walking,
        Running,
        Idle,
        Sliding
    }

    public enum EmoteState {
        Pointing,
        ArmsUp,
        ShowSign,
        None
    }

    public static MovementState GetMovementStateFromMovement(Vector3 InMoveDir, Vector3 InAimDir)
    {

        if (InMoveDir == Vector3.Zero) return MovementState.Idle;
        return MovementState.Walking;
    }

    public static void PlayAnimationFromMovement(MovementState InNew,AnimationPlayer InAnimationPlayer){
        switch (InNew){
            case MovementState.Walking:
            InAnimationPlayer.Play("WalkAction_001");
            break;
            case MovementState.Running:
            // Présentement, utilises la même animation, mais l'accélère
            // cela pourrais changer, case still valide
            InAnimationPlayer.Play("WalkAction_001");
            break;
            case MovementState.Idle:
            InAnimationPlayer.Play("Idle_001");
            break;
            case MovementState.Sliding:
            InAnimationPlayer.Play("SlideAction_001");
            break;
        }
    }

    /// <summary>
    /// Calcule la force vectorielle 3D d'un ressort amorti selon la loi de Hooke étendue.
    /// La force résultante est : F = -k·x - b·v, où k est la raideur, x le déplacement,
    /// b le coefficient d'amortissement, et v la vélocité actuelle.
    /// </summary>
    /// <param name="InDisplacement">
    /// Le vecteur de déplacement (position actuelle - position d'équilibre).
    /// </param>
    /// <param name="InCurrentVelocity">
    /// La vélocité actuelle de l'objet, utilisée pour calculer la force d'amortissement.
    /// </param>
    /// <param name="InStiffness">
    /// La raideur du ressort (constante k). Une valeur élevée donne un ressort rigide.
    /// </param>
    /// <param name="InDamping">
    /// Le coefficient d'amortissement (constante b). Contrôle la vitesse à laquelle
    /// les oscillations s'atténuent, indépendamment de la raideur.
    /// </param>
    public static Vector3 HookesLaw(Vector3 InDisplacement, Vector3 InCurrentVelocity, float InStiffness, float InDamping)
    {
        return (InStiffness * InDisplacement) - (InDamping * InCurrentVelocity);
    }
}
