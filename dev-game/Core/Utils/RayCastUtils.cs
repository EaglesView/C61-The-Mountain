using Godot;
namespace Utils;

public static class RayCastUtils
{
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
}
