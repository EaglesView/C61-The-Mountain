using Godot;

public partial class WinningScene : Control
{
	/// <summary>
	/// Émis quand le joueur (typiquement l'hôte) a confirmé qu'on relance une partie.
	/// Le <c>WinningController</c> écoute ce signal pour avancer la FSM vers
	/// l'état <c>NewGame</c>.
	/// </summary>
	[Signal] public delegate void LaunchNewLobbyEventHandler();

	/// <summary>
	/// Câblé au signal <c>pressed</c> du bouton «&#160;Relancer&#160;» dans la scène.
	/// </summary>
	private void OnLaunchNewLobbyPressed()
	{
		EmitSignal(SignalName.LaunchNewLobby);
	}
}
