using Godot;
namespace Core.UI.Loading;

/// <summary>
/// Helper statique pour afficher / cacher l'overlay de chargement
/// (<c>loading_scene.tscn</c>) à partir de n'importe quel controller. L'instance
/// est parentée à la racine de la <see cref="SceneTree"/> pour qu'elle survive
/// aux transitions de phase (Lobby → Game) sans dépendre du cycle de vie
/// d'un controller particulier.
///
/// L'overlay expose une étiquette de statut et une jauge de progression. Les
/// appelants annoncent leur étape via <see cref="SetStatus"/> (texte + ratio 0..1)&#160;;
/// la jauge interpole en douceur entre les valeurs successives via un <see cref="Tween"/>.
/// </summary>
public static class LoadingScreen
{
    private const string ScenePath = "res://Core/UI/Loading/loading_scene.tscn";
    private const string LabelPath = "MarginContainer/Panel/PanelContainer/Label";
    private const string TextureBarPath = "MarginContainer/Panel/PanelContainer/TextureProgressBar";
    private const string BarPath = "MarginContainer/Panel/PanelContainer/ProgressBar";

    /// <summary>Durée du tween entre la valeur courante de la jauge et la nouvelle cible.</summary>
    private const float TweenDurationSec = 0.25f;

    private static PackedScene _scene;
    private static Control _instance;
    private static Label _label;
    private static Range _textureBar;
    private static Range _bar;
    private static Tween _tween;

    /// <summary>Indique si l'overlay est actuellement affiché.</summary>
    public static bool IsVisible => _instance is not null && GodotObject.IsInstanceValid(_instance);

    /// <summary>
    /// Affiche l'overlay s'il ne l'est pas déjà. Parenté au root de la <see cref="SceneTree"/>
    /// pour persister à travers les changements de phase. La jauge et l'étiquette sont
    /// réinitialisées à zéro / vide. Idempotent.
    /// </summary>
    /// <param name="InTree">La <see cref="SceneTree"/> active (obtenue via <c>GetTree()</c>).</param>
    public static void Show(SceneTree InTree)
    {
        if (InTree is null) return;
        if (IsVisible) return;
        _scene ??= ResourceLoader.Load<PackedScene>(ScenePath);
        if (_scene is null)
        {
            GD.PrintErr($"[LoadingScreen] Scène introuvable : {ScenePath}");
            return;
        }
        _instance = _scene.Instantiate<Control>();
        InTree.Root.AddChild(_instance);
        _label = _instance.GetNodeOrNull<Label>(LabelPath);
        _textureBar = _instance.GetNodeOrNull<Range>(TextureBarPath);
        _bar = _instance.GetNodeOrNull<Range>(BarPath);
        if (_label is not null) _label.Text = string.Empty;
        if (_bar is not null) _bar.Value = 0d;
        if (_textureBar is not null) _textureBar.Value = 0d;
    }

    /// <summary>Cache et libère l'overlay. Idempotent.</summary>
    public static void Hide()
    {
        if (_tween is not null && GodotObject.IsInstanceValid(_tween)) _tween.Kill();
        _tween = null;
        if (_instance is not null && GodotObject.IsInstanceValid(_instance))
            _instance.QueueFree();
        _instance = null;
        _label = null;
        _textureBar = null;
        _bar = null;
    }

    /// <summary>
    /// Met à jour l'étiquette et la jauge en un appel. Équivaut à
    /// <see cref="SetLabel"/> + <see cref="SetProgress"/>. No-op si l'overlay
    /// n'est pas affiché.
    /// </summary>
    /// <param name="InText">Texte de statut affiché au-dessus de la jauge.</param>
    /// <param name="InProgress">Progression cible, ratio 0..1 (clampé).</param>
    public static void SetStatus(string InText, float InProgress)
    {
        SetLabel(InText);
        SetProgress(InProgress);
    }

    /// <summary>Met à jour le texte de statut. No-op si l'overlay n'est pas affiché.</summary>
    public static void SetLabel(string InText)
    {
        if (_label is null || !GodotObject.IsInstanceValid(_label)) return;
        _label.Text = InText ?? string.Empty;
    }

    /// <summary>
    /// Met à jour la progression cible. La jauge interpole de la valeur courante
    /// vers la cible (échelle 0..100) sur <see cref="TweenDurationSec"/> secondes
    /// via un Tween parallèle sur les deux ProgressBars. No-op si l'overlay
    /// n'est pas affiché.
    /// </summary>
    /// <param name="InProgress">Ratio cible 0..1, clampé. 1.0 = barre pleine.</param>
    public static void SetProgress(float InProgress)
    {
        if (!IsVisible) return;
        float target = Mathf.Clamp(InProgress, 0f, 1f) * 100f;

        if (_tween is not null && GodotObject.IsInstanceValid(_tween)) _tween.Kill();
        _tween = _instance.CreateTween();
        _tween.SetParallel(true);
        if (_bar is not null && GodotObject.IsInstanceValid(_bar))
            _tween.TweenProperty(_bar, "value", (double)target, TweenDurationSec);
        if (_textureBar is not null && GodotObject.IsInstanceValid(_textureBar))
            _tween.TweenProperty(_textureBar, "value", (double)target, TweenDurationSec);
    }
}
