/// +=============================================================+
/// |    _____ _          __  __              _        _          |
/// |   |_   _| |_  ___  |  \/  |___ _  _ _ _| |_ __ _(_)_ _      |
/// |     | | | ' \/ -_) | |\/| / _ | || | ' |  _/ _` | | ' \     |
/// |     |_| |_||_\___| |_|  |_\___/\_,_|_||_\__\__,_|_|_||_|    |
/// |                                                             |
/// |  ---------------------------------------------------------  |
/// |  Fichier:         WinningController.cs                      |
/// |  Auteur:           Jean-Marc Bouchard                       |
/// |  Fonction: Contrôleur de la Machine d'états de l'état       |
/// |  Winning.                                                   |
/// |  ---------------------------------------------------------  |
/// +==============================================================+
using Godot;
using System.Collections.Generic;
using Core.Auth;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
namespace Core.World;

/// <summary>
/// Phase «&#160;Winning&#160;» de la FSM principale. Possède l'instance de
/// <c>winning.tscn</c> pendant qu'elle est active et orchestre une séquence de
/// trois slides côté client. Côté serveur dédié, ne fait pas tourner d'UI mais
/// reste en vie pour&#160;: tallier les votes reçus via <see cref="ServerReceiveVote"/>,
/// rebroadcaster l'état via <see cref="ClientReceiveTally"/> et finaliser
/// le choix de map quand l'hôte confirme (<see cref="ServerConfirmVote"/>).
/// </summary>
public sealed partial class WinningController : Node3D, IPhase
{
	/// <summary>Scène <c>winning.tscn</c> à instancier à l'entrée de la phase.</summary>
    [Export] private PackedScene _winningSceneAsset;

	/// <summary>Durée d'affichage de la slide 1 (gagnant principal).</summary>
	[Export] private float _celebrationDuration = 10f;

	/// <summary>
	/// Délai après l'apparition de la slide 2 avant que le bouton «&#160;Continue&#160;»
    /// soit révélé (secondes).
    /// </summary>
    [Export] private float _slide2ButtonDelay = 4f;

    /// <summary>
	/// Délai max après l'apparition du bouton «&#160;Continue&#160;» avant le passage
	/// forcé vers la slide de vote (secondes).
	/// </summary>
	[Export] private float _slide2ForceAdvance = 15f;

	/// <summary>Délai max du vote (slide 3) avant auto-avance vers <c>NewGame</c>.</summary>
	[Export] private float _voteTimeout = 30f;

	public enum State { Init, Failure, Slide1, Slide2WaitButton, Slide2ButtonShown, Slide3, NewGame }

	private StateMachine<State> _fsm;
	private WinningScene _winningInstance;
	private bool _continuePressed;
	private bool _voteConfirmed;
	private bool _done;

	// État de vote côté serveur (et miroir client après chaque broadcast).
	// Clé&#160;: peerId. Valeur&#160;: identifiant de map sélectionné.
	private readonly Dictionary<int, string> _votes = new();
	private string _majorityMapId = "";

	// Temps cumulé côté serveur dédié (pas de FSM UI&#160;: on attend simplement
	// la fin de la fenêtre de vote, ou un ServerConfirmVote).
	private float _serverElapsed;

	public bool IsDone => _done;

	public void Enter()
	{
		_done = false;
		_continuePressed = false;
		_voteConfirmed = false;
		_votes.Clear();
		_majorityMapId = "";
		_serverElapsed = 0f;

		// Serveur dédié&#160;: pas d'UI mais on reste en vie pour traiter les
        // RPCs de vote et broadcaster les tallies pendant la fenêtre Winning.
        if (IsNetworkedServer())
        {
            Multiplayer.PeerDisconnected += OnPeerDisconnectedServer;
            return;
        }

        if (_winningSceneAsset is null)
        {
			GD.PrintErr("[WinningController] _winningSceneAsset non assigné dans l'inspecteur.");
            _fsm = new StateMachine<State>(State.Failure, OnSubEnter, OnSubExit);
            OnSubEnter(State.Failure);
            return;
        }

        _winningInstance = _winningSceneAsset.Instantiate<WinningScene>();
        _winningInstance.MapVoteCast += OnMapVoteCast;
        _winningInstance.ContinuePressed += OnContinuePressed;
        _winningInstance.VoteConfirmed += OnVoteConfirmed;
        _winningInstance.QuitPressed += OnQuitPressed;
        _winningInstance.LaunchNewLobby += OnLaunchNewLobby;
        AddChild(_winningInstance);

        // Pousse le libellé du gagnant principal à la slide 1 et remplit les
        // panneaux de sous-gagnants de la slide 2 à partir des données stockées
        // dans LobbyState par GameController.
        ApplyWinnerLabel();
        ApplySubWinners();

        _fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);

        // Init -> Slide1 dès que l'instance est dans l'arbre.
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => _winningInstance is not null && _winningInstance.IsInsideTree()),
            State.Slide1
        );

        // Slide1 -> Slide2WaitButton après le délai d'affichage du gagnant.
        _fsm.When(State.Slide1,
            new TimeElapsedCondition<State>(_celebrationDuration),
            State.Slide2WaitButton
        );

        // Slide2WaitButton -> Slide2ButtonShown après _slide2ButtonDelay.
        _fsm.When(State.Slide2WaitButton,
            new TimeElapsedCondition<State>(_slide2ButtonDelay),
            State.Slide2ButtonShown
        );

        // Slide2ButtonShown -> Slide3 quand l'utilisateur appuie sur Continue
        // ou après le délai d'auto-avance.
        _fsm.When(State.Slide2ButtonShown,
            new PredicateCondition<State>(() => _continuePressed),
            State.Slide3
        );
        _fsm.When(State.Slide2ButtonShown,
            new TimeElapsedCondition<State>(_slide2ForceAdvance),
            State.Slide3
        );

        // Slide3 -> NewGame sur confirmation explicite ou expiration du timeout.
        _fsm.When(State.Slide3,
            new PredicateCondition<State>(() => _voteConfirmed),
            State.NewGame
        );
        _fsm.When(State.Slide3,
            new TimeElapsedCondition<State>(_voteTimeout),
            State.NewGame
        );

        OnSubEnter(State.Init);
    }

    public void Tick(float InDelta)
    {
        if (_fsm is not null)
        {
            _fsm.Tick(InDelta);
            return;
        }
        // Serveur dédié&#160;: aucun FSM UI. Attendre l'enveloppe totale puis
        // finaliser, sauf si un ServerConfirmVote l'a déjà fait avant.
        _serverElapsed += InDelta;
        float totalWindow = _celebrationDuration + _slide2ButtonDelay + _slide2ForceAdvance + _voteTimeout;
        if (_serverElapsed >= totalWindow)
        {
            ServerFinalizeIfNeeded();
            _done = true;
        }
    }

    public void Exit()
    {
        Multiplayer.PeerDisconnected -= OnPeerDisconnectedServer;

        if (_winningInstance is not null)
        {
            _winningInstance.MapVoteCast -= OnMapVoteCast;
            _winningInstance.ContinuePressed -= OnContinuePressed;
            _winningInstance.VoteConfirmed -= OnVoteConfirmed;
            _winningInstance.QuitPressed -= OnQuitPressed;
            _winningInstance.LaunchNewLobby -= OnLaunchNewLobby;
            _winningInstance.QueueFree();
            _winningInstance = null;
        }
        _votes.Clear();
		_majorityMapId = "";
        _fsm = null;
    }

    // ── Signaux UI -> contrôleur ─────────────────────────────────────────────

    private void OnMapVoteCast(string InMapId)
    {
        if (IsNetworkedClient())
        {
            RpcId(1, MethodName.ServerReceiveVote, InMapId);
            return;
        }
        // Offline : applique localement comme si on était le serveur.
        ApplyVoteLocal(Multiplayer.GetUniqueId(), InMapId);
    }

    private void OnContinuePressed() => _continuePressed = true;

    private void OnVoteConfirmed(string InMapId)
    {
        //  seul l'hôte peut confirmer. Le bouton est aussi désactivé
        // côté UI, ce check est une ceinture de sécurité.
        if (!LobbyState.IsHost) return;

        if (IsNetworkedClient())
        {
            RpcId(1, MethodName.ServerConfirmVote);
            return;
        }
        // Offline : pas de réseau, finalise directement sur la map majoritaire
        // (ou la sélection locale en repli).
        string winner = !string.IsNullOrEmpty(_majorityMapId) ? _majorityMapId : InMapId;
        FinalizeVoteLocal(winner);
    }

    private void OnLaunchNewLobby() { /* legacy, conservé pour rétro-compat */ }

    /// <summary>
    /// Bouton QUIT de la slide 3&#160;: l'utilisateur quitte volontairement le
    /// post-game. On coupe la session réseau (si active) et on rebascule la
    /// scène vers le main menu plutôt que de laisser la FSM principale
	/// reboucler vers Lobby. <see cref="_done"/> est levé pour que la phase
    /// se termine proprement côté MainController au cas où le scene change
    /// ne se produirait pas immédiatement.
    /// </summary>
    private void OnQuitPressed()
    {
        NetworkManager.Instance?.Disconnect();
        LobbyState.Clear();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _done = true;
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
    }

    // ── Logique de vote (serveur ou offline) ─────────────────────────────────

    /// <summary>
	/// Applique un vote dans <see cref="_votes"/>, recalcule la majorité et
    /// pousse la mise à jour: broadcast aux peers en mode serveur, mise à
    /// jour locale de l'UI en offline. Idempotent par peer: re-voter écrase
    /// le choix précédent.
    /// </summary>
    private void ApplyVoteLocal(int InPeerId, string InMapId)
    {
        _votes[InPeerId] = InMapId;
        RecomputeMajority();
        // En mode connecté serveur, on propage à tous les peers. En offline
        // (ou côté client&#160;: ne devrait pas arriver), on met simplement à
        // jour l'UI locale.
        if (IsNetworkedServer())
            BroadcastTally();
        else
            ApplyTallyToUi();
    }

    private static bool IsNetworked()
        => NetworkManager.Instance is not null && (NetworkManager.Instance.IsServer || NetworkManager.Instance.IsClient);

    private static bool IsNetworkedServer()
        => NetworkManager.Instance is not null && NetworkManager.Instance.IsServer;

    private static bool IsNetworkedClient()
        => NetworkManager.Instance is not null && NetworkManager.Instance.IsClient;

    private void FinalizeVoteLocal(string InMapId)
    {
        if (!string.IsNullOrEmpty(InMapId)) LobbyState.SetSelectedMap(InMapId);
        _voteConfirmed = true;
    }

    /// <summary>
	/// Met à jour <see cref="_majorityMapId"/>&#160;: vide tant qu'aucune map n'a
    /// strictement plus de la moitié des voix exprimées. Le total utilisé est
    /// le nombre de peers connectés (ou 1 en offline).
    /// </summary>
    private void RecomputeMajority()
    {
		_majorityMapId = "";
        int totalVoters = Mathf.Max(1, GetTotalVoters());
        int threshold = (totalVoters / 2) + 1;

        var counts = ComputeCounts();
        foreach (var (mapId, count) in counts)
        {
            if (count >= threshold)
            {
                _majorityMapId = mapId;
                break;
            }
        }
    }

    private Dictionary<string, int> ComputeCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var map in MapRegistry.All) counts[map.Id] = 0;
        foreach (var (_, mapId) in _votes)
        {
            if (counts.ContainsKey(mapId)) counts[mapId] += 1;
            else counts[mapId] = 1;
        }
        return counts;
    }

    private int GetTotalVoters()
    {
        // Offline&#160;: seul le joueur local vote. En mode connecté, on s'aligne
        // sur le nombre de joueurs présents dans la salle (source d'autorité du
        // lobby) plutôt que sur la liste de peers ENet du moment, qui peut
        // fluctuer en cas de reconnexion.
        if (!IsNetworked()) return 1;
        var snapshot = LobbyState.Current;
        if (snapshot is not null && snapshot.Players.Count > 0) return snapshot.Players.Count;
        // Repli&#160;: peers visibles + self pour un client, peers seuls pour le serveur.
        if (IsNetworkedServer()) return Multiplayer.GetPeers().Length;
        return Multiplayer.GetPeers().Length + 1;
    }

    private void ApplyTallyToUi()
    {
        var counts = ComputeCounts();
        var dict = new Godot.Collections.Dictionary<string, int>();
        foreach (var (k, v) in counts) dict[k] = v;
        _winningInstance?.ApplyVoteTally(dict, _majorityMapId);
    }

    private void BroadcastTally()
    {
        // Sérialise _votes en deux tableaux parallèles pour le RPC.
        int n = _votes.Count;
        var peerIds = new int[n];
        var mapIds = new string[n];
        int i = 0;
        foreach (var (peer, map) in _votes)
        {
            peerIds[i] = peer;
            mapIds[i] = map;
            i++;
        }
        Rpc(MethodName.ClientReceiveTally, peerIds, mapIds, _majorityMapId);
    }

    private void ServerFinalizeIfNeeded()
    {
        if (string.IsNullOrEmpty(_majorityMapId)) return;
        Rpc(MethodName.ClientFinalizeVote, _majorityMapId);
        if (!string.IsNullOrEmpty(_majorityMapId)) LobbyState.SetSelectedMap(_majorityMapId);
    }

    private void OnPeerDisconnectedServer(long InPeerId)
    {
        if (_votes.Remove((int)InPeerId))
        {
            RecomputeMajority();
            BroadcastTally();
        }
    }

    // ── RPCs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reçu par le serveur quand un client clique sur une map. Met à jour le
    /// vote du peer et rebroadcaste le tally à tous.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerReceiveVote(string InMapId)
    {
        if (!Multiplayer.IsServer()) return;
        int peerId = Multiplayer.GetRemoteSenderId();
        ApplyVoteLocal(peerId, InMapId);
    }

    /// <summary>
    /// Reçu par les clients (et appelé localement sur le serveur via
	/// <c>CallLocal=true</c>)&#160;: reconstruit <see cref="_votes"/> et pousse
    /// les counts à l'UI.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReceiveTally(int[] InPeerIds, string[] InMapIds, string InMajorityMapId)
    {
        _votes.Clear();
        int n = Mathf.Min(InPeerIds?.Length ?? 0, InMapIds?.Length ?? 0);
        for (int i = 0; i < n; i++) _votes[InPeerIds[i]] = InMapIds[i];
		_majorityMapId = InMajorityMapId ?? "";
        ApplyTallyToUi();
    }

    /// <summary>
    /// Reçu par le serveur quand l'hôte clique sur CONFIRM&#160;: verrouille la
	/// map majoritaire courante et broadcast <see cref="ClientFinalizeVote"/>.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerConfirmVote()
    {
        if (!Multiplayer.IsServer()) return;
        if (string.IsNullOrEmpty(_majorityMapId)) return;
        Rpc(MethodName.ClientFinalizeVote, _majorityMapId);
        LobbyState.SetSelectedMap(_majorityMapId);
        _done = true; // serveur dédié finit ici aussi
    }

    /// <summary>
    /// Reçu par les clients (et serveur via CallLocal)&#160;: applique la map
    /// retenue et fait sortir la phase vers <c>NewGame</c>.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientFinalizeVote(string InMapId)
    {
        FinalizeVoteLocal(InMapId);
    }

    // ── FSM UI (clients uniquement) ──────────────────────────────────────────

    private void OnSubEnter(State InState)
    {
        switch (InState)
        {
            case State.Slide1:
                _winningInstance?.TweenToSlide1();
                break;
            case State.Slide2WaitButton:
                _winningInstance?.TweenToSlide2();
                break;
            case State.Slide2ButtonShown:
                _winningInstance?.ShowSlide2ContinueButton();
                break;
            case State.Slide3:
                _winningInstance?.TweenToSlide3();
                // En offline, le joueur unique est de facto l'hôte du vote.
                // En réseau, on se fie au flag LobbyState renseigné à la
                // création de la salle.
                _winningInstance?.SetIsHost(LobbyState.IsHost || !IsNetworked());
                ApplyTallyToUi();
                break;
            case State.NewGame:
            case State.Failure:
                // Filet de sécurité côté UI&#160;: si rien n'a été confirmé via le
                // flux serveur, applique la majorité courante (ou la sélection
                // locale en repli) pour ne pas laisser le prochain Lobby sans
                // map. Le serveur réseau passe par ServerFinalizeIfNeeded.
                {
                    string fallback = !string.IsNullOrEmpty(_majorityMapId)
                        ? _majorityMapId
						: (_winningInstance?.SelectedMapId ?? "");
                    if (!string.IsNullOrEmpty(fallback)) LobbyState.SetSelectedMap(fallback);
                }
                _done = true;
                break;
        }
    }

    private void OnSubExit(State _) { }

    public override void _Ready() { }

    /// <summary>
	/// Lit les données de gagnant persistées par <see cref="GameController"/> dans
	/// <see cref="LobbyState"/> et les pousse au titre de la slide 1 via
	/// <see cref="WinningScene"/>. Sans effet si aucun gagnant n'est enregistré (ex.&#160;:
    /// première partie, partie avortée).
    /// </summary>
    private void ApplyWinnerLabel()
    {
        if (_winningInstance is null) return;
        int peerId = LobbyState.LastWinnerPeerId;
        if (peerId == 0) return;
        string username = ResolveUsername(peerId);
        string label = string.IsNullOrEmpty(LobbyState.LastWinnerConditionLabel)
			? $"{username} Won"
			: $"{username} — {LobbyState.LastWinnerConditionLabel}";
		// Le _Ready des slides a tourné synchroniquement à l'AddChild du Winning
		// instance, donc SetWinnerLabel peut être appelé directement.
		var slide1 = _winningInstance.GetNodeOrNull<WinningSlide1>("Winning_UI/SCR_01_WHOWINS");
        slide1?.SetWinnerLabel(label);
    }

    /// <summary>
	/// Pousse les sous-gagnants persistés par <see cref="GameController"/> à la
    /// slide 2 sous forme d'entrées (titre, username, détail). Les panneaux sans
    /// sous-gagnant correspondant seront masqués par
	/// <see cref="WinningSlide2.Populate"/>.
    /// </summary>
    private void ApplySubWinners()
    {
        if (_winningInstance is null) return;
		var slide2 = _winningInstance.GetNodeOrNull<WinningSlide2>("Winning_UI/SCR_02_SUBWINNERS");
        if (slide2 is null) return;
        var subs = LobbyState.LastSubWinners;
        var entries = new List<WinningSlide2.SubEntry>(subs?.Count ?? 0);
        if (subs is not null)
        {
            for (int i = 0; i < subs.Count; i++)
            {
                var sw = subs[i];
                entries.Add(new WinningSlide2.SubEntry
                {
					Title = sw.Label ?? "",
                    Username = ResolveUsername(sw.PeerId),
					Detail = "",
                });
            }
        }
        slide2.Populate(entries);
    }

    /// <summary>
	/// Résout un nom d'usager affichable pour <paramref name="InPeerId"/>. Pour
	/// le joueur local (peer == <see cref="NetworkManager.LocalPeerId"/> ou peer
    /// == 1 en offline) on utilise le username Firebase courant. Sans mapping
    /// peer→userId broadcasté pour les autres joueurs, on retombe sur
    /// «&#160;Player N&#160;» jusqu'à ce que ce mapping soit ajouté au protocole.
    /// </summary>
    private static string ResolveUsername(int InPeerId)
    {
        int localPeer = NetworkManager.Instance?.LocalPeerId ?? 1;
        bool isLocal = InPeerId == localPeer
            || (InPeerId == 1 && (NetworkManager.Instance is null || !NetworkManager.Instance.IsRunning));
        if (isLocal)
        {
            var u = AuthServiceProvider.Instance.CurrentUser?.Username;
            if (!string.IsNullOrEmpty(u)) return u;
        }
		return $"Player {InPeerId}";
    }
}
