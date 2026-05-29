using System;
using System.Collections.Generic;

namespace Core.Network.Rooms;

/// <summary>
/// Mapping mémoire <c>userId</c> Firestore ↔ <c>peerId</c> ENet, populé pendant
/// la phase Lobby par le handshake d'identité de <see cref="Core.World.LobbyController"/>
/// (RPC <c>ServerReceiveIdentity</c>&#160;/&#160;<c>ClientReceiveIdentity</c>).
/// Utilisé par les UI qui veulent câbler un slot keyé par <c>userId</c>
/// (<c>LobbyScene</c>) sur la pipeline réseau keyée par <c>peerId</c>
/// (<c>Preview.BindNetwork</c>). Vidé à l'entrée de la phase Lobby pour ne pas
/// traîner d'entrées zombie d'une session précédente.
/// </summary>
public static class LobbyPresence
{
	private static readonly Dictionary<string, int> _peerByUserId = new();
	private static readonly Dictionary<int, string> _userIdByPeer = new();

	/// <summary>
	/// Émis après chaque modification du mapping (ajout, swap, suppression,
	/// clear). Les consommateurs UI (<c>LobbyScene</c>) s'abonnent pour câbler
	/// les Preview slots dès que leur <c>peerId</c> devient connu.
	/// </summary>
	public static event Action? IdentityMapChanged;

	public static bool TryGetPeerId(string userId, out int peerId)
		=> _peerByUserId.TryGetValue(userId, out peerId);

	public static bool TryGetUserId(int peerId, out string userId)
		=> _userIdByPeer.TryGetValue(peerId, out userId!);

	/// <summary>Snapshot pour back-fill (ex.&#160;: serveur envoie les entrées existantes au nouveau client).</summary>
	public static IEnumerable<(int peerId, string userId)> Snapshot()
	{
		foreach (var kv in _userIdByPeer) yield return (kv.Key, kv.Value);
	}

	public static void Set(int peerId, string userId)
	{
		if (string.IsNullOrEmpty(userId)) return;
		// Si peerId était déjà mappé à un autre user (cas très rare&#160;: même peerId
		// recyclé après reconnexion), purger l'ancienne entrée pour éviter un
		// reverse-lookup périmé.
		if (_userIdByPeer.TryGetValue(peerId, out var prev) && prev != userId)
			_peerByUserId.Remove(prev);
		_userIdByPeer[peerId] = userId;
		_peerByUserId[userId] = peerId;
		IdentityMapChanged?.Invoke();
	}

	public static void Remove(int peerId)
	{
		if (!_userIdByPeer.TryGetValue(peerId, out var u)) return;
		_userIdByPeer.Remove(peerId);
		_peerByUserId.Remove(u);
		IdentityMapChanged?.Invoke();
	}

	public static void Clear()
	{
		if (_userIdByPeer.Count == 0) return;
		_peerByUserId.Clear();
		_userIdByPeer.Clear();
		IdentityMapChanged?.Invoke();
	}
}
