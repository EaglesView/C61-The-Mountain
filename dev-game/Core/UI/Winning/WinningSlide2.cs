using Godot;

/// <summary>
/// Deuxième slide de l'écran Winning (<c>SCR_02_SUBWINNERS</c>)&#160;: présente
/// les sous-catégories (Most Ragdolls, Quickest to Die, etc.) puis affiche un
/// bouton «&#160;Continue&#160;» après un délai pour permettre au joueur d'avancer
/// vers la slide de vote. Le bouton est révélé par
/// <see cref="ShowContinueButton"/>&#160;; son clic émet <see cref="ContinuePressed"/>.
/// </summary>
public sealed partial class WinningSlide2 : MarginContainer
{
	/// <summary>Émis lorsque l'utilisateur clique sur le bouton «&#160;Continue&#160;».</summary>
	[Signal] public delegate void ContinuePressedEventHandler();

	private Button _continueButton;

	/// <summary>Rend le bouton «&#160;Continue&#160;» visible et cliquable.</summary>
	public void ShowContinueButton()
	{
		if (_continueButton is null) return;
		_continueButton.Visible = true;
		_continueButton.Disabled = false;
	}

	/// <summary>Cache le bouton (état initial, ou re-entrée propre).</summary>
	public void HideContinueButton()
	{
		if (_continueButton is null) return;
		_continueButton.Visible = false;
	}

	public override void _Ready()
	{
		_continueButton = GetNodeOrNull<Button>("ContinueBar/ContinueButton");
		if (_continueButton is not null)
		{
			_continueButton.Visible = false;
			_continueButton.Pressed += OnContinuePressed;
		}
	}

	private void OnContinuePressed() => EmitSignal(SignalName.ContinuePressed);
}
