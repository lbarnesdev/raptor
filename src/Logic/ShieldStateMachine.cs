// src/Logic/ShieldStateMachine.cs
// ─────────────────────────────────────────────────────────────────────────────
// Shield state machine for the player ship.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// State diagram:
//
//   Active ──[hit]──► GracePeriod ──[timer]──► Broken ──[timer]──► Active
//     ▲                                                               │
//     └───────────────────────────────────────────────────────────────┘
//
//   Recharging ──[hit]──► GracePeriod   (Recharging is an optional
//                                        externally-triggered state)
//
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>Four states the player shield can occupy.</summary>
public enum ShieldState
{
    /// <summary>Shield is up and blocking hits.</summary>
    Active,

    /// <summary>
    /// Brief invincibility window after absorbing a hit.
    /// The shield is still "up" but another hit during this window does not
    /// reset the grace timer — it simply returns true (absorbed).
    /// </summary>
    GracePeriod,

    /// <summary>Shield is down. Hits reach the player (TryAbsorbHit returns false).</summary>
    Broken,

    /// <summary>
    /// Optional externally-triggered state. Behaves identically to Active for
    /// hit-absorption purposes: a hit transitions to GracePeriod.
    /// </summary>
    Recharging,
}

/// <summary>
/// Finite state machine that governs player-shield behaviour.
/// Tick <see cref="Update"/> every frame to advance internal timers.
/// </summary>
public sealed class ShieldStateMachine : StateMachine<ShieldState>
{
    // ── Tuning properties ───────────────────────────────────────────────────

    /// <summary>
    /// Seconds the shield stays in <see cref="ShieldState.Broken"/> before
    /// automatically transitioning back to <see cref="ShieldState.Active"/>.
    /// </summary>
    public float RechargeTime { get; init; } = 4.0f;

    /// <summary>
    /// Seconds the shield stays in <see cref="ShieldState.GracePeriod"/> before
    /// transitioning to <see cref="ShieldState.Broken"/>.
    /// </summary>
    public float GraceDuration { get; init; } = 0.6f;

    // ── Convenience query ───────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when the shield is in <see cref="ShieldState.Broken"/> and
    /// the player takes full damage on the next hit.
    /// </summary>
    public bool IsVulnerable => CurrentState == ShieldState.Broken;

    // ── Internal timers ─────────────────────────────────────────────────────

    private float _graceTimer    = 0f;
    private float _rechargeTimer = 0f;

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>Starts the machine in <see cref="ShieldState.Active"/>.</summary>
    public ShieldStateMachine() : base(ShieldState.Active) { }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to absorb an incoming hit.
    /// </summary>
    /// <returns>
    /// <c>true</c>  — hit absorbed (shield handled it; player takes no damage).<br/>
    /// <c>false</c> — shield is <see cref="ShieldState.Broken"/>; hit reaches the player.
    /// </returns>
    public bool TryAbsorbHit()
    {
        switch (CurrentState)
        {
            case ShieldState.Active:
                Transition(ShieldState.GracePeriod);
                return true;

            case ShieldState.GracePeriod:
                // Grace timer is NOT reset on additional hits during the window.
                return true;

            case ShieldState.Broken:
                return false;

            case ShieldState.Recharging:
                Transition(ShieldState.GracePeriod);
                return true;

            default:
                return false;
        }
    }

    // ── StateMachine<T> overrides ───────────────────────────────────────────

    /// <summary>
    /// Advance timers. Call once per frame (or physics step) with the elapsed
    /// seconds since the last call.
    /// </summary>
    public override void Update(double delta)
    {
        switch (CurrentState)
        {
            case ShieldState.GracePeriod:
                _graceTimer -= (float)delta;
                if (_graceTimer <= 0f)
                    Transition(ShieldState.Broken);
                break;

            case ShieldState.Broken:
                _rechargeTimer -= (float)delta;
                if (_rechargeTimer <= 0f)
                    Transition(ShieldState.Active);
                break;
        }
    }

    /// <summary>Initialise the appropriate timer when entering a timed state.</summary>
    protected override void OnEnter(ShieldState state)
    {
        switch (state)
        {
            case ShieldState.GracePeriod:
                _graceTimer = GraceDuration;
                break;

            case ShieldState.Broken:
                _rechargeTimer = RechargeTime;
                break;
        }
    }
}
