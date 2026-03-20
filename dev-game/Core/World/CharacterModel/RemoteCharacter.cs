using Godot;
using Core.Network;
using static Utils.CharacterUtils;

/// <summary>
/// Network-driven peer character. Interpolates between ring-buffered snapshots
/// received from the server; never runs MoveAndSlide.
/// PhysicsSkelton continues as a child node, springing toward the interpolated pose.
/// </summary>
public partial class RemoteCharacter : Character
{
    private const int   BufferSize   = 4;
    private const float RenderDelay  = 0.1f; // 100ms behind to absorb jitter

    private readonly PlayerNetState[] _snapshots  = new PlayerNetState[BufferSize];
    private readonly ulong[]          _timestamps = new ulong[BufferSize]; // msec
    private int  _head  = 0;
    private int  _count = 0;

    private MovementState _lastAnimatedState = MovementState.Idle;

    /// <summary>Called by World.OnStateReceived.</summary>
    public void PushSnapshot(PlayerNetState state, ulong timestampMsec)
    {
        _snapshots[_head]  = state;
        _timestamps[_head] = timestampMsec;
        _head = (_head + 1) % BufferSize;
        if (_count < BufferSize) _count++;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Never call base._PhysicsProcess — MoveAndSlide must not run here.
        if (_count < 2) return;

        ulong nowMsec    = Time.GetTicksMsec();
        ulong renderTime = nowMsec - (ulong)(RenderDelay * 1000f);

        int oldest = (_head - _count + BufferSize) % BufferSize;
        int aIdx = -1, bIdx = -1;

        for (int i = 0; i < _count - 1; i++)
        {
            int ia = (oldest + i) % BufferSize;
            int ib = (oldest + i + 1) % BufferSize;
            if (_timestamps[ia] <= renderTime && _timestamps[ib] >= renderTime)
            {
                aIdx = ia;
                bIdx = ib;
                break;
            }
        }

        if (aIdx < 0)
        {
            // No straddling pair: snap to the most recent snapshot
            int latest = (_head - 1 + BufferSize) % BufferSize;
            ApplyNetworkState(_snapshots[latest]);
            return;
        }

        ulong tA = _timestamps[aIdx];
        ulong tB = _timestamps[bIdx];
        float t  = (tB == tA) ? 0f : (float)(renderTime - tA) / (float)(tB - tA);
        t = Mathf.Clamp(t, 0f, 1f);

        PlayerNetState a = _snapshots[aIdx];
        PlayerNetState b = _snapshots[bIdx];

        // Continuous interpolation
        GlobalPosition             = a.Position.Lerp(b.Position, t);
        velocity                   = a.Velocity.Lerp(b.Velocity, t);
        Rotation                   = new Vector3(Rotation.X, Mathf.LerpAngle(a.BodyYaw, b.BodyYaw, t), Rotation.Z);
        RotateHead(Mathf.LerpAngle(a.HeadPitch, b.HeadPitch, t));
        PhysicsSkelton.ArmPointDir = a.ArmPointDir.Lerp(b.ArmPointDir, t);

        // Discrete values snap at the midpoint
        PlayerNetState snap    = t >= 0.5f ? b : a;
        PhysicsSkelton.Aiming  = snap.Aiming;
        PhysicsSkelton.ArmsUp  = snap.ArmsUp;
        currentMovementState   = snap.MoveState;
        currentEmoteState      = snap.EmoteState;
    }

    public override void _Process(double delta)
    {
        // Drive animation from network movement state; skip base._Process (input-driven).
        if (_lastAnimatedState == currentMovementState) return;
        _lastAnimatedState = currentMovementState;
        PlayAnimationFromMovement(currentMovementState, AnimPlayer);
    }
}
