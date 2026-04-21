// src/Logic/EnemyStateMachine.cs
// ─────────────────────────────────────────────────────────────────────────────
// Shared finite state machine for flying enemy types (WraithFighter,
// SpecterFighter).  HarbingerTurret is stationary and does not use this FSM.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// State diagram:
//
//   FormationHold ──[StartAttackRun()]──► AttackRun ──[StartFlee()]──► Fleeing
//         │                                  │                            │
//         └──────────── Kill() ──────────────┴──────────── Kill() ───────┴──► Dead
//
// Guard rules:
//   Dead is a terminal state — no transition out of Dead is permitted.
//   AttackRun only reachable from FormationHold.
//   Fleeing only reachable from AttackRun.
//   Dead reachable from any state.
//
// Timers are NOT owned by this class.  Callers (WraithFighter, SpecterFighter)
// control timing via async/await and drive transitions through the public API.
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>Four states a flying enemy can occupy.</summary>
public enum EnemyState
{
    /// <summary>Enters screen in formation; drifts toward player at low speed.</summary>
    FormationHold,

    /// <summary>Breaks formation; arcs / dives at player and fires.</summary>
    AttackRun,

    /// <summary>Attack complete; exits left at high speed.</summary>
    Fleeing,

    /// <summary>Terminal state — health reached zero.</summary>
    Dead,
}

/// <summary>
/// Finite state machine that governs the lifecycle of a flying enemy.
/// </summary>
public sealed class EnemyStateMachine : StateMachine<EnemyState>
{
    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>Starts in <see cref="EnemyState.FormationHold"/>.</summary>
    public EnemyStateMachine() : base(EnemyState.FormationHold) { }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Transition <c>FormationHold → AttackRun</c>.
    /// No-op (returns <c>false</c>) if called from any other state.
    /// </summary>
    public bool StartAttackRun() => Transition(EnemyState.AttackRun);

    /// <summary>
    /// Transition <c>AttackRun → Fleeing</c>.
    /// No-op (returns <c>false</c>) if called from any other state.
    /// </summary>
    public bool StartFlee() => Transition(EnemyState.Fleeing);

    /// <summary>
    /// Transition to <c>Dead</c> from any non-terminal state.
    /// Safe to call when already <c>Dead</c> — returns <c>false</c>.
    /// </summary>
    public bool Kill() => Transition(EnemyState.Dead);

    // ── StateMachine<T> overrides ───────────────────────────────────────────

    /// <summary>
    /// Enforces the guard rules: Dead is terminal; other transitions are
    /// strictly sequential except Kill() which is always permitted.
    /// </summary>
    protected override bool CanTransition(EnemyState from, EnemyState to)
    {
        // Dead is a terminal state — no escape.
        if (from == EnemyState.Dead) return false;

        return to switch
        {
            EnemyState.AttackRun => from == EnemyState.FormationHold,
            EnemyState.Fleeing   => from == EnemyState.AttackRun,
            EnemyState.Dead      => true,   // Kill() works from any live state.
            _                    => false,
        };
    }

    /// <summary>
    /// No internal timers — callers own timing via async/await.
    /// </summary>
    public override void Update(double delta) { }
}
