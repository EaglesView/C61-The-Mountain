using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using HttpClient = System.Net.Http.HttpClient;
using System.Text.Json;
using System.Threading.Tasks;
using Config;
using Core.Auth;
using Core.Network.Rooms;

public partial class CreditsScene : Control
{
	[Export] private Control _creditsContent;
	[Export] private Button _backButton;
	[Export] private GridContainer _developersGrid;
	[Export] private GridContainer _testersGrid;

	private const float ScrollSpeed = 45.0f;
	private const float InitialTopOffset = 16.0f;
	private const int MaxVisibleTesterCards = 16;
	private const string PreviewScenePath = "res://Core/UI/Preview/preview.tscn";
	private static readonly HttpClient Http = new();
	private static readonly Random Rng = new();
	private static readonly HashSet<string> DeveloperUsernames =
		new(StringComparer.OrdinalIgnoreCase) { "sam", "jim" };
	private static readonly string[] HardcodedDevelopers = { "sam", "jim" };

	private readonly List<PersonCard> _developerCards = new();
	private readonly List<PersonCard> _testerCards = new();
	private float _viewportHeight;

	public override void _Ready()
	{
		if (_creditsContent == null) _creditsContent = GetNode<Control>("ClipContainer/CreditsContent");
		if (_backButton == null) _backButton = GetNode<Button>("BackButton");
		if (_developersGrid == null) _developersGrid = GetNodeOrNull<GridContainer>("ClipContainer/CreditsContent/DevelopersGrid");
		if (_testersGrid == null) _testersGrid = GetNodeOrNull<GridContainer>("ClipContainer/CreditsContent/TestersGrid");

		CollectCards(_developersGrid, _developerCards, "Développeur");
		CollectCards(_testersGrid, _testerCards, "Testeur");
		ApplyLoadingState(_developerCards, "Développeur");
		ApplyLoadingState(_testerCards, "Testeur");
		_ = LoadProfilesAsync();

		_viewportHeight = GetViewportRect().Size.Y;
		_creditsContent.Position = new Vector2(_creditsContent.Position.X, InitialTopOffset);
		_backButton.Pressed += GoBack;
	}

	public override void _Process(double delta)
	{
		var pos = _creditsContent.Position;
		pos.Y -= ScrollSpeed * (float)delta;
		_creditsContent.Position = pos;

		if (pos.Y + _creditsContent.Size.Y < 0)
			_creditsContent.Position = new Vector2(pos.X, _viewportHeight);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("pause_menu"))
			GoBack();
	}

	private void GoBack()
	{
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}

	private void CollectCards(GridContainer? grid, List<PersonCard> target, string defaultRole)
	{
		target.Clear();
		if (grid is null) return;

		foreach (Node child in grid.GetChildren())
		{
			if (child is not Control cardRoot) continue;
			var viewportContainer = cardRoot.GetNodeOrNull<SubViewportContainer>("ViewportContainer");
			var viewport = viewportContainer?.GetNodeOrNull<SubViewport>("PreviewViewport");
			var roleLabel = cardRoot.GetNodeOrNull<Label>("RoleLabel");
			var nameLabel = cardRoot.GetNodeOrNull<Label>("NameLabel");
			if (viewportContainer is null || viewport is null || roleLabel is null || nameLabel is null)
				continue;

			var preview = GetOrCreatePreview(viewportContainer, viewport);
			if (roleLabel.Text == "loading..." || string.IsNullOrWhiteSpace(roleLabel.Text))
				roleLabel.Text = defaultRole;

			target.Add(new PersonCard
			{
				Root = cardRoot,
				RoleLabel = roleLabel,
				NameLabel = nameLabel,
				Preview = preview
			});
		}
	}

	private Preview? BuildPreview(SubViewportContainer container, SubViewport viewport)
	{
		var packed = ResourceLoader.Load<PackedScene>(PreviewScenePath);
		if (packed is null)
		{
			GD.PrintErr($"[Credits] Preview scene introuvable: {PreviewScenePath}");
			return null;
		}

		var preview = packed.Instantiate<Preview>();
		preview.Mode = Preview.InteractionMode.MouseLook;
		viewport.AddChild(preview);
		preview.SpawnCharacter(HatRegistry.DefaultHatId);
		preview.BindMouseInput(container);
		return preview;
	}

	private static void ApplyLoadingState(List<PersonCard> cards, string role)
	{
		for (int i = 0; i < cards.Count; i++)
		{
			ApplyCard(cards[i], role, "loading...", HatRegistry.DefaultHatId);
		}
	}

	private async Task LoadProfilesAsync()
	{
		ApplyHardcodedDevelopers();

		try
		{
			var profilesByUserId = await LoadAllProfilesByUserIdAsync();
			var allProfiles = new List<Core.Profile.Domain.PlayerProfile>(profilesByUserId.Values);
			allProfiles.Sort((a, b) => string.Compare(a.Username, b.Username, StringComparison.OrdinalIgnoreCase));

			var testers = new List<Core.Profile.Domain.PlayerProfile>();
			foreach (var profile in allProfiles)
			{
				if (!DeveloperUsernames.Contains(profile.Username))
					testers.Add(profile);
			}

			if (testers.Count > MaxVisibleTesterCards)
			{
				GD.Print($"[Credits] {testers.Count} testeurs trouvés, affichage limité à {MaxVisibleTesterCards} pour préserver les performances.");
				testers = testers.GetRange(0, MaxVisibleTesterCards);
			}

			EnsureEnoughCards(_testersGrid, _testerCards, testers.Count);
			ApplyProfilesToCards(_testerCards, testers, "Testeur");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Credits] Failed to load Firebase testers profiles: {ex.Message}");
		}
	}

	private static void ApplyProfilesToCards(
		List<PersonCard> cards,
		List<Core.Profile.Domain.PlayerProfile> profiles,
		string role)
	{
		for (int i = 0; i < cards.Count; i++)
		{
			if (i < profiles.Count)
				ApplyCard(cards[i], role, profiles[i].Username, profiles[i].SelectedHatId);
			else
				ApplyCard(cards[i], role, "-", HatRegistry.DefaultHatId);
		}
	}

	private void ApplyHardcodedDevelopers()
	{
		for (int i = 0; i < _developerCards.Count; i++)
		{
			string username = i < HardcodedDevelopers.Length ? HardcodedDevelopers[i] : "-";
			ApplyCard(_developerCards[i], "Développeur", username, GetRandomHatId());
		}
	}

	private static string GetRandomHatId()
	{
		if (HatRegistry.All is null || HatRegistry.All.Length == 0)
			return HatRegistry.DefaultHatId;

		var pool = new List<string>(HatRegistry.All.Length);
		foreach (var hat in HatRegistry.All)
		{
			if (hat is null || string.IsNullOrWhiteSpace(hat.Id)) continue;
			if (hat.Id == HatRegistry.NoneHatId) continue;
			pool.Add(hat.Id);
		}

		if (pool.Count == 0) return HatRegistry.DefaultHatId;
		return pool[Rng.Next(pool.Count)];
	}

	private static void ApplyCard(PersonCard card, string role, string username, string hatId)
	{
		if (card.RoleLabel is not null) card.RoleLabel.Text = role;

		if (card.NameLabel is not null)
		{
			card.NameLabel.Text = username;
		}

		if (card.Preview is not null)
			card.Preview.SetHat(string.IsNullOrWhiteSpace(hatId) ? HatRegistry.DefaultHatId : hatId);
	}

	private static async Task<Dictionary<string, Core.Profile.Domain.PlayerProfile>> LoadAllProfilesByUserIdAsync()
	{
		var result = new Dictionary<string, Core.Profile.Domain.PlayerProfile>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(FirebaseConfig.ProjectId)) return result;

		string token;
		try
		{
			token = AuthServiceProvider.GetCurrentToken();
		}
		catch
		{
			return result;
		}

		// claude qui a gen ca. Test pour voir si ca se ferait avec L'IA
		// ca ici cest vraiment bad comment cest fait.
		// TODO faut refaire ce fichier la et lintegrer au clean architecture de la db
		using var req = new HttpRequestMessage(HttpMethod.Get,
			$"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/profiles");
		req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

		using var res = await Http.SendAsync(req);
		if (!res.IsSuccessStatusCode) return result;

		var body = await res.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(body);
		if (!doc.RootElement.TryGetProperty("documents", out var docs) || docs.ValueKind != JsonValueKind.Array)
			return result;

		foreach (var node in docs.EnumerateArray())
		{
			if (!node.TryGetProperty("name", out var nameEl)) continue;
			var fullName = nameEl.GetString() ?? "";
			int slash = fullName.LastIndexOf('/');
			if (slash < 0 || slash + 1 >= fullName.Length) continue;
			var userId = fullName[(slash + 1)..];

			if (!node.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object) continue;
			string username = "";
			string hatId = HatRegistry.DefaultHatId;

			if (fields.TryGetProperty("username", out var usernameNode)
				&& usernameNode.TryGetProperty("stringValue", out var usernameVal))
			{
				username = usernameVal.GetString() ?? "";
			}

			if (fields.TryGetProperty("hatId", out var hatNode)
				&& hatNode.TryGetProperty("stringValue", out var hatVal)
				&& !string.IsNullOrWhiteSpace(hatVal.GetString()))
			{
				hatId = hatVal.GetString() ?? HatRegistry.DefaultHatId;
			}

			if (string.IsNullOrWhiteSpace(username)) continue;
			result[userId] = new Core.Profile.Domain.PlayerProfile(userId, username, hatId);
		}

		return result;
	}

	private void EnsureEnoughCards(GridContainer? grid, List<PersonCard> cards, int requiredCount)
	{
		if (grid is null || cards.Count >= requiredCount) return;

		if (cards.Count == 0) return;

		var templateCard = cards[0].Root;
		if (templateCard is null) return;

		while (cards.Count < requiredCount)
		{
			var clonedCard = (Control?)templateCard.Duplicate();
			if (clonedCard is null) break;

			grid.AddChild(clonedCard);
			var viewportContainer = clonedCard.GetNodeOrNull<SubViewportContainer>("ViewportContainer");
			var viewport = viewportContainer?.GetNodeOrNull<SubViewport>("PreviewViewport");
			var roleLabel = clonedCard.GetNodeOrNull<Label>("RoleLabel");
			var nameLabel = clonedCard.GetNodeOrNull<Label>("NameLabel");

			if (viewportContainer is null || viewport is null || roleLabel is null || nameLabel is null)
			{
				clonedCard.QueueFree();
				break;
			}

			var preview = RecreatePreview(viewportContainer, viewport);
			cards.Add(new PersonCard
			{
				Root = clonedCard,
				RoleLabel = roleLabel,
				NameLabel = nameLabel,
				Preview = preview
			});
		}
	}

	private Preview? RecreatePreview(SubViewportContainer container, SubViewport viewport)
	{
		foreach (Node child in viewport.GetChildren())
		{
			if (child is not Preview previewChild) continue;

			previewChild.GetParent()?.RemoveChild(previewChild);
			previewChild.QueueFree();
		}

		return BuildPreview(container, viewport);
	}

	private Preview? GetOrCreatePreview(SubViewportContainer container, SubViewport viewport)
	{
		Preview? firstPreview = null;

		foreach (Node child in viewport.GetChildren())
		{
			if (child is not Preview previewChild) continue;

			if (firstPreview is null)
			{
				firstPreview = previewChild;
				continue;
			}

			previewChild.QueueFree();
		}

		if (firstPreview is not null)
		{
			firstPreview.Mode = Preview.InteractionMode.MouseLook;
			firstPreview.BindMouseInput(container);
			return firstPreview;
		}

		return BuildPreview(container, viewport);
	}

	private sealed class PersonCard
	{
		public Control? Root { get; init; }
		public Label? RoleLabel { get; init; }
		public Label? NameLabel { get; init; }
		public Preview? Preview { get; init; }
	}
}
