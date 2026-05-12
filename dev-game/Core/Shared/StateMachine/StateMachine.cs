using System;
using System.Collections.Generic;

namespace Core.Shared.StateMachine;


/// <summary>
/// Machine à états générique, utilisée par composition (jamais en héritage).
/// Supporte&#160;:
/// <list type="bullet">
/// <item>transitions impératives via <see cref="TransitionTo(TState)"/></item>
/// <item>transitions automatiques via <see cref="When(TState, ITransitionCondition{TState}, TState)"/>
/// + <see cref="Tick(float)"/></item>
/// <item>pile d'états pour pause/reprise via <see cref="Push(TState)"/>/<see cref="Pop"/></item>
/// <item>callbacks <c>onEnter</c>/<c>onExit</c> et événement <see cref="Transitioned"/> pour les abonnés externes</item>
/// </list>
/// Aucune connaissance du réseau&#160;: les owners qui veulent répliquer une transition
/// le font dans leur propre callback ou abonné.
/// </summary>
/// <typeparam name="TState">Enum décrivant les états possibles.</typeparam>
public sealed class StateMachine<TState> where TState : struct, Enum
{
    private static readonly EqualityComparer<TState> Eq = EqualityComparer<TState>.Default;

    private readonly Action<TState>? _onEnter;
    private readonly Action<TState>? _onExit;

    private readonly Dictionary<TState, List<Rule>> _rules = new();
    private readonly Stack<TState> _resumeStack = new();

    /// <summary>État courant.</summary>
    public TState Current { get; private set; }

    /// <summary>État précédant la dernière transition réussie.</summary>
    public TState Previous { get; private set; }

    /// <summary>
    /// Émis après chaque transition réussie&#160;: <c>(from, to)</c>. Le callback se produit
    /// après <c>onExit</c>, après l'application de <see cref="Current"/>, et après <c>onEnter</c>.
    /// </summary>
    public event Action<TState, TState>? Transitioned;

    /// <summary>
    /// Construit la machine dans l'état initial donné.
    /// Le callback <c>onEnter</c> n'est PAS invoqué pour cet état initial&#160;: l'owner est
    /// responsable de son setup (typiquement dans <c>_Ready</c>). Cette convention évite
    /// d'invoquer un callback alors que l'owner n'est pas pleinement initialisé.
    /// </summary>
    public StateMachine(
        TState InInitial,
        Action<TState>? InOnEnter = null,
        Action<TState>? InOnExit = null)
    {
        Current = InInitial;
        Previous = InInitial;
        _onEnter = InOnEnter;
        _onExit = InOnExit;
    }

    /// <summary>Indique si la machine est dans un état donné.</summary>
    public bool Is(TState InState) => Eq.Equals(Current, InState);

    /// <summary>
    /// Transition impérative. Aucun effet (et retour <c>false</c>) si <paramref name="InNext"/>
    /// est déjà l'état courant.
    /// </summary>
    public bool TransitionTo(TState InNext)
    {
        if (Eq.Equals(Current, InNext)) return false;

        Previous = Current;
        _onExit?.Invoke(Current);
        Current = InNext;
        ResetRulesFor(Current);
        _onEnter?.Invoke(Current);
        Transitioned?.Invoke(Previous, Current);
        return true;
    }

    /// <summary>
    /// Empile l'état courant et transitionne vers <paramref name="InNext"/>. À apparier
    /// avec <see cref="Pop"/> pour restaurer. Utile pour les états interruptifs (ex.&#160;: pause).
    /// </summary>
    public bool Push(TState InNext)
    {
        if (Eq.Equals(Current, InNext)) return false;
        _resumeStack.Push(Current);
        return TransitionTo(InNext);
    }

    /// <summary>
    /// Restaure le dernier état empilé via <see cref="Push"/>. Retourne <c>false</c>
    /// si la pile est vide ou si la cible est déjà l'état courant.
    /// </summary>
    public bool Pop()
    {
        if (_resumeStack.Count == 0) return false;
        return TransitionTo(_resumeStack.Pop());
    }

    /// <summary>Vide la pile de reprise. À appeler lors d'un reset complet (ex.&#160;: nouvelle partie).</summary>
    public void ClearResumeStack() => _resumeStack.Clear();

    /// <summary>
    /// Enregistre une transition conditionnelle&#160;: lorsque la machine est dans
    /// <paramref name="InFrom"/> et que <paramref name="InCondition"/> évalue <c>true</c>
    /// pendant un <see cref="Tick(float)"/>, la machine transitionne vers <paramref name="InTo"/>.
    /// Plusieurs règles par état source autorisées&#160;: la première à évaluer <c>true</c>,
    /// dans l'ordre d'enregistrement, gagne.
    /// </summary>
    public void When(TState InFrom, ITransitionCondition<TState> InCondition, TState InTo)
    {
        if (InCondition is null) throw new ArgumentNullException(nameof(InCondition));

        if (!_rules.TryGetValue(InFrom, out var list))
        {
            list = new List<Rule>();
            _rules[InFrom] = list;
        }
        list.Add(new Rule(InCondition, InTo));

        // Si la machine est déjà dans l'état source, on reset pour démarrer le compteur maintenant.
        if (Eq.Equals(InFrom, Current)) InCondition.Reset();
    }

    /// <summary>
    /// Évalue les règles enregistrées pour l'état courant. Une seule transition par tick.
    /// Appel typique depuis <c>_Process</c> ou <c>_PhysicsProcess</c>&#160;:
    /// <c>_fsm.Tick((float)delta);</c>
    /// </summary>
    public void Tick(float InDelta)
    {
        if (!_rules.TryGetValue(Current, out var rules)) return;

        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i].Condition.Evaluate(Current, InDelta))
            {
                TransitionTo(rules[i].Target);
                return;
            }
        }
    }

    private void ResetRulesFor(TState InState)
    {
        if (!_rules.TryGetValue(InState, out var rules)) return;
        for (int i = 0; i < rules.Count; i++) rules[i].Condition.Reset();
    }

    private readonly struct Rule
    {
        public readonly ITransitionCondition<TState> Condition;
        public readonly TState Target;

        public Rule(ITransitionCondition<TState> InCondition, TState InTarget)
        {
            Condition = InCondition;
            Target = InTarget;
        }
    }
}
