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

	/// <summary>Définit le texte du titre («&#160;Player X Won&#160;»). Optionnel.</summary>
	public void SetWinnerLabel(string InText)
	{
		if (_titleLabel is not null) _titleLabel.Text = InText;
	}

	public override void _Ready()
	{
		_titleLabel = GetNodeOrNull<Label>("Panel/VBoxContainer/Label");
	}
}
