using Godot;
using Core.Auth;
using Core.Network.Rooms;
using Core.Profile;

public partial class ProfileScene : Control
{
    private const string VBox = "HBoxContainer/PanelContainer/InnerMargin/VBoxContainer";
    private const string VPPath = "HBoxContainer/SubViewportContainer/PreviewViewport";

    private LineEdit _usernameField = null!;
    private Button   _saveButton    = null!;
    private Button   _backButton    = null!;
    private Label    _statusLabel   = null!;

    private Label  _hatNameLabel  = null!;
    private Button _hatPrevButton = null!;
    private Button _hatNextButton = null!;
    private int    _hatIndex      = 0;

    private BoneAttachment3D? _hatAttachment;
    private Node3D?           _hatInstance;

    private Node3D?  _cameraPivot;
    private bool     _isDraggingPreview;
    private Vector2  _lastPreviewMousePos;

    private string _userId = null!;

    public override void _Ready()
    {
        _usernameField = GetNode<LineEdit>($"{VBox}/UsernameField");
        _saveButton    = GetNode<Button>($"{VBox}/SaveButton");
        _backButton    = GetNode<Button>($"{VBox}/BackButton");
        _statusLabel   = GetNode<Label>($"{VBox}/StatusLabel");
        _hatNameLabel  = GetNode<Label>($"{VBox}/HatBox/HatValue");
        _hatPrevButton = GetNode<Button>($"{VBox}/HatBox/HatPrev");
        _hatNextButton = GetNode<Button>($"{VBox}/HatBox/HatNext");

        _saveButton.Pressed    += OnSavePressed;
        _backButton.Pressed    += OnBackPressed;
        _hatPrevButton.Pressed += OnHatPrev;
        _hatNextButton.Pressed += OnHatNext;

        _hatIndex = HatRegistry.IndexOf(LobbyState.SelectedHatId);
        _RefreshHatDisplay();

        var svContainer = GetNode<SubViewportContainer>("HBoxContainer/SubViewportContainer");
        svContainer.GuiInput += OnPreviewInput;

        var viewport = GetNode<SubViewport>(VPPath);
        _BuildPreviewScene(viewport);

        LoadProfile();
    }

    // ── Hat cycling ──────────────────────────────────────────────────────────

    private void OnHatPrev()
    {
        _hatIndex = (_hatIndex - 1 + HatRegistry.All.Length) % HatRegistry.All.Length;
        _RefreshHatDisplay();
        LobbyState.SetSelectedHat(HatRegistry.All[_hatIndex].Id);
        _ApplyHatToPreview();
    }

    private void OnHatNext()
    {
        _hatIndex = (_hatIndex + 1) % HatRegistry.All.Length;
        _RefreshHatDisplay();
        LobbyState.SetSelectedHat(HatRegistry.All[_hatIndex].Id);
        _ApplyHatToPreview();
    }

    private void _RefreshHatDisplay()
    {
        _hatNameLabel.Text = HatRegistry.All[_hatIndex].DisplayName;
    }

    // ── 3D preview ───────────────────────────────────────────────────────────

    private void _BuildPreviewScene(SubViewport vp)
    {
        var root = new Node3D();
        vp.AddChild(root);

        // Background + ambient
        var worldEnv = new WorldEnvironment();
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.08f, 0.12f, 0.20f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = Colors.White;
        env.AmbientLightEnergy = 0.55f;
        worldEnv.Environment = env;
        root.AddChild(worldEnv);

        // Main light
        var light = new DirectionalLight3D();
        light.RotationDegrees = new Vector3(-50f, 30f, 0f);
        root.AddChild(light);

        // Orbit camera: pivot at penguin center, camera as child along +Z
        var pivot = new Node3D();
        pivot.Position = new Vector3(0f, 0.32f, 0f);
        root.AddChild(pivot);
        _cameraPivot = pivot;

        var camera = new Camera3D();
        camera.Position = new Vector3(0f, 0f, 1.1f);
        pivot.AddChild(camera);

        // Penguin visual mesh with idle animation
        var penguinScene = ResourceLoader.Load<PackedScene>("res://Assets/Models/penguin01.glb");
        var penguin = penguinScene.Instantiate<Node3D>();
        root.AddChild(penguin);

        // Play idle (standing) animation so the penguin is not in T-pose
        var animPlayer = _FindFirst<AnimationPlayer>(penguin);
        if (animPlayer != null)
        {
            animPlayer.Play("Idle_001");
            animPlayer.Advance(0.0);  // force the first frame to apply immediately
        }

        var skeleton = _FindFirst<Skeleton3D>(penguin);
        if (skeleton != null)
        {
            var attachment = new BoneAttachment3D();
            attachment.Name = "HatAttachment";
            attachment.BoneName = "Head.001";
            skeleton.AddChild(attachment);
            _hatAttachment = attachment;
            _ApplyHatToPreview();
        }
    }

    private void _ApplyHatToPreview()
    {
        _hatInstance?.QueueFree();
        _hatInstance = null;

        if (_hatAttachment == null) return;

        var hatDef = HatRegistry.All[_hatIndex];
        if (string.IsNullOrEmpty(hatDef.ScenePath)) return;

        var packed = ResourceLoader.Load<PackedScene>(hatDef.ScenePath);
        if (packed == null) return;

        _hatInstance = packed.Instantiate<Node3D>();
        _hatInstance.Position = HatRegistry.GlobalOffset + hatDef.Offset;
        _hatInstance.Scale = HatRegistry.GlobalScale * hatDef.Scale;
        _hatAttachment.AddChild(_hatInstance);
    }

    private void OnPreviewInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _isDraggingPreview = mb.Pressed;
            if (_isDraggingPreview)
                _lastPreviewMousePos = mb.Position;
        }
        else if (@event is InputEventMouseMotion mm && _isDraggingPreview && _cameraPivot != null)
        {
            float delta = mm.Position.X - _lastPreviewMousePos.X;
            _lastPreviewMousePos = mm.Position;
            _cameraPivot.RotateY(-delta * 0.008f);
        }
    }

    private static T? _FindFirst<T>(Node node) where T : Node
    {
        if (node is T found) return found;
        foreach (Node child in node.GetChildren())
        {
            var result = _FindFirst<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    // ── Profile load / save ──────────────────────────────────────────────────

    private async void LoadProfile()
    {
        var user = AuthServiceProvider.Instance.CurrentUser;
        if (user is null)
        {
            GetTree().ChangeSceneToFile("res://Core/UI/LoginTemp/login_temp.tscn");
            return;
        }

        _userId = user.Id;
        _saveButton.Disabled = true;

        try
        {
            var profile = await ProfileServiceProvider.Instance.GetOrCreateProfileAsync(
                user.Id, user.Username);
            _usernameField.Text = profile.Username;
        }
        catch (System.Exception e)
        {
            ShowStatus($"Failed to load profile: {e.Message}", error: true);
        }
        finally
        {
            _saveButton.Disabled = false;
        }
    }

    private async void OnSavePressed()
    {
        _saveButton.Disabled = true;
        _statusLabel.Visible = false;

        try
        {
            await ProfileServiceProvider.Instance.UpdateUsernameAsync(_userId, _usernameField.Text);
            ShowStatus("Username updated!", error: false);
        }
        catch (System.Exception e)
        {
            ShowStatus(e.Message, error: true);
        }
        finally
        {
            _saveButton.Disabled = false;
        }
    }

    private void OnBackPressed()
    {
        GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
    }

    private void ShowStatus(string message, bool error)
    {
        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride(
            "font_color",
            error ? new Color(1, 0.3f, 0.3f) : new Color(0.3f, 1, 0.3f));
        _statusLabel.Visible = true;
    }
}
