using Godot;
namespace Utils;

/// <summary>
/// Classe utilitaire pour les opérations de raycasting dans le monde 3D.
/// Fournit des méthodes pour interroger les collisions détectées par un <see cref="RayCast3D"/>.
/// </summary>
public static class RayCastUtils
{
    /// <summary>
    /// Retourne l'objet frappé par le raycaster s'il y a collision, sinon <c>null</c>.
    /// Affiche également le type de l'objet frappé dans la console via <see cref="GD.Print"/>.
    /// </summary>
    /// <param name="raycaster">Le <see cref="RayCast3D"/> à interroger pour la collision.</param>
    /// <returns>
    /// Le <see cref="GodotObject"/> frappé par le rayon, ou <c>null</c> si aucune collision.
    /// </returns>
    public static GodotObject GetObjectTypeFromRaycast(RayCast3D raycaster)
    {
        if (!raycaster.IsColliding()) return null;
        {
            GodotObject hit = raycaster.GetCollider();
            Vector3 hitPoint = raycaster.GetCollisionPoint();
            Vector3 hitNormal = raycaster.GetCollisionNormal();

            GD.Print("Hit: ", hit.GetType().Name);
            return hit;
        }
    }

    public static Interactable? GetInteractableFromRaycast(RayCast3D raycaster, float maxDistance = 3.0f)
    {
        if (!raycaster.IsColliding()) return null;

        Vector3 origin = raycaster.GlobalTransform.Origin;
        Vector3 hitPoint = raycaster.GetCollisionPoint();
        if (origin.DistanceTo(hitPoint) > maxDistance)
        {
            return null;
        }

        if (raycaster.GetCollider() is not Node node)
        {
            return null;
        }

        Node current = node;
        while (current != null)
        {
            if (current is Interactable interactable)
            {
                return interactable;
            }

            current = current.GetParent();
        }

        return null;
    }
}
