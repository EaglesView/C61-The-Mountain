/// <summary>
/// Catalogue des chapeaux cosmétiques sélectionnables par le joueur. Mirroir
/// volontaire de <see cref="MapRegistry"/> : pour ajouter un chapeau, créer
/// une scène <c>.tscn</c> dont la racine est un <c>Node3D</c> positionné en
/// local de façon à ce qu'une fois parenté au <c>BoneAttachment3D</c> de la
/// tête (bone <c>Head.001</c> du rig animé), le rendu soit correct — puis
/// déclarer une entrée dans <see cref="All"/>.
/// </summary>
public static class HatRegistry
{
    /// <summary>Id réservé indiquant «&#160;aucun chapeau&#160;».</summary>
    public const string NoneHatId = "none";

    /// <summary>Chapeau par défaut au démarrage / fallback.</summary>
    public const string DefaultHatId = NoneHatId;

    /// <summary>
    /// Texture placeholder pour les chapeaux qui n'ont pas encore d'aperçu
    /// dédié — même convention que <c>MapRegistry.PlaceholderImagePath</c>.
    /// </summary>
    public const string PlaceholderImagePath = "res://Assets/Models/penguin01_private_baseColor.png";

    public sealed class HatDefinition
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        /// <summary>
        /// Chemin vers la scène du chapeau. Sa racine doit être un
        /// <c>Node3D</c>. Laisser vide pour l'entrée «&#160;aucun chapeau&#160;»
        /// (rien n'est instancié dans ce cas).
        /// </summary>
        public string ScenePath { get; init; } = "";

        /// <summary>
        /// Aperçu utilisé par l'UI de sélection. Par défaut, le placeholder
        /// partagé : laisser tel quel jusqu'à ce qu'un asset propre soit
        /// disponible.
        /// </summary>
        public string ImagePath { get; init; } = PlaceholderImagePath;
    }

    public static readonly HatDefinition[] All =
    [
        new HatDefinition
        {
            Id = NoneHatId,
            DisplayName = "Aucun",
            ScenePath = ""
        },
        new HatDefinition
        {
            Id = "summerhat",
            DisplayName = "Chapeau d'été",
            ScenePath = "res://Assets/Models/hats/SummerHat1/scene.gltf"
        }
        // Ajouter de nouveaux chapeaux ici, p.ex.:
        // new HatDefinition
        // {
        //     Id = "tuque",
        //     DisplayName = "Tuque",
        //     ScenePath = "res://Core/World/Cosmetics/Hats/tuque.tscn"
        // },
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
