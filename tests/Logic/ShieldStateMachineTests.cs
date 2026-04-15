// tests/Logic/ShieldStateMachineTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for ShieldStateMachine.
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered:
//   1. Active.TryAbsorbHit()     → state becomes GracePeriod, returns true
//   2. GracePeriod.TryAbsorbHit()→ stays GracePeriod, returns true (no timer reset)
//   3. Broken.TryAbsorbHit()     → stays Broken, returns false
//   4. After GraceDuration ticks → transitions to Broken
//   5. After RechargeTime from Broken → transitions to Active
//   6. Hit during Recharging     → restarts grace period (GracePeriod entered fresh)
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class ShieldStateMachineTests
{
    // ── 1. Active absorbs a hit ──────────────────────────────────────────────

    [Fact]
    public void Active_TryAbsorbHit_ReturnsTrue()
    {
        var sm = new ShieldStateMachine();

        bool result = sm.TryAbsorbHit();

        Assert.True(result);
    }

    [Fact]
    public void Active_TryAbsorbHit_TransitionsToGracePeriod()
    {
        var sm = new ShieldStateMachine();

        sm.TryAbsorbHit();

        Assert.Equal(ShieldState.GracePeriod, sm.CurrentState);
    }

    // ── 2. GracePeriod absorbs a hit (no timer reset) ───────────────────────

    [Fact]
    public void GracePeriod_TryAbsorbHit_ReturnsTrue()
    {
        var sm = new ShieldStateMachine();
        sm.TryAbsorbHit(); // Active → GracePeriod

        bool result = sm.TryAbsorbHit(); // hit during grace

        Assert.True(result);
    }

    [Fact]
    public void GracePeriod_TryAbsorbHit_StaysInGracePeriod()
    {
        var sm = new ShieldStateMachine();
        sm.TryAbsorbHit(); // Active → GracePeriod

        sm.TryAbsorbHit();

        Assert.Equal(ShieldState.GracePeriod, sm.CurrentState);
    }

    [Fact]
    public void GracePeriod_TryAbsorbHit_DoesNotResetTimer()
    {
        // Advance timer halfway through grace, take a second hit,
        // then finish the remaining half — should still go Broken.
        var sm = new ShieldStateMachine { GraceDuration = 1.0f };
        sm.TryAbsorbHit(); // Active → GracePeriod (timer = 1.0)

        sm.Update(0.5);         // timer = 0.5 — still in GracePeriod
        sm.TryAbsorbHit();      // hit during grace (timer stays at 0.5, NOT reset to 1.0)
        sm.Update(0.51);        // timer <= 0 → Broken

        Assert.Equal(ShieldState.Broken, sm.CurrentState);
    }

    // ── 3. Broken does NOT absorb a hit ─────────────────────────────────────

    [Fact]
    public void Broken_TryAbsorbHit_ReturnsFalse()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.1f };
        sm.TryAbsorbHit(); // Active → GracePeriod
        sm.Update(0.2);    // GracePeriod → Broken

        bool result = sm.TryAbsorbHit();

        Assert.False(result);
    }

    [Fact]
    public void Broken_TryAbsorbHit_StaysInBroken()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.1f };
        sm.TryAbsorbHit();
        sm.Update(0.2);

        sm.TryAbsorbHit();

        Assert.Equal(ShieldState.Broken, sm.CurrentState);
    }

    // ── 4. GraceDuration timer expires → Broken ─────────────────────────────

    [Fact]
    public void Update_AfterGraceDuration_TransitionsToBroken()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.6f };
        sm.TryAbsorbHit(); // Active → GracePeriod

        sm.Update(0.7); // exceeds GraceDuration

        Assert.Equal(ShieldState.Broken, sm.CurrentState);
    }

    [Fact]
    public void Update_BeforeGraceDurationExpires_StaysInGracePeriod()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.6f };
        sm.TryAbsorbHit();

        sm.Update(0.3); // less than GraceDuration

        Assert.Equal(ShieldState.GracePeriod, sm.CurrentState);
    }

    [Fact]
    public void Update_ExactlyAtGraceDuration_TransitionsToBroken()
    {
        // Timer ticks down to exactly 0 — should still trigger.
        var sm = new ShieldStateMachine { GraceDuration = 0.6f };
        sm.TryAbsorbHit();

        sm.Update(0.6); // exactly GraceDuration

        Assert.Equal(ShieldState.Broken, sm.CurrentState);
    }

    // ── 5. RechargeTime timer expires → Active ───────────────────────────────

    [Fact]
    public void Update_AfterRechargeTime_TransitionsToActive()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.1f, RechargeTime = 4.0f };
        sm.TryAbsorbHit();  // Active → GracePeriod
        sm.Update(0.2);     // GracePeriod → Broken

        sm.Update(4.1);     // exceeds RechargeTime → Active

        Assert.Equal(ShieldState.Active, sm.CurrentState);
    }

    [Fact]
    public void Update_BeforeRechargeTimeExpires_StaysInBroken()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.1f, RechargeTime = 4.0f };
        sm.TryAbsorbHit();
        sm.Update(0.2);

        sm.Update(2.0); // only half the recharge time

        Assert.Equal(ShieldState.Broken, sm.CurrentState);
    }

    // ── 6. Hit during Recharging → GracePeriod (timer restarted) ────────────

    [Fact]
    public void Recharging_TryAbsorbHit_ReturnsTrue()
    {
        var sm = new ShieldStateMachine();
        sm.Transition(ShieldState.Recharging); // external trigger

        bool result = sm.TryAbsorbHit();

        Assert.True(result);
    }

    [Fact]
    public void Recharging_TryAbsorbHit_TransitionsToGracePeriod()
    {
        var sm = new ShieldStateMachine();
        sm.Transition(ShieldState.Recharging);

        sm.TryAbsorbHit();

        Assert.Equal(ShieldState.GracePeriod, sm.CurrentState);
    }

    [Fact]
    public void Recharging_TryAbsorbHit_GracePeriodTimerIsFullyReset()
    {
        // After the hit from Recharging the grace timer should be the full
        // GraceDuration — i.e. a fresh GracePeriod, not a continuation.
        var sm = new ShieldStateMachine { GraceDuration = 1.0f };
        sm.Transition(ShieldState.Recharging);

        sm.TryAbsorbHit();       // Recharging → GracePeriod (timer reset to 1.0)
        sm.Update(0.9);          // still 0.1 left on the timer → still GracePeriod

        Assert.Equal(ShieldState.GracePeriod, sm.CurrentState);
    }

    // ── IsVulnerable convenience property ────────────────────────────────────

    [Fact]
    public void IsVulnerable_IsFalse_WhenActive()
    {
        var sm = new ShieldStateMachine();

        Assert.False(sm.IsVulnerable);
    }

    [Fact]
    public void IsVulnerable_IsTrue_WhenBroken()
    {
        var sm = new ShieldStateMachine { GraceDuration = 0.1f };
        sm.TryAbsorbHit();
        sm.Update(0.2);

        Assert.True(sm.IsVulnerable);
    }

    [Fact]
    public void IsVulnerable_IsFalse_WhenGracePeriod()
    {
        var sm = new ShieldStateMachine();
        sm.TryAbsorbHit(); // Active → GracePeriod

        Assert.False(sm.IsVulnerable);
    }
}
