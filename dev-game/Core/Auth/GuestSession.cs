namespace Core.Auth;

/// <summary>
/// État local de la session invité côté client&#160;: stocke l'index 1..8 du
/// slot attribué par le serveur (cf. <c>GuestSlotRegistry</c>). Renseigné par
/// <c>Login.cs</c> après que l'allocator serveur a retourné un slot libre,
/// puis lu par <c>LobbyController._OnConnected</c> pour le transmettre dans
/// <c>ServerReceiveIdentity</c>. Vaut 0 si l'utilisateur n'est pas connecté en
/// tant qu'invité.
/// </summary>
public static class GuestSession
{
	public static int LocalSlotIndex { get; set; }

	public static void Clear() => LocalSlotIndex = 0;
}
