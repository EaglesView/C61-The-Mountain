using Godot;
using System;

/// <summary>
/// Dialogue modal d'erreur affichable depuis n'importe quel controller via
/// l'API statique <see cref="Show"/>. Mirroir volontaire de <c>LoadingScreen</c>&#160;:
/// l'instance est parentée au root de la <see cref="SceneTree"/> pour survivre
/// aux <c>ChangeSceneToFile</c> que le callback OK pourrait déclencher.
///
/// Le dialogue est <b>agnostique</b> de l'action liée au bouton OK. L'appelant
/// fournit un <see cref="Action"/> (retour menu, retour lobby, retry, …)&#160;:
/// le dialogue l'invoque puis se libère.
/// </summary>
public partial class ErrorDialog : Control
{
    private const string ScenePath = "res://Core/UI/Error/error_dialog.tscn";
    private const string TitlePath = "MarginContainer/Panel/VBoxContainer/HBoxContainer/DialogTitle";
    private const string MessagePath = "MarginContainer/Panel/VBoxContainer/ErrorMessageRichLabel";
    private const string CloseButtonPath = "MarginContainer/Panel/VBoxContainer/HBoxContainer/MarginContainer/Button";
    private const string OkButtonPath = "MarginContainer/Panel/VBoxContainer/HBoxContainer2/Button";

    private static PackedScene _scene;
    private static ErrorDialog _instance;

    private Label _title;
    private RichTextLabel _message;
    private Button _closeButton;
    private Button _okButton;
    private Action _onOk;
    private Action _onClose;

    /// <summary>Indique si un dialogue est actuellement affiché.</summary>
    public static bool IsVisible => _instance is not null && GodotObject.IsInstanceValid(_instance);

    /// <summary>
    /// Affiche un dialogue d'erreur modal. Si un dialogue est déjà visible,
    /// il est remplacé. Le bouton OK invoque <paramref name="InOnOk"/> puis
    /// ferme le dialogue&#160;; la croix invoque <paramref name="InOnClose"/>
    /// (ou ferme sans effet de bord si <c>null</c>).
    /// </summary>
    /// <param name="InTree">La <see cref="SceneTree"/> active (obtenue via <c>GetTree()</c>).</param>
    /// <param name="InMessage">Message affiché. Supporte le BBCode (RichTextLabel).</param>
    /// <param name="InTitle">Titre, ou <c>null</c> pour garder celui de la scène.</param>
    /// <param name="InOkText">Libellé du bouton OK, ou <c>null</c> pour garder «&#160;OK&#160;».</param>
    /// <param name="InOnOk">Action invoquée au clic OK, après fermeture du dialogue. Peut être <c>null</c>.</param>
    /// <param name="InOnClose">Action invoquée au clic sur la croix. Peut être <c>null</c>.</param>
    public static void Show(SceneTree InTree,
        string InMessage,
        string InTitle = null,
        string InOkText = null,
        Action InOnOk = null,
        Action InOnClose = null)
    {
        if (InTree is null) return;
        if (IsVisible) Hide();

        _scene ??= ResourceLoader.Load<PackedScene>(ScenePath);
        if (_scene is null)
        {
            GD.PrintErr($"[ErrorDialog] Scène introuvable&#160;: {ScenePath}");
            InOnOk?.Invoke();
            return;
        }

        _instance = _scene.Instantiate<ErrorDialog>();
        _instance._onOk = InOnOk;
        _instance._onClose = InOnClose;
        InTree.Root.AddChild(_instance);
        _instance.Configure(InMessage, InTitle, InOkText);
    }

    /// <summary>Ferme le dialogue immédiatement, sans invoquer d'action. Idempotent.</summary>
    public static void Hide()
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance))
            _instance.QueueFree();
        _instance = null;
    }

    private void Configure(string InMessage, string InTitle, string InOkText)
    {
        if (_title is not null && !string.IsNullOrEmpty(InTitle))
            _title.Text = InTitle;
        if (_message is not null)
            _message.Text = InMessage ?? string.Empty;
        if (_okButton is not null && !string.IsNullOrEmpty(InOkText))
            _okButton.Text = InOkText;
    }

    private void OnOkPressed()
    {
        var cb = _onOk;
        _onOk = null;
        _onClose = null;
        Hide();
        cb?.Invoke();
    }

    private void OnClosePressed()
    {
        var cb = _onClose;
        _onOk = null;
        _onClose = null;
        Hide();
        cb?.Invoke();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _title = GetNodeOrNull<Label>(TitlePath);
        _message = GetNodeOrNull<RichTextLabel>(MessagePath);
        _closeButton = GetNodeOrNull<Button>(CloseButtonPath);
        _okButton = GetNodeOrNull<Button>(OkButtonPath);

        if (_closeButton is not null) _closeButton.Pressed += OnClosePressed;
        if (_okButton is not null) _okButton.Pressed += OnOkPressed;
    }
}
