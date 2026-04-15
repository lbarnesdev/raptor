// src/Logic/BossPhaseHealth.cs
// ─────────────────────────────────────────────────────────────────────────────
// Per-phase HP tracking for the boss fight.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// Lifecycle:
//   1. Boss.cs creates a BossPhaseHealth with three PhaseConfigs.
//   2. Each WeakPoint hit calls ApplyHit().
//   3. When IsCurrentPhaseDead, Boss.cs calls SetTransitionLock(true) to
//      prevent double-processing during the phase-change animation.
//   4. After the animation completes Boss.cs calls AdvancePhase(), which
//      promotes CurrentPhase, resets HP, and clears IsTransitionLocked.
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>
/// Tuning data for a single boss phase.
/// </summary>
/// <param name="MaxHp">Hit-points the weak point has in this phase.</param>
/// <param name="MissileMultiplier">
/// Multiplier applied to missile damage for this phase (default 3).
/// Missiles are deliberately more powerful to reward resource management.
/// </param>
public record PhaseConfig(int MaxHp, int MissileMultiplier = 3);

/// <summary>
/// Tracks hit-points across all three boss phases and enforces transition
/// gating so no extra hits bleed through during phase-change animations.
/// </summary>
public sealed class BossPhaseHealth
{
    // ── Private fields ──────────────────────────────────────────────────────

    private readonly PhaseConfig[] _phases;

    // ── Public state ────────────────────────────────────────────────────────

    /// <summary>
    /// Current phase number, 1-indexed.
    /// Starts at 1; advances to at most <c>_phases.Length</c>.
    /// </summary>
    public int CurrentPhase { get; private set; } = 1;

    /// <summary>Remaining HP for the current phase.</summary>
    public int CurrentHp { get; private set; }

    /// <summary>Maximum HP for the current phase.</summary>
    public int MaxHp => _phases[CurrentPhase - 1].MaxHp;

    /// <summary>
    /// <c>true</c> when <see cref="CurrentHp"/> has reached zero.
    /// Cleared automatically when <see cref="AdvancePhase"/> resets HP.
    /// </summary>
    public bool IsCurrentPhaseDead => CurrentHp <= 0;

    /// <summary>
    /// When <c>true</c>, <see cref="ApplyHit"/> is a no-op and returns
    /// <c>false</c>. Set externally by Boss.cs during transition animations
    /// to prevent double-processing.
    /// </summary>
    public bool IsTransitionLocked { get; private set; }

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise with one <see cref="PhaseConfig"/> per phase.
    /// For RAPTOR pass exactly three configs (phases 1, 2, 3).
    /// </summary>
    /// <param name="phases">
    /// Array of phase configs, ordered phase-1-first.
    /// Must not be null or empty.
    /// </param>
    public BossPhaseHealth(PhaseConfig[] phases)
    {
        if (phases is null || phases.Length == 0)
            throw new ArgumentException("At least one phase config is required.", nameof(phases));

        _phases   = phases;
        CurrentHp = phases[0].MaxHp;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to apply damage to the current phase.
    /// </summary>
    /// <param name="damage">Raw damage amount (before missile multiplier).</param>
    /// <param name="isMissile">
    /// When <c>true</c> the damage is multiplied by the current phase's
    /// <see cref="PhaseConfig.MissileMultiplier"/> before being applied.
    /// </param>
    /// <returns>
    /// <c>true</c>  — damage was applied (HP reduced).<br/>
    /// <c>false</c> — hit was rejected: either <see cref="IsTransitionLocked"/>
    ///                is set or the phase HP is already zero.
    /// </returns>
    public bool ApplyHit(int damage, bool isMissile)
    {
        if (IsTransitionLocked || IsCurrentPhaseDead)
            return false;

        int effective = isMissile
            ? damage * _phases[CurrentPhase - 1].MissileMultiplier
            : damage;

        CurrentHp = Math.Max(0, CurrentHp - effective);
        return true;
    }

    /// <summary>
    /// Advance to the next phase, resetting HP and clearing the transition lock.
    /// If already on the final phase this is a no-op.
    /// </summary>
    public void AdvancePhase()
    {
        if (CurrentPhase >= _phases.Length)
            return;

        CurrentPhase++;
        CurrentHp           = MaxHp;   // MaxHp now reflects the new CurrentPhase
        IsTransitionLocked  = false;
    }

    /// <summary>
    /// Set or clear the transition lock.
    /// Call with <c>true</c> as soon as a phase dies to block stray hits;
    /// <see cref="AdvancePhase"/> clears it automatically.
    /// </summary>
    public void SetTransitionLock(bool locked)
    {
        IsTransitionLocked = locked;
    }
}
