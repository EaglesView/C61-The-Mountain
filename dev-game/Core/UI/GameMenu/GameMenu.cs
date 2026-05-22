using Godot;

/// <summary>
/// Menu de pause du jeu. Gère sa propre visibilité et le mode souris.
/// Le <see cref="Player"/> vérifie <see cref="IsPaused"/> pour bloquer son input
/// et appelle <see cref="OpenMenu"/> pour ouvrir le menu.
/// </summary>
public partial class GameMenu : Control
{
	/// <summary>Instance unique du menu, disponible dès <c>_Ready</c>.</summary>
	public static GameMenu? Instance { get; private set; }

	/// <summary><c>true</c> si le jeu est présentement en pause.</summary>
	public static bool IsPaused { get; private set; } = false;

	[Export] private Button _resumeButton;
	[Export] private Button _settingsButton;
	[Export] private Button _backToMenuButton;
	[Export] private Button _backToDesktopButton;
	[Export] private Button _muteButton;
	[Export] private Button _killCharacterButton;

	public override void _Ready()
	{
		Instance = this;
		Visible = false;

		_resumeButton.Pressed += CloseMenu;
		_settingsButton.Pressed += OpenSettings;
		_backToMenuButton.Pressed += QuitToMenu;
		_backToDesktopButton.Pressed += QuitToDesktop;
		_muteButton.Pressed += ToggleMute;
		_killCharacterButton.Pressed += KillCharacter;
	}

	/// <summary>
	/// Ouvre le menu de pause. Appelé par le <see cref="Player"/> sur l'action <c>pause_menu</c>.
    /// </summary>
    public void OpenMenu()
    {
        IsPaused = true;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// Ferme le menu de pause et redonne le contrôle au joueur.
    /// </summary>
    public void CloseMenu()
    {
        IsPaused = false;
        Visible = false;
        Input.MouseMode = DebugMenuOpen()
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent @event)
    {
		// Le menu gère uniquement la fermeture — l'ouverture est gérée par le Player
		if (IsPaused && @event.IsActionPressed("pause_menu"))
			CloseMenu();
	}

	private void OpenSettings()
	{
		var settingsScene = GD.Load<PackedScene>("res://Core/UI/Settings/settings_menu.tscn");
		AddChild(settingsScene.Instantiate<Control>());
	}

	private void QuitToMenu()
	{
		IsPaused = false;

		Core.Network.Rooms.LobbyCleanup.LeaveRoomFireAndForget();
		Core.Network.NetworkManager.Instance?.Disconnect();
		Core.Network.Rooms.LobbyState.Clear();

		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}

	private void QuitToDesktop()
	{
		GetTree().Quit();
	}

	private void ToggleMute()
	{
		AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), !AudioServer.IsBusMute(AudioServer.GetBusIndex("Master")));
	}

	private void KillCharacter()
	{
		// TODO: implémenter la logique de kill
	}

	private static bool DebugMenuOpen()
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		var debug = tree?.Root.FindChild("Debug", true, false) as Control;
		return debug?.Visible ?? false;
	}
}
