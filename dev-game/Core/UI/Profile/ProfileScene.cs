using Godot;
using Core.Auth;
using Core.Profile;

public partial class ProfileScene : Control
{
    private LineEdit _usernameField = null!;
    private Button _saveButton = null!;
    private Button _backButton = null!;
    private Label _statusLabel = null!;

    private string _userId = null!;

    public override void _Ready()
    {
        _usernameField = GetNode<LineEdit>("VBoxContainer/UsernameField");
        _saveButton = GetNode<Button>("VBoxContainer/SaveButton");
        _backButton = GetNode<Button>("VBoxContainer/BackButton");
        _statusLabel = GetNode<Label>("VBoxContainer/StatusLabel");

        _saveButton.Pressed += OnSavePressed;
        _backButton.Pressed += OnBackPressed;

        LoadProfile();
    }

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
