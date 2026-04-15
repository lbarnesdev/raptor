// src/Logic/StateMachine.cs
// ─────────────────────────────────────────────────────────────────────────────
// Generic finite state machine base class.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// Usage:
//   1. Define a state enum (e.g. ShieldState, BossState).
//   2. Subclass StateMachine<TState>.
//   3. Implement Update(double delta) to tick timers / drive transitions.
//   4. Override CanTransition to guard illegal transitions.
//   5. Override OnEnter / OnExit to respond to state changes.
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>
/// Generic finite state machine base class.
/// TState must be an <see cref="Enum"/>; use a dedicated enum per machine.
/// </summary>
public abstract class StateMachine<TState> where TState : Enum
{
    // ── Public state ────────────────────────────────────────────────────────

    /// <summary>The state the machine is currently in.</summary>
    public TState CurrentState { get; private set; }

    /// <summary>
    /// The state the machine was in before the most recent transition.
    /// Equals <see cref="CurrentState"/> until the first transition occurs.
    /// </summary>
    public TState PreviousState { get; private set; }

    // ── Construction ────────────────────────────────────────────────────────

    /// <param name="initialState">Starting state. No OnEnter is called for the initial state.</param>
    protected StateMachine(TState initialState)
    {
        CurrentState  = initialState;
        PreviousState = initialState;
    }

    // ── Core API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to move the machine to <paramref name="next"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the transition succeeded;
    /// <c>false</c> if <see cref="CanTransition"/> blocked it (state is unchanged).
    /// </returns>
    public bool Transition(TState next)
    {
        if (!CanTransition(CurrentState, next))
            return false;

        TState previous = CurrentState;
        OnExit(previous);

        PreviousState = previous;
        CurrentState  = next;

        OnEnter(CurrentState);
        return true;
    }

    /// <summary>
    /// Called every frame (or physics step) by the owning Godot node.
    /// Subclasses tick timers here and call <see cref="Transition"/> when
    /// a timed condition fires.
    /// </summary>
    public abstract void Update(double delta);

    // ── Extension points ────────────────────────────────────────────────────

    /// <summary>
    /// Return <c>false</c> to veto a transition before it happens.
    /// Default implementation allows all transitions.
    /// </summary>
    protected virtual bool CanTransition(TState from, TState to) => true;

    /// <summary>Called immediately after <see cref="CurrentState"/> changes to <paramref name="state"/>.</summary>
    protected virtual void OnEnter(TState state) { }

    /// <summary>Called immediately before <see cref="CurrentState"/> changes away from <paramref name="state"/>.</summary>
    protected virtual void OnExit(TState state) { }
}
