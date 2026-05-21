using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Core.Network.Rooms;

public sealed class RoomRepository
{
	private static readonly HttpClient Http = new();
	private const string BaseUrl = "https://the-mountain-game-default-rtdb.firebaseio.com/rooms";

	private readonly Func<string> _getToken;

	public RoomRepository(Func<string> getToken)
	{
		_getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
	}

	public async Task CreateAsync(Room room, string? serverIpOverride = null)
	{
		var token = _getToken();

		var payload = new
		{
			code       = room.Code,
			hostUserId = room.HostUserId,
			serverIp   = serverIpOverride ?? Room.HardcodedServerIp,
			serverPort = Room.HardcodedServerPort,
			status     = Room.DefaultStatus,
			maxPlayers = Room.DefaultMaxPlayers,
			mapId      = Room.DefaultMapId,
			players    = new Dictionary<string, PlayerEntryDto>
			{
				[room.HostUserId] = new PlayerEntryDto(room.HostUsername, true, HatRegistry.DefaultHatId)
			}
		};

		var body    = JsonSerializer.Serialize(payload);
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(room.Code)}.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");

		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	public async Task<RoomSnapshot?> GetAsync(string code)
	{
		var token = _getToken();
		var response = await Http.GetAsync(
			$"{BaseUrl}/{Uri.EscapeDataString(code)}.json?auth={token}");

		if (!response.IsSuccessStatusCode) return null;

		var json = await response.Content.ReadAsStringAsync();
		if (json == "null" || string.IsNullOrWhiteSpace(json)) return null;

		var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var dto  = JsonSerializer.Deserialize<RoomDto>(json, opts);
		if (dto is null) return null;

		var players = new Dictionary<string, RoomSnapshot.PlayerEntry>();
		if (dto.Players is not null)
			foreach (var (uid, p) in dto.Players)
				players[uid] = new RoomSnapshot.PlayerEntry
				{
					Username = p.Username ?? "",
					IsHost = p.IsHost,
					HatId = string.IsNullOrEmpty(p.HatId) ? HatRegistry.DefaultHatId : p.HatId
				};

		return new RoomSnapshot
		{
			Code       = dto.Code       ?? code,
			HostUserId = dto.HostUserId ?? "",
			ServerIp   = dto.ServerIp   ?? Room.HardcodedServerIp,
			ServerPort = dto.ServerPort > 0 ? dto.ServerPort : Room.HardcodedServerPort,
			Status     = dto.Status     ?? Room.DefaultStatus,
			MaxPlayers = dto.MaxPlayers > 0 ? dto.MaxPlayers : Room.DefaultMaxPlayers,
			MapId      = dto.MapId      ?? Room.DefaultMapId,
			Players    = players
		};
	}

	public async Task AddPlayerAsync(string code, string userId, string username, bool isHost = false)
	{
		var token = _getToken();
		var body  = JsonSerializer.Serialize(new { username, isHost, hatId = HatRegistry.DefaultHatId });
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/players/{Uri.EscapeDataString(userId)}.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	/// <summary>
	/// Met à jour le champ <c>hostUserId</c> de la salle. Utilisé quand un
	/// joueur prend la place d'un hôte zombie (cf. <c>MainMenu.OnPlayPressed</c>).
	/// </summary>
	public async Task UpdateHostAsync(string code, string hostUserId)
	{
		var token = _getToken();
		var body  = JsonSerializer.Serialize(hostUserId);
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/hostUserId.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	/// <summary>
	/// Met à jour le chapeau cosmétique du joueur <paramref name="userId"/>
	/// dans la salle <paramref name="code"/>. Chaque joueur n'écrit que sa
	/// propre entrée — pas de check d'autorisation côté DB&#160;: on s'appuie
	/// sur le fait que le client ne pousse que pour son propre userId.
	/// </summary>
	public async Task UpdateHatAsync(string code, string userId, string hatId)
	{
		var token = _getToken();
		var body  = JsonSerializer.Serialize(hatId);
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/players/{Uri.EscapeDataString(userId)}/hatId.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	public async Task RemovePlayerAsync(string code, string userId)
	{
		var token = _getToken();
		var request = new HttpRequestMessage(HttpMethod.Delete,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/players/{Uri.EscapeDataString(userId)}.json?auth={token}");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	public async Task UpdateMapAsync(string code, string mapId)
	{
		var token = _getToken();
		var body  = JsonSerializer.Serialize(mapId);
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/mapId.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	public async Task UpdateStatusAsync(string code, string status)
	{
		var token = _getToken();
		var body  = JsonSerializer.Serialize(status);
		var request = new HttpRequestMessage(HttpMethod.Put,
			$"{BaseUrl}/{Uri.EscapeDataString(code)}/status.json?auth={token}");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		var response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
	}

	// --- DTOs ---

	private sealed class RoomDto
	{
		[JsonPropertyName("code")]       public string? Code       { get; set; }
		[JsonPropertyName("hostUserId")] public string? HostUserId { get; set; }
		[JsonPropertyName("serverIp")]   public string? ServerIp   { get; set; }
		[JsonPropertyName("serverPort")] public int     ServerPort  { get; set; }
		[JsonPropertyName("status")]     public string? Status      { get; set; }
		[JsonPropertyName("maxPlayers")] public int     MaxPlayers  { get; set; }
		[JsonPropertyName("mapId")]      public string? MapId       { get; set; }
		[JsonPropertyName("players")]    public Dictionary<string, PlayerEntryDto>? Players { get; set; }
	}

	private sealed record PlayerEntryDto(
		[property: JsonPropertyName("username")] string? Username,
		[property: JsonPropertyName("isHost")]   bool    IsHost,
		[property: JsonPropertyName("hatId")]    string? HatId = HatRegistry.DefaultHatId);
}
