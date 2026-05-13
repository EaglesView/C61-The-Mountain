using Godot;
using System;
using Core.Shared.StateMachine;
namespace Core.World;

public sealed partial class FallingTilesController : Node3D, IPhase, IGameMode
{
    [Export] public PackedScene CurrentLevel;

    private enum Phase { Idle, Active, Done }
    private StateMachine<Phase>? _fsm;
    private Area3D? _deathZone;
    private int _aliveCount;
    private bool _tilesActivated;

    public string DisplayName => "Falling Tiles";
    public PackedScene Level => CurrentLevel!;
    public bool IsDone => _fsm is not null && _fsm.Is(Phase.Done);

    public override void _Ready()
    {
        _deathZone = GetNodeOrNull<Area3D>("DeathZone");
        if (_deathZone != null)
            _deathZone.BodyEntered += OnDeathZoneBodyEntered;

        if (Multiplayer.GetPeers().Length == 0)
            Enter();
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.GetPeers().Length == 0)
            Tick((float)delta);
    }

    public void Enter()
    {
        _aliveCount = Math.Max(2, Multiplayer.GetPeers().Length + 1);
        _tilesActivated = false;
        _fsm = new StateMachine<Phase>(Phase.Idle);
        _fsm.When(Phase.Idle, new TimeElapsedCondition<Phase>(5f), Phase.Active);
        _fsm.When(Phase.Active, new PredicateCondition<Phase>(() => CheckWinCondition()), Phase.Done);
    }

    public void Tick(float InDelta)
    {
        _fsm?.Tick(InDelta);
        if (!_tilesActivated && _fsm != null && _fsm.Is(Phase.Active))
        {
            _tilesActivated = true;
            GetNodeOrNull<TileGrid>("TileGrid")?.Activate();
        }
    }

    public void Exit()
    {
        if (_deathZone != null)
            _deathZone.BodyEntered -= OnDeathZoneBodyEntered;
        _fsm = null;
    }

    public void LoadLevel() { }
    public void GetRelevantStats() { }

    private void OnDeathZoneBodyEntered(Node3D body)
    {
        if (body is not Character) return;
        _aliveCount = Math.Max(0, _aliveCount - 1);
        body.QueueFree();
    }

    private bool CheckWinCondition() => _aliveCount <= 1;
}