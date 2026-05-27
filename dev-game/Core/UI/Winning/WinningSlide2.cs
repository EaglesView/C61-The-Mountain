using System.Collections.Generic;
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

    /// <summary>Entrée «&#160;un sous-gagnant&#160;» à afficher dans un des panneaux.</summary>
    public readonly struct SubEntry
    {
        public string Title { get; init; }
        public string Username { get; init; }
        public string Detail { get; init; }
        /// <summary>
        /// Peer ID du sous-gagnant&#160;: utilisé par le slot 3D pour résoudre
        /// le chapeau depuis <c>LobbyState.LastHats</c>. 0 = pas de peer
        /// connu (slot affichera le penguin par défaut sans chapeau).
        /// </summary>
        public int PeerId { get; init; }
    }

    private Button _continueButton;
    private Panel[] _panels;
    private Label[] _titleLabels;
    private Label[] _usrLabels;
    private Label[] _amtLabels;
    private Preview[] _winnerDisplays;
    private SubViewportContainer[] _previewContainers;

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

    /// <summary>
    /// Remplit les trois panneaux de sous-gagnants. Pour chaque slot&#160;: titre
    /// = libellé de la condition, usr_label = nom du joueur résolu via la
    /// callback, amt_label = texte court (placeholder si non fourni). Les
    /// panneaux sans entrée correspondante sont masqués.
    /// </summary>
    public void Populate(IReadOnlyList<SubEntry> InEntries)
    {
        GD.Print($"[WinningDiag] Slide2.Populate entries={InEntries?.Count ?? -1} panels={(_panels is null ? "NULL" : _panels.Length.ToString())} continueBtn={(_continueButton is null ? "NULL" : "ok")}");
        if (_panels is null) return;
        for (int i = 0; i < _panels.Length; i++)
        {
            bool hasEntry = InEntries is not null && i < InEntries.Count;
            if (_panels[i] is not null) _panels[i].Visible = hasEntry;
            if (!hasEntry)
            {
                // Pas de sous-gagnant à cet index&#160;: on libère le penguin du
				// slot pour éviter qu'un précédent affichage (slide replayée) ne
				// traîne dans le viewport masqué.
				_winnerDisplays?[i]?.Clear();
				continue;
			}
			var e = InEntries[i];
			if (_titleLabels[i] is not null) _titleLabels[i].Text = e.Title ?? "";
			if (_usrLabels[i] is not null) _usrLabels[i].Text = e.Username ?? "";
			if (_amtLabels[i] is not null) _amtLabels[i].Text = e.Detail ?? "";
			var slot = _winnerDisplays?[i];
			GD.Print($"[WinningDiag] Slide2.Populate[{i}] peer={e.PeerId} slot={(slot is null ? "NULL" : "ok")}");
			if (slot is null) continue;
			try { slot.Show(e.PeerId); }
			catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide2[{i}].Show threw: {ex}"); continue; }
			try { slot.BindNetwork(e.PeerId); }
			catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide2[{i}].BindNetwork threw: {ex}"); continue; }
			if (_previewContainers?[i] is not null)
			{
				try { slot.BindMouseInput(_previewContainers[i]); }
				catch (System.Exception ex) { GD.PrintErr($"[WinningDiag] Slide2[{i}].BindMouseInput threw: {ex}"); }
			}
		}
	}

	public override void _Ready()
	{
		_continueButton ??= GetNodeOrNull<Button>("SubwinController/SubWinPanel3/ContinueBar/ContinueButton");
		if (_continueButton is not null)
		{
			_continueButton.Visible = false;
			_continueButton.Pressed += OnContinuePressed;
		}

		_panels = new Panel[3];
		_titleLabels = new Label[3];
		_usrLabels = new Label[3];
		_amtLabels = new Label[3];

		// Panel 1 (le plus important — gagnant principal des sous-conditions).
		_panels[0] = GetNodeOrNull<Panel>("SubwinController/SubWinPanel1");
		_titleLabels[0] = GetNodeOrNull<Label>("SubwinController/SubWinPanel1/VBoxContainer/Title");
		_usrLabels[0] = GetNodeOrNull<Label>("SubwinController/SubWinPanel1/VBoxContainer/usr_label");
		_amtLabels[0] = GetNodeOrNull<Label>("SubwinController/SubWinPanel1/VBoxContainer/amt_label");

		_panels[1] = GetNodeOrNull<Panel>("SubwinController/SubWinPanel2");
		_titleLabels[1] = GetNodeOrNull<Label>("SubwinController/SubWinPanel2/VBoxContainer2/Title2");
		_usrLabels[1] = GetNodeOrNull<Label>("SubwinController/SubWinPanel2/VBoxContainer2/usr_label");
		_amtLabels[1] = GetNodeOrNull<Label>("SubwinController/SubWinPanel2/VBoxContainer2/amt_label");

		_panels[2] = GetNodeOrNull<Panel>("SubwinController/SubWinPanel3");
		_titleLabels[2] = GetNodeOrNull<Label>("SubwinController/SubWinPanel3/VBoxContainer3/Title3");
		_usrLabels[2] = GetNodeOrNull<Label>("SubwinController/SubWinPanel3/VBoxContainer3/usr_label");
		_amtLabels[2] = GetNodeOrNull<Label>("SubwinController/SubWinPanel3/VBoxContainer3/amt_label");

		_winnerDisplays = new Preview[3];
		_winnerDisplays[0] = GetNodeOrNull<Preview>(
			"SubwinController/SubWinPanel1/VBoxContainer/SubViewportContainer/Winner1Viewport/WinnerDisplay");
		_winnerDisplays[1] = GetNodeOrNull<Preview>(
			"SubwinController/SubWinPanel2/VBoxContainer2/SubViewportContainer/Winner2Viewport/WinnerDisplay");
		_winnerDisplays[2] = GetNodeOrNull<Preview>(
			"SubwinController/SubWinPanel3/VBoxContainer3/SubViewportContainer/Winner3Viewport/WinnerDisplay");

		_previewContainers = new SubViewportContainer[3];
		_previewContainers[0] = GetNodeOrNull<SubViewportContainer>(
			"SubwinController/SubWinPanel1/VBoxContainer/SubViewportContainer");
		_previewContainers[1] = GetNodeOrNull<SubViewportContainer>(
			"SubwinController/SubWinPanel2/VBoxContainer2/SubViewportContainer");
		_previewContainers[2] = GetNodeOrNull<SubViewportContainer>(
			"SubwinController/SubWinPanel3/VBoxContainer3/SubViewportContainer");

		// État initial&#160;: les panneaux restent visibles avec leur texte de scène
		// (placeholders) jusqu'à ce que Populate() les remplace ou les masque.
    }

    private void OnContinuePressed() => EmitSignal(SignalName.ContinuePressed);
}
