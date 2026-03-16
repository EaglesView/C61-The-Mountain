using Godot;

public partial class MainMenu : Control
{
    private Button _play = null!;
    private Button _create = null!;
    private Button _join = null!;
    private Button _profile = null!;
    private Button _credits = null!;
    private Button _signOut = null!;
    private Button _exit = null!;

    public override void _Ready()
    {
        _play = GetNode<Button>("VBoxContainer/PlayButton");
        _create = GetNode<Button>("VBoxContainer/CreateGameButton");
        _join = GetNode<Button>("VBoxContainer/JoinGameButton");
        _profile = GetNode<Button>("VBoxContainer/ProfileButton");
        _credits = GetNode<Button>("VBoxContainer/CreditsButton");
        _signOut = GetNode<Button>("VBoxContainer/SignOutButton");
        _exit = GetNode<Button>("VBoxContainer/ExitButton");

        _play.Pressed += OnPlayPressed;
        _create.Pressed += OnCreatePressed;
        _join.Pressed += OnJoinPressed;
        _profile.Pressed += OnProfilePressed;
        _credits.Pressed += OnCreditsPressed;
        _signOut.Pressed += OnSignOutPressed;
        _exit.Pressed += OnExitPressed;
    }

    private void OnPlayPressed()
    {
        GetTree().ChangeSceneToFile("res://Core/Dev/world_jim.tscn");
    }

    private void OnCreatePressed()
    {

    }

    private void OnJoinPressed()
    {

    }

    private void OnProfilePressed()
    {
        GetTree().ChangeSceneToFile("res://Core/UI/Profile/profile.tscn");
    }

    private void OnCreditsPressed()
    {

    }

    private async void OnSignOutPressed()
    {
        await Core.Auth.AuthServiceProvider.SignOutAsync();
        GetTree().ChangeSceneToFile("res://Core/UI/LoginTemp/login_temp.tscn");
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }
}
