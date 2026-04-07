using System;

namespace Core.Network;

/// <summary>
/// Rôle réseau d'un pair dans la session.
/// </summary>
public enum NetworkRole
{
    /// <summary>Aucun rôle assigné — transport non démarré.</summary>
    None,
    /// <summary>Ce pair est le serveur dédié ou l'hôte de la session.</summary>
    Server,
    /// <summary>Ce pair est un client connecté à un serveur.</summary>
    Client
}

/// <summary>
/// Contrat de transport réseau. Abstrait la couche de communication
/// pour permettre de swapper l'implémentation (ENet, Steam, etc.)
/// sans toucher au reste du code réseau.
/// </summary>
public interface INetworkProvider
{
    /// <summary>Rôle actuel de ce pair dans la session.</summary>
    NetworkRole Role    { get; }

    /// <summary>Identifiant unique de ce pair attribué par le transport.</summary>
    int LocalPeerId     { get; }

    /// <summary><c>true</c> si le transport est actif et connecté.</summary>
    bool IsRunning      { get; }

    /// <summary>
    /// Démarre un serveur sur le port et le nombre de pairs maximum donnés.
    /// </summary>
    /// <param name="port">Le port UDP sur lequel écouter.</param>
    /// <param name="maxPeers">Le nombre maximum de clients simultanés.</param>
    void StartServer(int port, int maxPeers);

    /// <summary>
    /// Connecte ce pair à un serveur distant.
    /// </summary>
    /// <param name="address">L'adresse IP ou le nom de domaine du serveur.</param>
    /// <param name="port">Le port UDP du serveur.</param>
    void ConnectToServer(string address, int port);

    /// <summary>Ferme la connexion active et libère le transport.</summary>
    void Disconnect();

    /// <summary>
    /// Envoie un paquet à un pair spécifique en mode fiable (TCP-like).
    /// </summary>
    /// <param name="toPeerId">L'identifiant du pair destinataire.</param>
    /// <param name="data">Les octets à envoyer.</param>
    void SendReliable(int toPeerId, byte[] data);

    /// <summary>
    /// Envoie un paquet à un pair spécifique en mode non-fiable ordonné (UDP).
    /// </summary>
    /// <param name="toPeerId">L'identifiant du pair destinataire.</param>
    /// <param name="data">Les octets à envoyer.</param>
    void SendUnreliable(int toPeerId, byte[] data);

    /// <summary>
    /// Diffuse un paquet à tous les pairs connectés en mode non-fiable ordonné.
    /// </summary>
    /// <param name="data">Les octets à diffuser.</param>
    /// <param name="excludePeerId">Identifiant d'un pair à exclure de la diffusion. <c>-1</c> pour inclure tout le monde.</param>
    void BroadcastUnreliable(byte[] data, int excludePeerId = -1);

    /// <summary>
    /// Diffuse un paquet à tous les pairs connectés en mode fiable.
    /// </summary>
    /// <param name="data">Les octets à diffuser.</param>
    /// <param name="excludePeerId">Identifiant d'un pair à exclure de la diffusion. <c>-1</c> pour inclure tout le monde.</param>
    void BroadcastReliable(byte[] data, int excludePeerId = -1);

    /// <summary>Déclenché lorsqu'un pair se connecte. Paramètre : l'identifiant du pair.</summary>
    event Action<int>         PeerConnected;

    /// <summary>Déclenché lorsqu'un pair se déconnecte. Paramètre : l'identifiant du pair.</summary>
    event Action<int>         PeerDisconnected;

    /// <summary>Déclenché à la réception d'un paquet brut. Paramètres : (identifiant de l'émetteur, octets reçus).</summary>
    event Action<int, byte[]> PacketReceived;

    /// <summary>Déclenché une fois que le serveur est démarré et prêt à accepter des connexions.</summary>
    event Action              ServerStarted;

    /// <summary>Déclenché si la connexion ou le démarrage du serveur échoue. Paramètre : message d'erreur.</summary>
    event Action<string>      ConnectionFailed;
}
