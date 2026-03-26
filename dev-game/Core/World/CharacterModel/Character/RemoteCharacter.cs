using Godot;
using Core.Network;
using static Utils.CharacterUtils;

/// <summary>
/// Personnage pair piloté par le réseau. Interpole entre les snapshots reçus du serveur
/// via un ring buffer, sans jamais appeler MoveAndSlide directement.
/// Le <c>PhysicsSkelton</c> continue de tourner en tant que noeud enfant et suit
/// la pose interpolée par ressort.
/// </summary>
public partial class RemoteCharacter : Character
{
    private const int BufferSize = 4;
    private const float RenderDelay = 0.1f; // 100ms de retard pour absorber le jitter réseau

    private readonly PlayerNetState[] _snapshots = new PlayerNetState[BufferSize];
    private readonly ulong[] _timestamps = new ulong[BufferSize]; // horodatages en millisecondes
    private int _head = 0;
    private int _count = 0;

    private MovementState _lastAnimatedState = MovementState.Idle;

    /// <summary>
    /// Pousse un nouveau snapshot réseau dans le ring buffer.
    /// Appelé par <c>World.OnStateReceived</c> à chaque paquet reçu du serveur.
    /// </summary>
    /// <param name="state">Le snapshot d'état du joueur distant à enregistrer.</param>
    /// <param name="timestampMsec">L'horodatage de réception en millisecondes (<see cref="Time.GetTicksMsec"/>).</param>
    public void PushSnapshot(PlayerNetState state, ulong timestampMsec)
    {
        _snapshots[_head] = state;
        _timestamps[_head] = timestampMsec;
        _head = (_head + 1) % BufferSize;
        if (_count < BufferSize) _count++;
    }

    ///<summary></summary>
    public override void _PhysicsProcess(double delta)
    {
        // On Appelle pas base._PhysicsProcess
        // Le remote character fait juste update les choses nécéssaire au client
        if (_count < 2) return;

        ulong nowMsec = Time.GetTicksMsec();
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
            // Aucune paire trouvée : on snap directement sur le snapshot le plus récent
            int latest = (_head - 1 + BufferSize) % BufferSize;
            ApplyNetworkState(_snapshots[latest]);
            return;
        }

        ulong tA = _timestamps[aIdx];
        ulong tB = _timestamps[bIdx];
        float t = (tB == tA) ? 0f : (float)(renderTime - tA) / (float)(tB - tA);
        t = Mathf.Clamp(t, 0f, 1f);

        PlayerNetState a = _snapshots[aIdx];
        PlayerNetState b = _snapshots[bIdx];

        // Valeurs continues : interpolées linéairement entre les deux snapshots
        GlobalPosition = a.Position.Lerp(b.Position, t);
        velocity = a.Velocity.Lerp(b.Velocity, t);
        Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(a.BodyYaw, b.BodyYaw, t), Rotation.Z);
        RotateHead(Mathf.LerpAngle(a.HeadPitch, b.HeadPitch, t));
        PhysicsSkelton.ArmPointDir = a.ArmPointDir.Lerp(b.ArmPointDir, t);

        // Valeurs discrètes (enums, booléens) : on bascule au point médian pour éviter les oscillations
        PlayerNetState snap = t >= 0.5f ? b : a;
        PhysicsSkelton.Aiming = snap.Aiming;
        PhysicsSkelton.ArmsUp = snap.ArmsUp;
        currentMovementState = snap.MoveState;
        currentEmoteState = snap.EmoteState;
    }

    public override void _Process(double delta)
    {
        // l'animation est géré par le currentMovementState au serveur.
        if (_lastAnimatedState == currentMovementState) return;
        _lastAnimatedState = currentMovementState;
        PlayAnimationFromMovement(currentMovementState, AnimPlayer);
    }
}
