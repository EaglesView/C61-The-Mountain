using Godot;
using System.Collections.Generic;

public abstract partial class Interactable : StaticBody3D
{
	private Dictionary<MeshInstance3D, Material> originalMaterials = new();
	
	public abstract void Interact(Node3D interactor);
	
	public virtual void OnHighlight()
	{
		var meshInstances = FindChildren("*", "MeshInstance3D");
		foreach (var child in meshInstances)
		{
			if (child is MeshInstance3D meshInst)
			{
				for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
				{
					var originalMat = meshInst.GetSurfaceOverrideMaterial(i);
					if (originalMat != null)
					{
						originalMaterials[meshInst] = originalMat;
					}
				}
				
				var redMaterial = new StandardMaterial3D();
				redMaterial.AlbedoColor = new Color(1.0f, 0.3f, 0.3f, 1.0f);
				
				for (int i = 0; i < meshInst.GetMesh().GetSurfaceCount(); i++)
				{
					meshInst.SetSurfaceOverrideMaterial(i, redMaterial);
				}
			}
		}
	}
	
	public virtual void OnUnhighlight()
	{
		var meshInstances = FindChildren("*", "MeshInstance3D");
		foreach (var child in meshInstances)
		{
			if (child is MeshInstance3D meshInst)
			{
				if (originalMaterials.TryGetValue(meshInst, out var originalMat))
				{

					for (int i = 0; i < meshInst.GetMesh().GetSurfaceCount(); i++)
					{
						meshInst.SetSurfaceOverrideMaterial(i, originalMat);
					}
				}
				else
				{
					for (int i = 0; i < meshInst.GetMesh().GetSurfaceCount(); i++)
					{
						meshInst.SetSurfaceOverrideMaterial(i, null);
					}
				}
			}
		}
	}
}
