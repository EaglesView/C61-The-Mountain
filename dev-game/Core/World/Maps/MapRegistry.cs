public static class MapRegistry
{
    public const string DefaultMapId = "dev";

    /// <summary>
    /// Texture placeholder utilisée pour les maps qui n'ont pas encore d'aperçu
    /// dédié : réutilise la base color du penguin déjà présente dans les
    /// assets. Remplacer par des renders propres au moment voulu.
    /// </summary>
    public const string PlaceholderImagePath = "res://Assets/Models/penguin01_private_baseColor.png";

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

        /// <summary>
        /// Chemin vers une <c>Texture2D</c> d'aperçu de la map, utilisée par
        /// l'écran de vote (<c>MapGridItem</c>). Par défaut, le placeholder
        /// partagé : laisser tel quel jusqu'à ce qu'un asset propre soit
        /// disponible.
        /// </summary>
        public string ImagePath { get; init; } = PlaceholderImagePath;
    }

    public static readonly MapDefinition[] All =

    [
        new MapDefinition
        {
            Id = "dev",
            DisplayName = "Dev - Crash to Winning",
            ScenePath = "res://Core/Dev/map_jm.tscn"
        },
        new MapDefinition
        {
            Id = "Jump The Barrel",
            DisplayName = "Rotating Thing Contest",
            ScenePath = "res://Core/World/Maps/rotating_barrel.tscn"
        },
        new MapDefinition
        {
            Id = "Falling Tiles",
            DisplayName = "Falling Tiles",
            ScenePath = "res://Core/World/Maps/falling_tiles.tscn"
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
