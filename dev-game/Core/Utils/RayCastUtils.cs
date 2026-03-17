using Godot;
namespace Utils;

public static class RayCastUtils
{
    public static GodotObject? GetObjectTypeFromRaycast(RayCast3D raycaster)
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
