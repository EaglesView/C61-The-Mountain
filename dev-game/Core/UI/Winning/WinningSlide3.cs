using Godot;
using System.Collections.Generic;
using Core.Network.Rooms;

/// <summary>
/// Troisième slide de l'écran Winning (<c>SCR_3_VOTEMAP</c>)&#160;: panneau de vote
/// pour la prochaine map et liste des joueurs connectés. À l'entrée, peuple la
/// grille avec les maps de <c>MapRegistry</c> et la colonne lobby avec
/// <c>LobbyState.Current.Players</c>. Les nœuds échantillons présents dans la
/// scène servent de templates et sont préservés. La logique de vote
/// (réseau&#160;+ majorité) vit dans <c>WinningController</c>&#160;: la slide se
/// contente d'émettre <see cref="MapVoteCast"/>/<see cref="VoteConfirmed"/> et
/// de refléter l'état que le contrôleur lui pousse via
/// <see cref="ApplyTally"/> / <see cref="SetCanConfirm"/>.
/// </summary>
public sealed partial class WinningSlide3 : MarginContainer
{
	/// <summary>Émis quand un joueur clique sur une carte (vote local).</summary>
	[Signal] public delegate void MapVoteCastEventHandler(string InMapId);

	/// <summary>Émis quand l'hôte confirme le résultat du vote (bouton CONFIRM).</summary>
	[Signal] public delegate void VoteConfirmedEventHandler(string InMapId);

	/// <summary>Émis quand l'utilisateur clique sur QUIT (sortie volontaire).</summary>
	[Signal] public delegate void QuitPressedEventHandler();

	private GridContainer _mapGrid;
	private VBoxContainer _lobbyList;
	private Button _quitButton;
	private Button _confirmButton;
	private MapGridItem _mapItemTemplate;
	private LobbyUser _lobbyUserTemplate;
	private PackedScene _mapItemScene;
	private PackedScene _lobbyUserScene;

	private string _selectedMapId = "";
	private string _majorityMapId = "";
	private bool _isHost;
	private readonly List<MapGridItem> _spawnedMapItems = new();
	private readonly List<LobbyUser> _spawnedLobbyUsers = new();
	private bool _populated;

	/// <summary>Identifiant de la map sélectionnée localement (vide si aucune).</summary>
	public string SelectedMapId => _selectedMapId;

	/// <summary>Identifiant de la map majoritaire publié par le contrôleur (vide si aucune).</summary>
	public string MajorityMapId => _majorityMapId;

	/// <summary>
	/// Construit la grille de maps et la liste des joueurs. Idempotent&#160;: les
	/// instances spawned d'un précédent affichage sont d'abord nettoyées.
	/// </summary>
	public void Populate()
	{
		ClearSpawned();

		foreach (var def in MapRegistry.All)
		{
			MapGridItem item;
			if (_mapItemScene is not null) item = _mapItemScene.Instantiate<MapGridItem>();
			else if (_mapItemTemplate is not null) item = (MapGridItem)_mapItemTemplate.Duplicate();
			else continue;

			_mapGrid.AddChild(item);
			item.SetMap(def.Id, def.DisplayName, def.ImagePath);
			item.SetVoteCount(0);
			item.Selected += OnMapSelected;
			_spawnedMapItems.Add(item);
		}

		var snapshot = LobbyState.Current;
		if (snapshot is not null)
		{
			foreach (var (_, player) in snapshot.Players)
			{
				LobbyUser user;
				if (_lobbyUserScene is not null) user = _lobbyUserScene.Instantiate<LobbyUser>();
				else if (_lobbyUserTemplate is not null) user = (LobbyUser)_lobbyUserTemplate.Duplicate();
				else continue;

				_lobbyList.AddChild(user);
				user.SetUser(player.Username, player.IsHost);
				_spawnedLobbyUsers.Add(user);
			}
		}

		_isHost = LobbyState.IsHost;
		RefreshConfirmButton();
		_populated = true;
	}

	/// <summary>
	/// Détruit les items spawned à l'entrée précédente (les templates de la scène
	/// sont conservés). Doit être appelé en sortie de slide.
	/// </summary>
	public void ClearSpawned()
	{
		foreach (var item in _spawnedMapItems) if (IsInstanceValid(item)) item.QueueFree();
		foreach (var user in _spawnedLobbyUsers) if (IsInstanceValid(user)) user.QueueFree();
		_spawnedMapItems.Clear();
		_spawnedLobbyUsers.Clear();
		_selectedMapId = "";
		_majorityMapId = "";
		_populated = false;
	}

	/// <summary>
	/// Met à jour le compteur de votes par map et le candidat majoritaire&#160;:
	/// rafraîchit les <c>VoteCountLabel</c> et l'état du bouton CONFIRM.
	/// </summary>
	public void ApplyTally(Godot.Collections.Dictionary<string, int> InCounts, string InMajorityMapId)
	{
		_majorityMapId = InMajorityMapId ?? "";
		foreach (var item in _spawnedMapItems)
		{
			int count = InCounts is not null && InCounts.ContainsKey(item.MapId) ? (int)InCounts[item.MapId] : 0;
			item.SetVoteCount(count);
		}
		RefreshConfirmButton();
	}

	/// <summary>Indique si ce client est l'hôte (autorisé à confirmer le vote).</summary>
	public void SetIsHost(bool InIsHost)
	{
		_isHost = InIsHost;
		RefreshConfirmButton();
	}

	public override void _Ready()
	{
		_mapGrid = GetNodeOrNull<GridContainer>("Panel/HBoxContainer/LeftPanel/VBoxContainer/ScrollContainer/GridContainer");
		_lobbyList = GetNodeOrNull<VBoxContainer>("Panel/HBoxContainer/RightPanel/MainLobbyPanel/VBoxContainer/LobbyPanel/VBoxContainer");
		_quitButton = GetNodeOrNull<Button>("Panel/HBoxContainer/LeftPanel/VBoxContainer/BottomControls/ButtonMargin1/QuitButton");
		_confirmButton = GetNodeOrNull<Button>("Panel/HBoxContainer/LeftPanel/VBoxContainer/BottomControls/ButtonMargin3/Button");

		_mapItemTemplate = _mapGrid?.GetNodeOrNull<MapGridItem>("MapGridItem");
		_lobbyUserTemplate = _lobbyList?.GetNodeOrNull<LobbyUser>("LobbyUser");

		_mapItemScene = ResourceLoader.Load<PackedScene>("res://Core/UI/Winning/map_grid_item.tscn");
		_lobbyUserScene = ResourceLoader.Load<PackedScene>("res://Core/UI/Winning/lobby_user.tscn");

		if (_quitButton is not null) _quitButton.Pressed += OnQuitPressed;
		if (_confirmButton is not null)
		{
			_confirmButton.Pressed += OnConfirmPressed;
			_confirmButton.Disabled = true;
		}
	}

	private void OnMapSelected(string InMapId)
	{
		_selectedMapId = InMapId;
		foreach (var item in _spawnedMapItems) item.SetSelected(item.MapId == InMapId);
		EmitSignal(SignalName.MapVoteCast, InMapId);
	}

	private void OnConfirmPressed() => EmitSignal(SignalName.VoteConfirmed, _majorityMapId);
	private void OnQuitPressed() => EmitSignal(SignalName.QuitPressed);

	/// <summary>
	/// Le bouton CONFIRM n'est cliquable que si&#160;: ce client est l'hôte
	/// <em>et</em> une majorité a été atteinte sur une map. Pour les autres
	/// joueurs, le bouton reste désactivé&#160;: le texte rappelle déjà «&#160;(HOST)&#160;».
	/// </summary>
	private void RefreshConfirmButton()
	{
		if (_confirmButton is null) return;
		_confirmButton.Disabled = !(_isHost && !string.IsNullOrEmpty(_majorityMapId));
	}
}
