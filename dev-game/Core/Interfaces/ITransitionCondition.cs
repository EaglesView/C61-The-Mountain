using System;

namespace Core.Shared.StateMachine;

/// <summary>
/// Condition évaluée par <see cref="StateMachine{TState}"/> à chaque appel à
/// <see cref="StateMachine{TState}.Tick(float)"/> pour déclencher une transition automatique.
/// Implémentations fournies&#160;: <see cref="TimeElapsedCondition{TState}"/>,
/// <see cref="PredicateCondition{TState}"/>.
/// </summary>
public interface ITransitionCondition<TState> where TState : struct, Enum
{
	/// <summary>
	/// Évalue la condition. Retourne <c>true</c> si la transition associée doit se déclencher.
	/// </summary>
	/// <param name="InCurrent">État courant de la machine au moment du tick.</param>
	/// <param name="InDelta">Pas de temps depuis le dernier <c>Tick</c> (secondes).</param>
	bool Evaluate(TState InCurrent, float InDelta);

	/// <summary>
	/// Réinitialise les compteurs internes. Appelée par la machine chaque fois que
	/// l'état source de cette condition est (ré)entré, ou à l'enregistrement si la
	/// machine est déjà dans cet état.
	/// </summary>
	void Reset();
}
