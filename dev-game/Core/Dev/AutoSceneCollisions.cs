using Godot;

namespace Core.World;

public partial class AutoSceneCollisions : Node3D
{
	[Export] public int collision_layer = 2;
	[Export] public int collision_mask = 2;

	public override void _Ready()
	{
		BuildCollisionsRecursive(this);
	}

	private void BuildCollisionsRecursive(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh)
				AddCollisionForMesh(mesh);
			BuildCollisionsRecursive(child);
		}
	}

	private void AddCollisionForMesh(MeshInstance3D meshInstance)
	{
		if (meshInstance.Mesh is null)
			return;

		if (meshInstance.GetNodeOrNull("__AutoStaticBody") is not null)
			return;

		Shape3D? shape = meshInstance.Mesh.CreateTrimeshShape();
		if (shape is null)
			return;

		var body = new StaticBody3D
		{
			Name = "__AutoStaticBody",
			CollisionLayer = (uint)collision_layer,
			CollisionMask = (uint)collision_mask,
		};

		var collider = new CollisionShape3D
		{
			Shape = shape,
		};

		body.AddChild(collider);
		meshInstance.AddChild(body);
	}
}
