/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:           CharacterUtils.cs                       |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Utilitaires pour le personnage et le skelette.   |
/// |  ---------------------------------------------------------  |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// |                                                             |
/// +==============================================================+
using Godot;
namespace Utils;

///<summary>
/// Classe Utilitaire pour le personnage, son skelette et ses animations. Comprends une
/// foule de méthodes utilisés dans le Player.cs, Character.cs et PhysicsSkeleton.cs pour
/// alléger le code et simplifier la compréhension.
/// </summary>
public static class CharacterUtils
{
    ///<summary>
    /// L'état de mouvement du personnage.
    /// </summary>
    public enum MovementState
    {
        ///<summary>L'état de marche du personnage</summary>
        Walking,
        ///<summary>L'état de course du personnage</summary>
        Running,
        ///<summary>L'état stationnaire du personnage</summary>
        Idle,
        ///<summary>L'état de glissage du personnage</summary>
        Sliding
    }

    ///<summary>
    ///     L'état des emotes du personnage. Le personnage contient plusieurs "poses" ou emotes
    ///     lui permettant de communiquer avec les autres pingouins. Ces états sont configurés
    ///     pour faire interagir le corps avec la physique externe
    /// </summary>
    public enum EmoteState
    {
        ///<summary>Le personnage pointe</summary>
        Pointing,
        ///<summary>Le personnage a les deux mains dans les airs</summary>
        ArmsUp,
        ///<summary>Le personnage tiens un signe dans les airs (Commande de jeu)</summary>
        ShowSign,
        ///<summary>État nul pour les emotes du personnage</summary>
        None
    }


    ///<summary>
    /// Retournes l'état de mouvement du personnage selon ses vecteurs de mouvement et de direction.
    /// </summary>
    public static MovementState GetMovementStateFromMovement(Vector3 InMoveDir, Vector3 InAimDir)
    {

        if (InMoveDir == Vector3.Zero) return MovementState.Idle;
        return MovementState.Walking;
    }

    ///<summary>
    /// Permet de Jouer une animation sur un personnage en mouvement
    /// </summary>
    /// <param name="InAnimationPlayer">
    /// Le node AnimationPlayer du personnage à animer
    /// </param>
    /// <param name="InNew">
    ///     Le nouveau MovementState du personnage pour l'animer en conséquence
    /// </param>
    public static void PlayAnimationFromMovement(MovementState InNew, AnimationPlayer InAnimationPlayer)
    {
        switch (InNew)
        {
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
    /// <remarks>
    ///     Probablement la cause du jittering partout, mais seule implémentation
    /// que je puisse expliquer sans soucis jusqu'à date.
    /// </remarks>
    public static Vector3 HookesLaw(Vector3 InDisplacement, Vector3 InCurrentVelocity, float InStiffness, float InDamping)
    {
        return (InStiffness * InDisplacement) - (InDamping * InCurrentVelocity);
    }

    /// <summary>
    /// Calcule la rotation (quaternion) nécessaire pour qu'un os pointe direct vers la cible.
    /// On part du principe que l'os regarde vers le haut (+Y) par défaut, et on l'aligne
    /// avec la direction locale demandée. Si c'est déjà enligné, on touche à rien.
    /// </summary>
    /// <param name="InLocalDir">Le vecteur de direction locale de pointage</param>
    /// <returns>Un beau Quaternion qui dit à l'os comment s'orienter.</returns>
    /// <remarks>
    /// NOTE: La fonction à été corrigé avec Claude Sonnet 4.6. Ma compréhension des Quaternions
    /// est encore purement au Type Godot et non mathématiquement.
    /// </remarks>
    public static Quaternion CalculatePointingRot(Vector3 InLocalDir)
    {
        // Étape 1 : On ramène la direction du monde dans l'espace local du parent.
        // Étape 2 : L'os pointe vers +Y, faut qui aille vers localdir
        Vector3 from = Vector3.Up;
        Vector3 to = InLocalDir.Normalized();
        // Étape 3 : On trouve l'axe de rotation qui est perpendiculaire aux deux vecteurs.
        Vector3 axis = from.Cross(to);
        // Check de sécurité : si c'est déjà aligné (ou pile de l'autre bord), on laisse ça de même.
        if (axis.LengthSquared() < 1e-6f)
            return Quaternion.Identity;
        // Étape 4 : On pogne l'angle entre les deux.
        float angle = from.AngleTo(to);
        // On retourne la rotation finale basée sur l'axe et l'angle.
        return new Quaternion(axis.Normalized(), angle);
    }

    /// <summary>
    ///     Permet de donner la direction locale d'une direction en worldspace, sur un Skeleton3D choisi.
    /// </summary>
    /// <param name="InTargetSkeleton">
    ///     Le skelette a prendre les transformations
    /// </param>
    /// <param name="InWorldAimDir">
    ///     La direction en worldspace de la direction a viser
    /// </param>
    /// <param name="parentBoneIdx">
    ///     Index du parent du bone à prendre la rotation
    /// </param>
    /// <returns> La position Vector3 Locale de direction demandée
    /// </returns>
    /// <remarks>
    ///     Pourrais etre merged avec CalculatePointingRot a moins qu'on l'utilise ailleurs
    /// </remarks>
    public static Vector3 GetLocalAimDir(Skeleton3D InTargetSkeleton, Vector3 InWorldAimDir, int parentBoneIdx)
    {
        Transform3D parentGlobalTransform = InTargetSkeleton.GlobalTransform * InTargetSkeleton.GetBoneGlobalPose(parentBoneIdx);
        return parentGlobalTransform.Basis.Inverse() * InWorldAimDir.Normalized();
    }

}
