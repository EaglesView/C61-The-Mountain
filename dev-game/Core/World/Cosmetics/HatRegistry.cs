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
    public static Godot.Vector3 GlobalOffset { get; set; } = new Godot.Vector3(0f, 0.075f, 0.015f);
    public static Godot.Vector3 GlobalScale  { get; set; } = new Godot.Vector3(0.3f, 0.3f, 0.3f);
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
        public Godot.Vector3 Offset { get; init; } = Godot.Vector3.Zero;
        public Godot.Vector3 Scale { get; init; } = Godot.Vector3.One;
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
        },
        new HatDefinition
        {
            Id = "ananas",
            DisplayName = "Ananas",
            ScenePath = "res://Assets/Models/hats/model_ananas/model_ananas.gltf"
        },
        new HatDefinition
        {
            Id = "canadien",
            DisplayName = "Canadien",
            ScenePath = "res://Assets/Models/hats/model_canadien/model_canadien.gltf"
        },
        new HatDefinition
        {
            Id = "can",
            DisplayName = "Canette",
            ScenePath = "res://Assets/Models/hats/model_can/model_can.gltf"
        },
        new HatDefinition
        {
            Id = "chapeau",
            DisplayName = "Chapeau",
            ScenePath = "res://Assets/Models/hats/model_chapeau/model_chapeau.gltf"
        },
        new HatDefinition
        {
            Id = "chef",
            DisplayName = "Chef",
            ScenePath = "res://Assets/Models/hats/model_chef/model_chef.gltf"
        },
        new HatDefinition
        {
            Id = "cone",
            DisplayName = "Cône",
            ScenePath = "res://Assets/Models/hats/model_cone/model_cone.gltf"
        },
        new HatDefinition
        {
            Id = "fete",
            DisplayName = "Fête",
            ScenePath = "res://Assets/Models/hats/model_fete/model_fete.gltf"
        },
        new HatDefinition
        {
            Id = "flower",
            DisplayName = "Fleur",
            ScenePath = "res://Assets/Models/hats/model_flower/model_flower.gltf"
        },
        new HatDefinition
        {
            Id = "fries",
            DisplayName = "Frites",
            ScenePath = "res://Assets/Models/hats/model_fries/model_fries.gltf"
        },
        new HatDefinition
        {
            Id = "hat",
            DisplayName = "Hat",
            ScenePath = "res://Assets/Models/hats/model_hat/model_hat.gltf"
        },
        new HatDefinition
        {
            Id = "lunette",
            DisplayName = "Lunettes",
            ScenePath = "res://Assets/Models/hats/model_lunette/model_lunette.gltf"
        },
        new HatDefinition
        {
            Id = "night_goggles",
            DisplayName = "Night Goggles",
            ScenePath = "res://Assets/Models/hats/model_night_goggles/model_night_goggles.gltf"
        },
        new HatDefinition
        {
            Id = "ninja",
            DisplayName = "Ninja",
            ScenePath = "res://Assets/Models/hats/model_ninja/model_ninja.gltf"
        },
        new HatDefinition
        {
            Id = "noel",
            DisplayName = "Père Noël",
            ScenePath = "res://Assets/Models/hats/model_noel/model_noel.gltf"
        },
        new HatDefinition
        {
            Id = "pingouino",
            DisplayName = "Pingouino",
            ScenePath = "res://Assets/Models/hats/model_pingouino/model_pingouino.gltf"
        },
        new HatDefinition
        {
            Id = "sable",
            DisplayName = "Chapeau de sable",
            ScenePath = "res://Assets/Models/hats/model_sable/model_sable.gltf"
        },
        new HatDefinition
        {
            Id = "witch",
            DisplayName = "Sorcière",
            ScenePath = "res://Assets/Models/hats/model_witch/model_witch.gltf"
        },
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
