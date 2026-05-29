using Godot;
using System.Collections.Generic;
using Core.Auth;
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
	private readonly Dictionary<string, LobbyUser> _lobbyUsersByUserId = new();
	private bool _populated;

	// Clé synthétique utilisée pour l'unique entrée «&#160;moi&#160;» quand on est en
	// mode solo/offline : pas de RoomSnapshot, donc pas de userId Firestore.
	private const string OfflineLocalKey = "__local__";

	// Re-interroge périodiquement la salle Firestore pour rafraîchir la liste
	// des joueurs (un peer qui se déconnecte/connecte pendant Winning doit
	// disparaître/apparaître). Aligné sur la cadence du LobbyScene.
	private const float PollInterval = 4.0f;
	private Timer _pollTimer;

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
			item.CustomMinimumSize = new Vector2(400, 250);
			item.Visible = true;
			item.SetMap(def.Id, def.DisplayName, def.ImagePath);
			item.SetVoteCount(0);
			item.Selected += OnMapSelected;
			_spawnedMapItems.Add(item);
		}

		var snapshot = LobbyState.Current;
		if (snapshot is not null && snapshot.Players.Count > 0)
		{
			RefreshLobbyUsersFromSnapshot(snapshot);
		}
		else
		{
			// Offline/solo&#160;: pas de RoomSnapshot, on retombe sur le nom d'usager Firebase
			// authentifié (ou un placeholder si pas d'auth) pour ne pas laisser la
			// colonne lobby vide.
			string localUsername = AuthServiceProvider.Instance.CurrentUser?.Username ?? "You";
			var user = InstantiateLobbyUser();
			if (user is not null)
			{
				_lobbyList.AddChild(user);
				user.CustomMinimumSize = new Vector2(380, 32);
				user.Visible = true;
				user.SetUser(localUsername, true);
				_lobbyUsersByUserId[OfflineLocalKey] = user;
			}
		}

		_isHost = LobbyState.IsHost;
		RefreshConfirmButton();
		_populated = true;

		StartPollingIfNeeded();
	}

	private LobbyUser InstantiateLobbyUser()
	{
		if (_lobbyUserScene is not null) return _lobbyUserScene.Instantiate<LobbyUser>();
		if (_lobbyUserTemplate is not null) return (LobbyUser)_lobbyUserTemplate.Duplicate();
		return null;
	}

	/// <summary>
	/// Diffe la colonne lobby contre <paramref name="InSnapshot"/>&#160;: retire les
	/// joueurs partis, ajoute les nouveaux, et met à jour le label des joueurs
	/// déjà présents (changement de username/host). Idempotent — sûr à appeler
	/// à chaque tick de poll sans flicker.
	/// </summary>
	private void RefreshLobbyUsersFromSnapshot(RoomSnapshot InSnapshot)
	{
		var currentIds = new HashSet<string>(InSnapshot.Players.Keys);
		var toRemove = new List<string>();
		foreach (var (userId, _) in _lobbyUsersByUserId)
		{
			if (!currentIds.Contains(userId)) toRemove.Add(userId);
		}
		foreach (var userId in toRemove)
		{
			var node = _lobbyUsersByUserId[userId];
			if (IsInstanceValid(node)) node.QueueFree();
			_lobbyUsersByUserId.Remove(userId);
		}

		foreach (var (userId, player) in InSnapshot.Players)
		{
			if (_lobbyUsersByUserId.TryGetValue(userId, out var existing))
			{
				if (IsInstanceValid(existing)) existing.SetUser(player.Username, player.IsHost);
				continue;
			}
			var user = InstantiateLobbyUser();
			if (user is null) continue;
			_lobbyList.AddChild(user);
			user.CustomMinimumSize = new Vector2(380, 32);
			user.Visible = true;
			user.SetUser(player.Username, player.IsHost);
			_lobbyUsersByUserId[userId] = user;
		}
	}

	private void StartPollingIfNeeded()
	{
		// Pas de salle Firestore associée (mode solo/offline) -> rien à poller.
		var snapshot = LobbyState.Current;
		if (snapshot is null || string.IsNullOrEmpty(snapshot.Code)) return;
		if (_pollTimer is not null && IsInstanceValid(_pollTimer)) return;

		_pollTimer = new Timer { WaitTime = PollInterval, Autostart = true };
		_pollTimer.Timeout += OnPollTick;
		AddChild(_pollTimer);
	}

	private void StopPolling()
	{
		if (_pollTimer is null) return;
		if (IsInstanceValid(_pollTimer))
		{
			_pollTimer.Timeout -= OnPollTick;
			_pollTimer.Stop();
			_pollTimer.QueueFree();
		}
		_pollTimer = null;
	}

	private async void OnPollTick()
	{
		if (!_populated || !IsInsideTree()) return;
		var snapshot = LobbyState.Current;
		if (snapshot is null || string.IsNullOrEmpty(snapshot.Code)) return;

		try
		{
			var fresh = await RoomServiceProvider.Repository.GetAsync(snapshot.Code);
			// La slide peut avoir été clearée / sortie de l'arbre pendant l'await.
			if (!_populated || !IsInsideTree()) return;
			if (fresh is null) return;
			LobbyState.Set(fresh, LobbyState.IsHost);
			RefreshLobbyUsersFromSnapshot(fresh);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[WinningSlide3] Poll failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Détruit les items spawned à l'entrée précédente (les templates de la scène
	/// sont conservés). Doit être appelé en sortie de slide.
	/// </summary>
	public void ClearSpawned()
	{
		StopPolling();
		foreach (var item in _spawnedMapItems) if (IsInstanceValid(item)) item.QueueFree();
		foreach (var (_, user) in _lobbyUsersByUserId) if (IsInstanceValid(user)) user.QueueFree();
		_spawnedMapItems.Clear();
		_lobbyUsersByUserId.Clear();
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

		// Les templates qui vivent dans la scène servent uniquement de référence
		// visuelle dans l'éditeur&#160;: on les cache à l'exécution pour ne pas
		// laisser un item «&#160;vide&#160;» (mapId="") cliquable au-dessus des items
		// instanciés depuis MapRegistry.
		if (_mapItemTemplate is not null) _mapItemTemplate.Visible = false;
		if (_lobbyUserTemplate is not null) _lobbyUserTemplate.Visible = false;

		// Bouton intermédiaire «&#160;VOTE SELECTED&#160;» laissé en place dans la
		// scène mais sans rôle&#160;: la sélection d'une map vaut déjà vote (cf.
		// OnMapSelected). On le masque pour éviter le bouton-mort visible.
		var voteSelectedBtn = GetNodeOrNull<Button>("Panel/HBoxContainer/LeftPanel/VBoxContainer/BottomControls/ButtonMargin2/Button");
		if (voteSelectedBtn is not null) voteSelectedBtn.Visible = false;

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
