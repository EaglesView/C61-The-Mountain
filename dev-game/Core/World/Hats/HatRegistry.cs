public static class HatRegistry
{
	public const string DefaultHatId = "none";

	public sealed class HatDefinition
	{
		public string Id { get; init; } = "";
		public string DisplayName { get; init; } = "";
		/// <summary>
		/// Chemin vers la scène (ou .gltf) du chapeau. Le root doit être un
		/// <c>Node3D</c> — il sera instancié sous un <c>BoneAttachment3D</c> par
		/// <c>Player._SpawnHat</c>. Une chaîne vide signifie « pas de chapeau » :
		/// le spawn est court-circuité, ce qui sert pour l'entrée « None ».
		/// </summary>
		public string ScenePath { get; init; } = "";
	}

	public static readonly HatDefinition[] All =
	[
		new HatDefinition
		{
			Id = "none",
			DisplayName = "Aucun",
			ScenePath = ""
		},
		new HatDefinition
		{
			Id = "summer1",
			DisplayName = "Chapeau d'été",
			ScenePath = "res://Assets/Models/hats/SummerHat1/scene.gltf"
		}
	];

	public static HatDefinition? Get(string id)
	{
		foreach (var hat in All)
			if (hat.Id == id) return hat;
		return null;
	}

	public static int IndexOf(string id)
	{
		for (int i = 0; i < All.Length; i++)
			if (All[i].Id == id) return i;
		return 0;
	}
}
