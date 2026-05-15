using Godot;

/// <summary>
/// Item d'une grille de votes pour le choix de la prochaine map.
/// Affiche un aperçu image, le nom de la map et le nombre de votes courants.
/// Un clic émet <see cref="Selected"/> avec l'identifiant de la map associée.
/// </summary>
public sealed partial class MapGridItem : Control
{
	/// <summary>Émis quand l'item est cliqué.</summary>
	[Signal] public delegate void SelectedEventHandler(string InMapId);

	private TextureRect _mapImage;
	private Label _mapNameLabel;
	private Label _voteCountLabel;
	private string _mapId = "";

	/// <summary>Identifiant de la map portée par cet item (cf. <c>MapRegistry</c>).</summary>
	public string MapId => _mapId;

	/// <summary>État sélectionné&#160;: change la teinte de modulation pour le distinguer.</summary>
	public bool IsSelected { get; private set; }

	/// <summary>
	/// Applique les données de la map à l'item. À appeler après instanciation,
	/// avant que l'item ne soit visible. Si <paramref name="InImagePath"/> ne
	/// résout pas une texture valide, l'aperçu placeholder du .tscn est
	/// conservé.
	/// </summary>
	public void SetMap(string InMapId, string InDisplayName, string InImagePath)
	{
		_mapId = InMapId;
		if (_mapNameLabel is not null) _mapNameLabel.Text = InDisplayName;
		if (_mapImage is not null && !string.IsNullOrEmpty(InImagePath))
		{
			var tex = ResourceLoader.Load<Texture2D>(InImagePath);
			if (tex is not null) _mapImage.Texture = tex;
		}
	}

	/// <summary>Met à jour le compteur de votes affiché sous le nom de la map.</summary>
	public void SetVoteCount(int InCount)
	{
		if (_voteCountLabel is null) return;
		_voteCountLabel.Text = InCount == 1 ? "1 vote" : $"{InCount} votes";
	}

	/// <summary>Met à jour l'état sélectionné et le visuel associé.</summary>
	public void SetSelected(bool InValue)
	{
		IsSelected = InValue;
		Modulate = InValue ? new Color(1f, 1f, 1f, 1f) : new Color(0.65f, 0.65f, 0.65f, 1f);
	}

	public override void _Ready()
	{
		_mapImage = GetNodeOrNull<TextureRect>("MarginContainer/MapPanel/VBoxContainer/MapImage");
		_mapNameLabel = GetNodeOrNull<Label>("MarginContainer/MapPanel/VBoxContainer/MapNameLabel");
		_voteCountLabel = GetNodeOrNull<Label>("MarginContainer/MapPanel/VBoxContainer/VoteCountLabel");
		MouseFilter = MouseFilterEnum.Stop;
		SetSelected(false);
	}

	public override void _GuiInput(InputEvent InEvent)
	{
		if (InEvent is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			EmitSignal(SignalName.Selected, _mapId);
			AcceptEvent();
		}
	}
}
