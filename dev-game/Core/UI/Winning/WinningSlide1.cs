using Godot;

/// <summary>
/// Première slide de l'écran Winning (<c>SCR_01_WHOWINS</c>)&#160;: affiche le
/// gagnant principal dans un grand panneau avec une scène 3D en SubViewport.
/// Reste statique&#160;; la séquence temporelle est pilotée par
/// <see cref="WinningController"/> via <see cref="WinningScene"/>.
/// </summary>
public sealed partial class WinningSlide1 : MarginContainer
{
	private Label _titleLabel;
	private Preview _winnerDisplay;
	private SubViewportContainer _previewContainer;

	/// <summary>Définit le texte du titre («&#160;Player X Won&#160;»). Optionnel.</summary>
	public void SetWinnerLabel(string InText)
	{
		if (_titleLabel is not null) _titleLabel.Text = InText;
	}

	/// <summary>
	/// Pousse le peer du gagnant au slot 3D pour qu'il reconstruise le penguin
	/// avec le chapeau du joueur correspondant, puis branche le pipeline réseau
	/// (MouseLook+send si le gagnant est le pair local, NetworkedRemote+receive
	/// sinon). Sans effet si le slot est introuvable (ex. tscn pas encore raccordé).
	/// </summary>
	public void SetWinnerPeer(int InPeerId)
	{
		GD.Print($"[WinningDiag] Slide1.SetWinnerPeer peer={InPeerId} display={(_winnerDisplay is null ? "NULL" : "ok")} container={(_previewContainer is null ? "NULL" : "ok")}");
		if (_winnerDisplay is null) return;
		try { _winnerDisplay.Show(InPeerId); GD.Print("[WinningDiag] Slide1.Show ok"); }
		catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide1.Show threw: {ex}"); return; }
		try { _winnerDisplay.BindNetwork(InPeerId); GD.Print("[WinningDiag] Slide1.BindNetwork ok"); }
		catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide1.BindNetwork threw: {ex}"); return; }
		if (_previewContainer is not null)
		{
			try { _winnerDisplay.BindMouseInput(_previewContainer); GD.Print("[WinningDiag] Slide1.BindMouseInput ok"); }
			catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide1.BindMouseInput threw: {ex}"); }
		}
	}

	public override void _Ready()
	{
		_titleLabel       = GetNodeOrNull<Label>("Panel/VBoxContainer/Label");
		_previewContainer = GetNodeOrNull<SubViewportContainer>("Panel/VBoxContainer/SubViewportContainer");
		_winnerDisplay    = GetNodeOrNull<Preview>(
			"Panel/VBoxContainer/SubViewportContainer/SubViewport/WinnerDisplay");
	}
}
