using Godot;
using Core.Auth;
using Core.Network.Rooms;
using Core.Profile;

public partial class ProfileScene : Control
{
	private const string VBox = "HBoxContainer/PanelContainer/InnerMargin/VBoxContainer";
	private const string PreviewScenePath = "res://Core/UI/Preview/preview.tscn";

	private LineEdit _usernameField = null!;
	private Button _saveButton = null!;
	private Button _backButton = null!;
	private Label _statusLabel = null!;

	private Label _hatNameLabel = null!;
	private Button _hatPrevButton = null!;
	private Button _hatNextButton = null!;
	private int _hatIndex = 0;

	private Preview? _preview;

	private string _userId = null!;

	public override void _Ready()
	{
		_usernameField = GetNode<LineEdit>($"{VBox}/UsernameField");
		_saveButton = GetNode<Button>($"{VBox}/SaveButton");
		_backButton = GetNode<Button>($"{VBox}/BackButton");
		_statusLabel = GetNode<Label>($"{VBox}/StatusLabel");
		_hatNameLabel = GetNode<Label>($"{VBox}/HatBox/HatValue");
		_hatPrevButton = GetNode<Button>($"{VBox}/HatBox/HatPrev");
		_hatNextButton = GetNode<Button>($"{VBox}/HatBox/HatNext");

		_saveButton.Pressed += OnSavePressed;
		_backButton.Pressed += OnBackPressed;
		_hatPrevButton.Pressed += OnHatPrev;
		_hatNextButton.Pressed += OnHatNext;

		_hatIndex = HatRegistry.IndexOf(LobbyState.SelectedHatId);
		_RefreshHatDisplay();

		_BuildPreview();
		LoadProfile();
	}

	// ── Hat cycling ──────────────────────────────────────────────────────────

	private void OnHatPrev()
	{
		_hatIndex = (_hatIndex - 1 + HatRegistry.All.Length) % HatRegistry.All.Length;
		_ApplySelectedHat();
	}

	private void OnHatNext()
	{
		_hatIndex = (_hatIndex + 1) % HatRegistry.All.Length;
		_ApplySelectedHat();
	}

	private void _ApplySelectedHat()
	{
		var hatId = HatRegistry.All[_hatIndex].Id;
		_RefreshHatDisplay();
		LobbyState.SetSelectedHat(hatId);
		_preview?.SetHat(hatId);
	}

	private void _RefreshHatDisplay()
	{
		_hatNameLabel.Text = HatRegistry.All[_hatIndex].DisplayName;
	}

	// ── Preview wiring ───────────────────────────────────────────────────────

	private void _BuildPreview()
	{
		var svContainer = GetNode<SubViewportContainer>("HBoxContainer/SubViewportContainer");
		var viewport = GetNode<SubViewport>("HBoxContainer/SubViewportContainer/PreviewViewport");

		var packed = ResourceLoader.Load<PackedScene>(PreviewScenePath);
		if (packed is null)
		{
			GD.PrintErr($"[ProfileScene] Preview scene introuvable&#160;: {PreviewScenePath}");
			return;
		}

		_preview = packed.Instantiate<Preview>();
		// DragOrbit: l'utilisateur drague pour inspecter son penguin sous tous
		// les angles avant de sauver — reproduit le feel de l'ancien
		// _cameraPivot.RotateY(-delta * 0.008f).
		_preview.Mode = Preview.InteractionMode.MouseLook;
		viewport.AddChild(_preview);
		// Spawn explicite&#160;: Preview ne fait plus d'auto-spawn en _Ready (cf.
		// commentaire dans Preview.cs). LoadProfile() ré-applique ensuite le
		// hat sauvegardé via SetHat() une fois le profil récupéré.
		_preview.SpawnCharacter(LobbyState.SelectedHatId);
		_preview.BindMouseInput(svContainer);
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
			LobbyState.SetProfileUsername(profile.Username);

			var savedIndex = HatRegistry.IndexOf(profile.SelectedHatId);
			if (savedIndex >= 0) _hatIndex = savedIndex;
			_ApplySelectedHat();
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
			await ProfileServiceProvider.Instance.UpdateHatIdAsync(_userId, HatRegistry.All[_hatIndex].Id);
			ShowStatus("Profil mis à jour", error: false);
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
