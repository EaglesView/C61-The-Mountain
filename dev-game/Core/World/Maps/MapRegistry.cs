public static class MapRegistry
{
	public const string DefaultMapId = "dev";

	public sealed class MapDefinition
	{
		public string Id { get; init; } = "";
		public string DisplayName { get; init; } = "";
		/// <summary>
		/// Chemin vers la scène du niveau. Le root de cette scène doit porter un
		/// script qui implémente <c>IPhase</c> + <c>IGameMode</c> — c'est lui qui
		/// sera consommé par le <c>GameController</c> comme mode de jeu actif.
		/// </summary>
		public string ScenePath { get; init; } = "";
	}

	public static readonly MapDefinition[] All =

	[
		new MapDefinition
		{
			Id = "dev",
			DisplayName = "La Montagne (Dev)",
			ScenePath = "res://Core/Dev/map_jm.tscn"
		},
			new MapDefinition
			{
				Id = "Jump The Barrel",
				DisplayName = "Rotating Thing Contest",
				ScenePath = "res://Core/World/Maps/rotating_barrel.tscn"
			}
	];

	public static MapDefinition? Get(string id)
	{
		foreach (var map in All)
			if (map.Id == id) return map;
		return null;
	}

	public static int IndexOf(string id)
	{
		for (int i = 0; i < All.Length; i++)
			if (All[i].Id == id) return i;
		return 0;
	}
}
