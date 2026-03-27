using Godot;
using Godot.Collections;
using Core.Network;

/// <summary>
/// Gère l'instanciation, le despawn et le respawn des personnages dans le monde.
/// Possède les spawn points et les scènes à instancier.
/// <para>
/// Architecture prévue : le serveur appellera <see cref="SpawnPlayer"/> via un paquet
/// <c>SpawnReq</c> reçu par le <c>NetworkManager</c>. Pour l'instant, c'est le
/// <c>World</c> qui appelle directement les méthodes.
/// </para>
/// </summary>
public partial class PlayerSpawner : Node3D
{
	[ExportGroup("Scènes")]
	[Export] private PackedScene _playerScene;
	[Export] private PackedScene _remoteCharacterScene;

	[ExportGroup("Spawn Points")]
	/// <summary>
	/// Points de spawn disponibles dans la map. Le spawner en choisit un
	/// automatiquement via <see cref="GetNextSpawnPoint"/>.
	/// Si vide, les personnages spawneront à l'origine (0,0,0).
	/// </summary>
	[Export] private Array<Marker3D> _spawnPoints = new();

	private int _nextSpawnIndex = 0;

	/// <summary>
	/// Dictionnaire des personnages actifs, indexés par peer ID.
	/// Partagé avec le <c>World</c> pour les mises à jour d'état réseau.
	/// </summary>
	public Dictionary<int, Character> Characters { get; } = new();

	/// <summary>
	/// Spawne un personnage pour le peer donné.
	/// Si <paramref name="isLocal"/> est <c>true</c>, instancie un <c>Player</c> jouable,
	/// sinon un <c>RemoteCharacter</c> piloté par le réseau.
	/// </summary>
	/// <param name="peerId">L'identifiant ENet du pair à spawner.</param>
	/// <param name="isLocal"><c>true</c> pour le joueur local, <c>false</c> pour un pair distant.</param>
	/// <param name="position">
	/// Position de spawn en world space. Si <c>null</c>, utilise le prochain
	/// spawn point disponible via <see cref="GetNextSpawnPoint"/>.
	/// </param>
	/// <returns>Le personnage instancié, ou <c>null</c> si le peerId est déjà spawné.</returns>
	public Character? SpawnPlayer(int peerId, bool isLocal, Vector3? position = null)
	{
		if (Characters.ContainsKey(peerId))
		{
			GD.PrintErr($"[PlayerSpawner] PeerId {peerId} est déjà spawné.");
			return null;
		}

		Character character;

		if (isLocal)
		{
			var player = _playerScene.Instantiate<Player>();
			player.PeerId = peerId;
			character = player;
		}
		else
		{
			var remote = _remoteCharacterScene.Instantiate<RemoteCharacter>();
			remote.PeerId = peerId;
			character = remote;
		}

		Vector3 spawnPos = position ?? GetNextSpawnPoint();
		character.Position = spawnPos;

		AddChild(character);
		Characters[peerId] = character;

		GD.Print($"[PlayerSpawner] Spawné peerId={peerId} isLocal={isLocal} à {spawnPos}");
		return character;
	}

	/// <summary>
	/// Retire le personnage associé au peer donné de la scène et du registre.
	/// </summary>
	/// <param name="peerId">L'identifiant ENet du pair à despawner.</param>
	public void DespawnPlayer(int peerId)
	{
		if (!Characters.TryGetValue(peerId, out var character))
		{
			GD.PrintErr($"[PlayerSpawner] Tentative de despawn d'un peerId inconnu : {peerId}");
			return;
		}

		character.QueueFree();
		Characters.Remove(peerId);
		GD.Print($"[PlayerSpawner] Despawné peerId={peerId}");
	}

	/// <summary>
	/// Despawne puis respawne le personnage à un nouveau spawn point.
	/// Utile pour les morts, les téléportations forcées par le serveur, etc.
	/// </summary>
	/// <param name="peerId">L'identifiant ENet du pair à respawner.</param>
	/// <param name="isLocal"><c>true</c> si c'est le joueur local.</param>
	public void RespawnPlayer(int peerId, bool isLocal)
	{
		DespawnPlayer(peerId);
		SpawnPlayer(peerId, isLocal);
	}

	/// <summary>
	/// Retourne la position du prochain spawn point disponible en round-robin.
	/// Retourne <c>Vector3.Zero</c> si aucun spawn point n'est configuré.
	/// </summary>
	public Vector3 GetNextSpawnPoint()
	{
		if (_spawnPoints.Count == 0) return Vector3.Zero;
		var point = _spawnPoints[_nextSpawnIndex];
		_nextSpawnIndex = (_nextSpawnIndex + 1) % _spawnPoints.Count;
		return point.GlobalPosition;
	}
}
