using Godot;
using System;
using Core.Network;
using Core.Network.Rooms;
using Core.Shared.StateMachine;
namespace Core.World;

/// <summary>
/// Phase «&#160;Game&#160;» de la FSM principale. Possède le <c>MapContainer</c>
/// (plomberie réseau partagée) et y injecte le niveau correspondant à la map
/// choisie par l'hôte dans le lobby. Le root du niveau implémente
/// <see cref="IPhase"/> + <see cref="IGameMode"/> et fournit le mode de jeu —
/// GameController consomme uniquement ce contrat.
/// </summary>
public sealed partial class GameController : Node3D, IPhase
{
    /// <summary>Scène <c>map_container.tscn</c> instanciée à chaque entrée de la phase.</summary>
    [Export] private PackedScene _mapContainerSceneAsset;

    /// <summary>Scène <c>Player.tscn</c> utilisée par le <c>MultiplayerSpawner</c>.</summary>
    [Export] private PackedScene _playerScene;

    public enum State { Init, Failure, Waiting, Playing, Resolving }
    private StateMachine<State> _fsm = null;
    // Typé en IPhase plutôt qu'IGameMode parce que GameController n'utilise ici
    // que le cycle de vie (Enter/Tick/Exit/IsDone). Les concrets de mode (ex.
    // RotatingBarrelController) implémentent les deux interfaces.
    private IPhase _mode = null;
    private Node _mapContainerInstance = null;
    private MultiplayerSpawner _spawner = null;
    private PlayerSpawner _playerSpawner = null;
    private MultiplayerApi.PeerDisconnectedEventHandler _onPeerDisconnectedHandler = null;
    private bool _done;
    public bool IsDone => _done;

    public override void _Ready()
    {
        // Fallback si les [Export] ne sont pas câblés dans main_controller.tscn —
        // évite d'imposer un drag&drop pour ce premier câblage.
        _mapContainerSceneAsset ??= ResourceLoader.Load<PackedScene>("res://Core/World/Maps/map_container.tscn");
        _playerScene ??= ResourceLoader.Load<PackedScene>("res://Core/World/CharacterModel/Player/Player.tscn");
    }

    public void Enter()
    {
        _done = false;

        // La map (et donc le mode) est choisie par l'hôte dans le lobby et
        // propagée via LobbyState.SelectedMapId.
        var mapDef = MapRegistry.Get(LobbyState.SelectedMapId) ?? MapRegistry.All[0];

        // 1) Instancie le MapContainer (plomberie réseau partagée + slot Map).
        if (_mapContainerSceneAsset is null)
        {
            GD.PrintErr("[GameController] _mapContainerSceneAsset non assigné.");
            _done = true;
            return;
        }
        _mapContainerInstance = _mapContainerSceneAsset.Instantiate();
        AddChild(_mapContainerInstance);

        // 2) Charge le niveau et l'injecte dans le slot Map du container.
        var levelScene = ResourceLoader.Load<PackedScene>(mapDef.ScenePath);
        if (levelScene is null)
        {
            GD.PrintErr($"[GameController] Niveau introuvable&#160;: {mapDef.ScenePath}");
            _done = true;
            return;
        }
        var levelInstance = levelScene.Instantiate();
        var mapSlot = _mapContainerInstance.GetNodeOrNull("Map");
        if (mapSlot is null)
        {
            GD.PrintErr("[GameController] Slot 'Map' introuvable dans map_container.tscn.");
            _done = true;
            return;
        }
        mapSlot.AddChild(levelInstance);

        // 3) Le root du niveau doit implémenter IPhase (et idéalement IGameMode).
        if (levelInstance is not IPhase mode)
        {
            GD.PrintErr($"[GameController] Le root du niveau '{mapDef.ScenePath}' n'implémente pas IPhase.");
            _done = true;
            return;
        }
        _mode = mode;

        // 4) Plomberie réseau — câblée à partir des nodes connus de map_container.
        _spawner = _mapContainerInstance.GetNodeOrNull<MultiplayerSpawner>("GameLogicAssets/MultiplayerSpawner");
        if (_spawner is null)
        {
            GD.PrintErr("[GameController] MultiplayerSpawner introuvable dans map_container.");
            _done = true;
            return;
        }
        _spawner.SpawnFunction = Callable.From<Variant, GodotObject>(SpawnPlayerNode);

        _playerSpawner = levelInstance.GetNodeOrNull<PlayerSpawner>("PlayerSpawner");
        if (_playerSpawner is null)
            GD.PrintErr($"[GameController] PlayerSpawner introuvable dans le niveau '{mapDef.ScenePath}'.");

        var net = NetworkManager.Instance;
        if (net is not null) net.StateReceived += OnStateReceived;

        if (net is not null && net.IsServer)
        {
            GD.Print("[GameController] Dedicated server — waiting for peers.");
            _onPeerDisconnectedHandler = OnPeerDisconnected;
            Multiplayer.PeerDisconnected += _onPeerDisconnectedHandler;
        }
        else if (net is not null && net.IsClient)
        {
            GD.Print("[GameController] Online client — requesting spawn from server.");
            Rpc(MethodName.ClientReady);
        }
        else
        {
            GD.Print("[GameController] Offline — spawning local player directly.");
            SpawnOffline();
        }

        // 5) FSM interne (Init → Waiting → Playing → Resolving).
        _fsm = new StateMachine<State>(State.Init, OnSubEnter, OnSubExit);
        _fsm.When(State.Init,
            new PredicateCondition<State>(() =>/* TODO: Inclure level loaded */ true),
            State.Waiting
        );
        _fsm.When(State.Init,
            new PredicateCondition<State>(() => /* TODO: flag d'erreur de chargement */ false),
            State.Failure
        );
        _fsm.When(State.Waiting,
            new TimeElapsedCondition<State>(10f),
            State.Playing
        );
        _fsm.When(State.Playing,
            new PredicateCondition<State>(() => _mode.IsDone),
            State.Resolving
        );
        OnSubEnter(State.Init);
    }

    public void Tick(float InDelta)
    {
        if (_fsm is null) return;
        if (_fsm.Is(State.Playing)) _mode?.Tick(InDelta);
        _fsm.Tick(InDelta);
    }

    public void Exit()
    {
        var net = NetworkManager.Instance;
        if (net is not null) net.StateReceived -= OnStateReceived;
        if (_onPeerDisconnectedHandler is not null)
        {
            Multiplayer.PeerDisconnected -= _onPeerDisconnectedHandler;
            _onPeerDisconnectedHandler = null;
        }

        if (_fsm is not null && _fsm.Is(State.Playing)) _mode?.Exit();
        if (_mapContainerInstance is not null)
        {
            _mapContainerInstance.QueueFree();
            _mapContainerInstance = null;
        }
        _spawner = null;
        _playerSpawner = null;
        _mode = null;
        _fsm = null;
    }

    // ── Spawn flow (porté de World.cs) ────────────────────────────────────────

    private GodotObject SpawnPlayerNode(Variant data)
    {
        var dict = data.As<Godot.Collections.Dictionary>();
        int peerId = dict["id"].As<int>();
        Vector3 pos = dict["pos"].As<Vector3>();

        var player = _playerScene.Instantiate<Player>();
        player.Name = peerId.ToString();
        player.PeerId = peerId;
        player.SetMultiplayerAuthority(peerId);
        player.SpawnPosition = pos;

        GD.Print($"[GameController] SpawnPlayerNode&#160;: peerId={peerId}, pos={pos}, isLocal={player.IsMultiplayerAuthority()}");
        return player;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReady()
    {
        if (!Multiplayer.IsServer()) return;
        int peerId = Multiplayer.GetRemoteSenderId();
        GD.Print($"[GameController] ClientReady from peer {peerId}");
        ServerSpawnPeer(peerId);
    }

    private void ServerSpawnPeer(int peerId)
    {
        if (_spawner is null || _playerSpawner is null) return;
        Vector3 spawnPos = _playerSpawner.GetNextSpawnPoint();
        var data = new Godot.Collections.Dictionary { ["id"] = peerId, ["pos"] = spawnPos };
        _spawner.Spawn(data);
        GD.Print($"[GameController] Server spawned peer {peerId} at {spawnPos}");
    }

    private void OnPeerDisconnected(long peerId)
    {
        var players = _mapContainerInstance?.GetNodeOrNull("Players");
        var player = players?.GetNodeOrNull(((int)peerId).ToString());
        if (player is not null)
        {
            ((Node)player).QueueFree();
            GD.Print($"[GameController] Server despawned peer {(int)peerId}");
        }
    }

    private void SpawnOffline()
    {
        if (_playerScene is null || _mapContainerInstance is null) return;
        var player = _playerScene.Instantiate<Player>();
        player.Name = "1";
        player.PeerId = 1;
        player.SpawnPosition = _playerSpawner?.GetNextSpawnPoint() ?? Vector3.Zero;
        var players = _mapContainerInstance.GetNodeOrNull("Players");
        if (players is null)
        {
            GD.PrintErr("[GameController] Container 'Players' introuvable dans map_container.");
            return;
        }
        players.AddChild(player);
    }

    private void OnStateReceived(PlayerNetState state)
    {
        var players = _mapContainerInstance?.GetNodeOrNull("Players");
        var player = players?.GetNodeOrNull<Player>(state.PeerId.ToString());
        if (player is null || player.IsMultiplayerAuthority()) return;
        player.PushSnapshot(state, Time.GetTicksMsec());
    }

    // ── FSM interne ───────────────────────────────────────────────────────────

    private void OnSubEnter(State s)
    {
        switch (s)
        {
            case State.Playing: _mode?.Enter(); break;
            case State.Resolving: _done = true; break;
        }
    }
    private void OnSubExit(State _) { }
}
