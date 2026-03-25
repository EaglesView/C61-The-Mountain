using Godot;
namespace Utils;

public static class CameraUtils
{
    ///<summary>
    /// Les types de caméras ou de modes de caméras possibles dans le jeu. Permet de simplifier
    /// La tache des programmeurs en automatisant certains processus.
    /// </summary>
    public enum CameraType
    {
        FirstPerson,
        ThirdPerson,
        FreeMode,
        Death
    };
}
