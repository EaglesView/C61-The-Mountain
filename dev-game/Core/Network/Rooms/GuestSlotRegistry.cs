using System.Collections.Generic;

namespace Core.Network.Rooms;

/// <summary>
/// Registre serveur des slots invités partagés (<c>invite1..invite8</c> dans
/// <c>Login.cs</c>). Firebase Auth accepte les mêmes credentials depuis
/// plusieurs clients&#160;: c'est ce registre qui garantit qu'un slot n'est
/// utilisé que par un peer à la fois.
///
/// Un slot est indisponible si&#160;:
///   1. Il est revendiqué par un peer encore connecté (Claim, vidé par
///      <see cref="ReleasePeer"/> sur PeerDisconnected), OU
///   2. Il a été distribué par <see cref="TryReserveFreeSlot"/> il y a moins
///      de <see cref="ReservationTtlMs"/> ms (TTL qui couvre la fenêtre entre
///      l'allocation et le sign-in Firebase).
///
/// Stockage purement en mémoire&#160;: serveur unique, registre éphémère
/// reconstruit à chaque redémarrage.
/// </summary>
public static class GuestSlotRegistry
{
	public const int SlotCount = 8;
	private const long ReservationTtlMs = 10_000;

	private static readonly Dictionary<int, long> _reservationExpiryMs = new();
	private static readonly Dictionary<int, int> _peerBySlot = new();
	private static readonly Dictionary<int, int> _slotByPeer = new();

	/// <summary>
	/// Trouve le premier slot 1..<see cref="SlotCount"/> qui n'est ni
	/// revendiqué par un peer ni réservé récemment, puis pose une réservation
	/// avec TTL <see cref="ReservationTtlMs"/>. Retourne -1 si tout est pris.
	/// </summary>
	public static int TryReserveFreeSlot()
	{
		long now = NowMs();
		for (int i = 1; i <= SlotCount; i++)
		{
			if (_peerBySlot.ContainsKey(i)) continue;
			if (_reservationExpiryMs.TryGetValue(i, out var exp) && exp > now) continue;
			_reservationExpiryMs[i] = now + ReservationTtlMs;
			return i;
		}
		return -1;
	}

	/// <summary>Vrai si le slot est revendiqué par un peer (ne lit pas les réservations TTL).</summary>
	public static bool TryGetPeerForSlot(int slot, out int peerId)
		=> _peerBySlot.TryGetValue(slot, out peerId);

	/// <summary>
	/// Marque le slot comme revendiqué par <paramref name="peerId"/>. Appelé
	/// par <c>ServerReceiveIdentity</c> quand un peer invité finalise sa
	/// connexion. Purge l'éventuel slot précédent du même peer (cas&#160;:
	/// retry après échec) et la réservation TTL associée au slot.
	/// </summary>
	public static void Claim(int slot, int peerId)
	{
		if (slot < 1 || slot > SlotCount) return;
		if (_slotByPeer.TryGetValue(peerId, out var oldSlot) && oldSlot != slot)
			_peerBySlot.Remove(oldSlot);
		_peerBySlot[slot] = peerId;
		_slotByPeer[peerId] = slot;
		_reservationExpiryMs.Remove(slot);
	}

	/// <summary>Libère le slot revendiqué par ce peer. À câbler sur PeerDisconnected.</summary>
	public static void ReleasePeer(int peerId)
	{
		if (!_slotByPeer.TryGetValue(peerId, out var slot)) return;
		_slotByPeer.Remove(peerId);
		_peerBySlot.Remove(slot);
	}

	private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
