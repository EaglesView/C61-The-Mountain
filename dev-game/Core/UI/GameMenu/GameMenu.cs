using Godot;

/// <summary>
/// Menu de pause du jeu. Gère sa propre visibilité, le mode souris, et son
/// propre input ESC (ouverture/fermeture) pour que la pause fonctionne pendant
/// toute la phase Game — y compris avant le spawn du joueur local, pendant
/// ragdoll, après la mort, en spectateur, etc. L'instance est créée par
/// <see cref="Core.World.GameController"/> au <c>Enter</c> de la phase.
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
		IsPaused = false;

		_resumeButton.Pressed += CloseMenu;
		_settingsButton.Pressed += OpenSettings;
		_backToMenuButton.Pressed += QuitToMenu;
		_backToDesktopButton.Pressed += QuitToDesktop;
		_muteButton.Pressed += ToggleMute;
		_killCharacterButton.Pressed += KillCharacter;
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		// Reset for the next phase — sinon un menu fermé via QuitToMenu
		// laisserait IsPaused=true et la prochaine phase Game ouvrirait
		// avec l'input du joueur déjà gelé.
		IsPaused = false;
	}

	/// <summary>
	/// Ouvre le menu de pause. Gèle l'input du joueur local s'il existe
	/// (sinon no-op — utile quand on ouvre le menu avant le spawn).
	/// </summary>
	public void OpenMenu()
	{
		if (IsPaused) return;
		IsPaused = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		SetLocalPlayerFrozen(true);
	}

	/// <summary>
	/// Ferme le menu de pause et redonne le contrôle au joueur.
	/// </summary>
	public void CloseMenu()
	{
		if (!IsPaused) return;
		IsPaused = false;
		Visible = false;
		Input.MouseMode = DebugMenuOpen()
			? Input.MouseModeEnum.Visible
			: Input.MouseModeEnum.Captured;
		SetLocalPlayerFrozen(false);
	}

	public override void _Input(InputEvent @event)
	{
		if (!@event.IsActionPressed("pause_menu")) return;
		if (IsPaused) CloseMenu();
		else OpenMenu();
	}

	private void SetLocalPlayerFrozen(bool InFrozen)
	{
		if (GetTree().GetFirstNodeInGroup("local_player") is Player p)
			p.InputFrozen = InFrozen;
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
