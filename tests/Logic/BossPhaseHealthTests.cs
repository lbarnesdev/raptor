// tests/Logic/BossPhaseHealthTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for BossPhaseHealth.
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered (matching ticket spec):
//   1. Phase 1: ApplyHit(1, false) reduces HP
//   2. ApplyHit with isMissile=true applies MissileMultiplier
//   3. IsCurrentPhaseDead true when HP reaches 0
//   4. ApplyHit returns false when IsTransitionLocked
//   5. ApplyHit returns false when HP already 0
//   6. AdvancePhase increments phase, resets HP to new MaxHp
//   7. All 3 phases can be advanced through in sequence
//   8. AdvancePhase on phase 3 is a no-op (no phase 4)
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class BossPhaseHealthTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Standard 3-phase config used by most tests:
    ///   Phase 1 — 20 HP, ×3 missile
    ///   Phase 2 — 15 HP, ×3 missile
    ///   Phase 3 — 10 HP, ×3 missile
    /// </summary>
    private static BossPhaseHealth MakeDefault() =>
        new(new PhaseConfig[]
        {
            new(MaxHp: 20, MissileMultiplier: 3),
            new(MaxHp: 15, MissileMultiplier: 3),
            new(MaxHp: 10, MissileMultiplier: 3),
        });

    // ── Construction sanity ──────────────────────────────────────────────────

    [Fact]
    public void NewInstance_StartsAtPhase1()
    {
        var bph = MakeDefault();
        Assert.Equal(1, bph.CurrentPhase);
    }

    [Fact]
    public void NewInstance_CurrentHpEqualsPhase1MaxHp()
    {
        var bph = MakeDefault();
        Assert.Equal(20, bph.CurrentHp);
        Assert.Equal(20, bph.MaxHp);
    }

    [Fact]
    public void NewInstance_IsNotDead()
    {
        var bph = MakeDefault();
        Assert.False(bph.IsCurrentPhaseDead);
    }

    [Fact]
    public void NewInstance_IsNotTransitionLocked()
    {
        var bph = MakeDefault();
        Assert.False(bph.IsTransitionLocked);
    }

    // ── 1. ApplyHit(damage, false) reduces HP ────────────────────────────────

    [Fact]
    public void ApplyHit_NormalDamage_ReducesHp()
    {
        var bph = MakeDefault();

        bph.ApplyHit(1, false);

        Assert.Equal(19, bph.CurrentHp);
    }

    [Fact]
    public void ApplyHit_NormalDamage_ReturnsTrue()
    {
        var bph = MakeDefault();

        bool result = bph.ApplyHit(5, false);

        Assert.True(result);
    }

    [Fact]
    public void ApplyHit_MultipleDamages_AccumulatesCorrectly()
    {
        var bph = MakeDefault();

        bph.ApplyHit(3, false);
        bph.ApplyHit(4, false);

        Assert.Equal(13, bph.CurrentHp); // 20 - 3 - 4
    }

    // ── 2. ApplyHit with isMissile=true applies MissileMultiplier ────────────

    [Fact]
    public void ApplyHit_MissileDamage_AppliesMultiplier()
    {
        // Phase 1 MissileMultiplier = 3; damage=2 → effective=6
        var bph = MakeDefault();

        bph.ApplyHit(2, true);

        Assert.Equal(14, bph.CurrentHp); // 20 - (2×3)
    }

    [Fact]
    public void ApplyHit_MissileDamage_ReturnsTrue()
    {
        var bph = MakeDefault();

        bool result = bph.ApplyHit(1, true);

        Assert.True(result);
    }

    [Theory]
    [InlineData(1, false, 1,  19)] // normal hit
    [InlineData(1, true,  3,  17)] // missile: 1×3=3
    [InlineData(2, true,  6,  14)] // missile: 2×3=6
    [InlineData(5, false, 5,  15)] // normal: 5
    public void ApplyHit_DamageCalculation(
        int rawDamage, bool isMissile, int expectedEffective, int expectedHp)
    {
        var bph = MakeDefault(); // starts at HP=20

        bph.ApplyHit(rawDamage, isMissile);

        Assert.Equal(expectedHp, bph.CurrentHp);
        // Sanity: effective damage = 20 - expectedHp
        Assert.Equal(expectedEffective, 20 - bph.CurrentHp);
    }

    // ── 3. IsCurrentPhaseDead when HP reaches 0 ──────────────────────────────

    [Fact]
    public void IsCurrentPhaseDead_FalseWhileHpAboveZero()
    {
        var bph = MakeDefault();

        bph.ApplyHit(19, false); // 1 HP remaining

        Assert.False(bph.IsCurrentPhaseDead);
    }

    [Fact]
    public void IsCurrentPhaseDead_TrueWhenHpReachesZero()
    {
        var bph = MakeDefault();

        bph.ApplyHit(20, false); // exact kill

        Assert.True(bph.IsCurrentPhaseDead);
    }

    [Fact]
    public void ApplyHit_DoesNotGoBelowZeroHp()
    {
        // Over-damage should floor at 0, not go negative.
        var bph = MakeDefault();

        bph.ApplyHit(999, false);

        Assert.Equal(0, bph.CurrentHp);
    }

    // ── 4. ApplyHit returns false when IsTransitionLocked ────────────────────

    [Fact]
    public void ApplyHit_ReturnsFalse_WhenTransitionLocked()
    {
        var bph = MakeDefault();
        bph.SetTransitionLock(true);

        bool result = bph.ApplyHit(1, false);

        Assert.False(result);
    }

    [Fact]
    public void ApplyHit_DoesNotChangeHp_WhenTransitionLocked()
    {
        var bph = MakeDefault();
        bph.SetTransitionLock(true);

        bph.ApplyHit(5, false);

        Assert.Equal(20, bph.CurrentHp);
    }

    [Fact]
    public void ApplyHit_Resumes_AfterTransitionLockCleared()
    {
        var bph = MakeDefault();
        bph.SetTransitionLock(true);
        bph.ApplyHit(5, false); // blocked
        bph.SetTransitionLock(false);

        bph.ApplyHit(5, false); // now allowed

        Assert.Equal(15, bph.CurrentHp);
    }

    // ── 5. ApplyHit returns false when HP already 0 ──────────────────────────

    [Fact]
    public void ApplyHit_ReturnsFalse_WhenHpAlreadyZero()
    {
        var bph = MakeDefault();
        bph.ApplyHit(20, false); // phase dead

        bool result = bph.ApplyHit(1, false);

        Assert.False(result);
    }

    [Fact]
    public void ApplyHit_DoesNotChangeHp_WhenHpAlreadyZero()
    {
        var bph = MakeDefault();
        bph.ApplyHit(20, false);

        bph.ApplyHit(1, false);

        Assert.Equal(0, bph.CurrentHp);
    }

    // ── 6. AdvancePhase increments phase and resets HP ───────────────────────

    [Fact]
    public void AdvancePhase_IncrementsPhase()
    {
        var bph = MakeDefault();

        bph.AdvancePhase();

        Assert.Equal(2, bph.CurrentPhase);
    }

    [Fact]
    public void AdvancePhase_ResetsHpToNewMaxHp()
    {
        // Phase 2 MaxHp = 15
        var bph = MakeDefault();
        bph.ApplyHit(10, false); // damage phase 1 first

        bph.AdvancePhase();

        Assert.Equal(15, bph.CurrentHp);
        Assert.Equal(15, bph.MaxHp);
    }

    [Fact]
    public void AdvancePhase_ClearsTransitionLock()
    {
        var bph = MakeDefault();
        bph.ApplyHit(20, false);           // kill phase 1
        bph.SetTransitionLock(true);       // Boss.cs sets this during animation

        bph.AdvancePhase();

        Assert.False(bph.IsTransitionLocked);
    }

    // ── 7. All 3 phases can be advanced through in sequence ──────────────────

    [Fact]
    public void FullPhaseSequence_Phase1To2To3_CorrectStats()
    {
        var bph = new BossPhaseHealth(new PhaseConfig[]
        {
            new(MaxHp: 20, MissileMultiplier: 3),
            new(MaxHp: 15, MissileMultiplier: 4),
            new(MaxHp: 10, MissileMultiplier: 5),
        });

        // Phase 1 → 2
        Assert.Equal(1,  bph.CurrentPhase);
        Assert.Equal(20, bph.CurrentHp);
        Assert.Equal(20, bph.MaxHp);

        bph.ApplyHit(20, false);
        bph.AdvancePhase();

        Assert.Equal(2,  bph.CurrentPhase);
        Assert.Equal(15, bph.CurrentHp);
        Assert.Equal(15, bph.MaxHp);

        // Phase 2 → 3
        bph.ApplyHit(15, false);
        bph.AdvancePhase();

        Assert.Equal(3,  bph.CurrentPhase);
        Assert.Equal(10, bph.CurrentHp);
        Assert.Equal(10, bph.MaxHp);
    }

    [Fact]
    public void FullPhaseSequence_MissileMultipliersApplyPerPhase()
    {
        // Each phase has a different multiplier; verify each is applied correctly.
        var bph = new BossPhaseHealth(new PhaseConfig[]
        {
            new(MaxHp: 30, MissileMultiplier: 3),
            new(MaxHp: 30, MissileMultiplier: 4),
            new(MaxHp: 30, MissileMultiplier: 5),
        });

        // Phase 1: 1 missile hit = 3 effective
        bph.ApplyHit(1, true);
        Assert.Equal(27, bph.CurrentHp);

        bph.ApplyHit(30, false); // kill phase 1
        bph.AdvancePhase();

        // Phase 2: 1 missile hit = 4 effective
        bph.ApplyHit(1, true);
        Assert.Equal(26, bph.CurrentHp);

        bph.ApplyHit(30, false);
        bph.AdvancePhase();

        // Phase 3: 1 missile hit = 5 effective
        bph.ApplyHit(1, true);
        Assert.Equal(25, bph.CurrentHp);
    }

    // ── 8. AdvancePhase on final phase is a no-op ────────────────────────────

    [Fact]
    public void AdvancePhase_OnFinalPhase_DoesNotChangePhasNumber()
    {
        var bph = MakeDefault();
        bph.AdvancePhase(); // → 2
        bph.AdvancePhase(); // → 3 (final)

        bph.AdvancePhase(); // should be no-op

        Assert.Equal(3, bph.CurrentPhase);
    }

    [Fact]
    public void AdvancePhase_OnFinalPhase_DoesNotResetHp()
    {
        var bph = MakeDefault();
        bph.AdvancePhase(); // → 2
        bph.AdvancePhase(); // → 3
        bph.ApplyHit(4, false); // take some damage in phase 3

        bph.AdvancePhase(); // no-op

        Assert.Equal(6, bph.CurrentHp); // 10 - 4, unchanged
    }

    [Fact]
    public void AdvancePhase_OnFinalPhase_DoesNotClearTransitionLock()
    {
        // If somehow locked while on phase 3, the no-op advance should not touch the lock.
        var bph = MakeDefault();
        bph.AdvancePhase();
        bph.AdvancePhase(); // now phase 3
        bph.SetTransitionLock(true);

        bph.AdvancePhase(); // no-op

        Assert.True(bph.IsTransitionLocked);
    }

    // ── Constructor guard ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnNullPhases()
    {
        Assert.Throws<ArgumentException>(() => new BossPhaseHealth(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyPhases()
    {
        Assert.Throws<ArgumentException>(() => new BossPhaseHealth(Array.Empty<PhaseConfig>()));
    }
}
