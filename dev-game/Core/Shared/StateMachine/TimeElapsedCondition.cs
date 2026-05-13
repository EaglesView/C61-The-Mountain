using System;

namespace Core.Shared.StateMachine;

/// <summary>
/// Condition qui se déclenche après qu'un délai (en secondes) se soit écoulé dans l'état source.
/// Le compteur est remis à zéro à chaque entrée dans cet état (via <see cref="Reset"/>).
/// </summary>
public sealed class TimeElapsedCondition<TState> : ITransitionCondition<TState>
    where TState : struct, Enum
{
    private float _elapsed;

    /// <summary>Durée totale avant déclenchement (secondes).</summary>
    public float Duration { get; }

    /// <summary>Temps cumulé dans l'état source courant.</summary>
    public float Elapsed => _elapsed;

    /// <summary>Temps restant avant déclenchement, planché à zéro.</summary>
    public float Remaining => MathF.Max(0f, Duration - _elapsed);

    public TimeElapsedCondition(float InSeconds)
    {
        Duration = MathF.Max(0f, InSeconds);
    }

    public bool Evaluate(TState InCurrent, float InDelta)
    {
        _elapsed += InDelta;
        return _elapsed >= Duration;
    }

    public void Reset() => _elapsed = 0f;
}
