// src/Logic/BossStateMachine.cs
// ─────────────────────────────────────────────────────────────────────────────
// State machine that governs the overall boss fight flow.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// State diagram:
//
//   Intro ──[StartBoss]──► Phase1
//                              │
//              [OnPhaseHealthDepleted(1)]
//                              ▼
//                         Transition ──[OnTransitionAnimationComplete]──► Phase2
//                                                                             │
//                                                        [OnPhaseHealthDepleted(2)]
//                                                                             ▼
//                                                                        Transition ──► Phase3
//                                                                                          │
//                                                              [OnPhaseHealthDepleted(3)]  │
//                                                                                          ▼
//                                                                                     Transition ──► Defeated
//
// Timing is driven entirely by external callbacks (animation signals, HP events).
// Update() is intentionally a no-op — the machine has no internal timers.
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>States of the boss fight from spawn to defeat.</summary>
public enum BossState
{
    /// <summary>Intro cinematic is playing; boss is not yet hittable.</summary>
    Intro,

    /// <summary>Phase 1 active — TinyHand weak point exposed.</summary>
    Phase1,

    /// <summary>
    /// Between-phase transition animation is playing.
    /// Weak points are disabled; no damage is accepted.
    /// </summary>
    Transition,

    /// <summary>Phase 2 active — Toupee weak point exposed.</summary>
    Phase2,

    /// <summary>Phase 3 active — Statue/Tentacle weak point exposed.</summary>
    Phase3,

    /// <summary>All phases complete; defeat animation is playing.</summary>
    Defeated,
}

/// <summary>
/// Orchestrates the three-phase boss fight.
/// <para>
/// Boss.cs drives this machine via two external callbacks:
/// <list type="bullet">
///   <item><see cref="OnPhaseHealthDepleted"/> — called by BossPhaseHealth when a phase's HP reaches zero.</item>
///   <item><see cref="OnTransitionAnimationComplete"/> — called by Boss.cs when the phase-change animation finishes.</item>
/// </list>
/// </para>
/// </summary>
public sealed class BossStateMachine : StateMachine<BossState>
{
    // ── Private fields ──────────────────────────────────────────────────────

    /// <summary>The phase number that just ended, set in <see cref="OnPhaseHealthDepleted"/>.</summary>
    private int _completedPhase;

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>Starts the machine in <see cref="BossState.Intro"/>.</summary>
    public BossStateMachine() : base(BossState.Intro) { }

    // ── Convenience property ────────────────────────────────────────────────

    /// <summary>
    /// Returns the 1-indexed phase number while in an active phase, or 0 otherwise.
    /// </summary>
    public int CurrentPhase => CurrentState switch
    {
        BossState.Phase1 => 1,
        BossState.Phase2 => 2,
        BossState.Phase3 => 3,
        _                => 0,
    };

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Boss.cs after the intro animation completes.
    /// Transitions from <see cref="BossState.Intro"/> to <see cref="BossState.Phase1"/>.
    /// </summary>
    public void StartBoss() => Transition(BossState.Phase1);

    /// <summary>
    /// Called by Boss.cs / BossPhaseHealth when the current phase's HP reaches zero.
    /// Guards against stale or out-of-order calls.
    /// </summary>
    /// <param name="phase">The 1-indexed phase number that just died (1, 2, or 3).</param>
    public void OnPhaseHealthDepleted(int phase)
    {
        // Only valid while an active phase is running.
        if (CurrentState is not (BossState.Phase1 or BossState.Phase2 or BossState.Phase3))
            return;

        // Ignore stale calls (e.g. a bullet that connected the same frame HP hit 0).
        if (phase != CurrentPhase)
            return;

        _completedPhase = phase;
        Transition(BossState.Transition);
    }

    /// <summary>
    /// Called by Boss.cs when the phase-change animation finishes playing.
    /// Determines the next state from <c>_completedPhase</c> and transitions to it.
    /// </summary>
    public void OnTransitionAnimationComplete()
    {
        if (CurrentState != BossState.Transition)
            return;

        BossState next = _completedPhase switch
        {
            1 => BossState.Phase2,
            2 => BossState.Phase3,
            3 => BossState.Defeated,
            _ => CurrentState,   // unreachable; safe no-op fallback
        };

        Transition(next);
    }

    // ── StateMachine<T> overrides ───────────────────────────────────────────

    /// <summary>
    /// Enforces the legal transition graph.
    /// Any transition not listed here returns <c>false</c> and is silently ignored.
    /// </summary>
    protected override bool CanTransition(BossState from, BossState to) => (from, to) switch
    {
        // Intro → active fight
        (BossState.Intro,      BossState.Phase1)    => true,

        // Active phases → transition animation
        (BossState.Phase1,     BossState.Transition) => true,
        (BossState.Phase2,     BossState.Transition) => true,
        (BossState.Phase3,     BossState.Transition) => true,

        // Transition animation → next phase or defeat
        (BossState.Transition, BossState.Phase2)     => true,
        (BossState.Transition, BossState.Phase3)     => true,
        (BossState.Transition, BossState.Defeated)   => true,

        // Everything else is illegal
        _                                            => false,
    };

    /// <summary>
    /// No internal timers — all timing is driven by external animation callbacks.
    /// </summary>
    public override void Update(double delta) { }
}
