using Godot;

/// <summary>
/// Entrée «&#160;joueur connecté&#160;» dans la colonne lobby de l'écran de vote
/// (SCR_3_VOTEMAP). Affiche le nom et un tag «&#160;(HOST)&#160;» conditionnel.
/// </summary>
public sealed partial class LobbyUser : Control
{
	private Label _playerName;
	private Label _isHostLabel;

	/// <summary>
	/// Renseigne le nom affiché et l'état hôte. À appeler après instanciation.
	/// </summary>
	public void SetUser(string InUsername, bool InIsHost)
	{
		if (_playerName is not null) _playerName.Text = InUsername;
		if (_isHostLabel is not null) _isHostLabel.Visible = InIsHost;
	}

	public override void _Ready()
	{
		_playerName = GetNodeOrNull<Label>("PlayerContainer/PlayerName");
		_isHostLabel = GetNodeOrNull<Label>("PlayerContainer/IsHostLabels");
	}
}
