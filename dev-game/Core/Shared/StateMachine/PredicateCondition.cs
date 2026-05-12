using System;

namespace Core.Shared.StateMachine;

/// <summary>
/// Condition pilotée par une fonction externe. Utile pour des transitions dépendant
/// d'un état du monde (ex.&#160;: «&#160;tous les pairs prêts&#160;», «&#160;joueur sur le sol&#160;»).
/// La fonction est ré-évaluée à chaque <c>Tick</c>.
/// </summary>
public sealed class PredicateCondition<TState> : ITransitionCondition<TState>
    where TState : struct, Enum
{
    private readonly Func<bool> _predicate;

    public PredicateCondition(Func<bool> InPredicate)
    {
        _predicate = InPredicate ?? throw new ArgumentNullException(nameof(InPredicate));
    }

    public bool Evaluate(TState InCurrent, float InDelta) => _predicate();

    public void Reset() { }
}
