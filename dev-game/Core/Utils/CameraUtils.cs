using Godot;
namespace Utils;

///<summary>
/// Classe statique d'utilitaires pour la caméra. Permet de simplifier le code de CameraMan.cs
/// </summary>
public static class CameraUtils
{
    ///<summary>
    /// Les types de caméras ou de modes de caméras possibles dans le jeu. Permet de simplifier
    /// La tache des programmeurs en automatisant certains processus.
    /// </summary>
    public enum CameraType
    {
        ///<summary> Mode de vue première personne </summary>
        FirstPerson,
        ///<summary> Mode de vue troisième personne </summary>
        ThirdPerson,
        ///<summary> Mode de vue libre (FreeCam) </summary>
        FreeMode,
        ///<summary> Mode spécial pour la caméra de "mort" </summary>
        Death
    };
}
