// tests/Logic/ScoreSystemTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for ScoreSystem.
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered (matching ticket spec):
//   1. First kill: baseValue=100, multiplier=1 → Total=100, multiplier becomes 2
//   2. Second kill: baseValue=100, multiplier=2 → Total=300, multiplier becomes 3
//   3. Multiplier caps at MaxMultiplier (8)
//   4. OnHitTaken resets multiplier to 1; Total is unchanged
//   5. Reset() zeros both Total and Multiplier
//   6. AddKill with baseValue=0 still increments multiplier
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class ScoreSystemTests
{
    // ── 1. First kill ────────────────────────────────────────────────────────

    [Fact]
    public void FirstKill_AwardsBaseValue_WhenMultiplierIsOne()
    {
        var ss = new ScoreSystem();

        int awarded = ss.AddKill(100);

        Assert.Equal(100, awarded);
    }

    [Fact]
    public void FirstKill_TotalEqualsBaseValue()
    {
        var ss = new ScoreSystem();

        ss.AddKill(100);

        Assert.Equal(100, ss.Total);
    }

    [Fact]
    public void FirstKill_MultiplierBecomesTwo()
    {
        var ss = new ScoreSystem();

        ss.AddKill(100);

        Assert.Equal(2, ss.Multiplier);
    }

    // ── 2. Second kill ───────────────────────────────────────────────────────

    [Fact]
    public void SecondKill_AwardsTwiceBaseValue()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100);          // multiplier was 1 → 100 pts, now 2

        int awarded = ss.AddKill(100);  // multiplier is 2 → 200 pts

        Assert.Equal(200, awarded);
    }

    [Fact]
    public void SecondKill_TotalIsThreeHundred()
    {
        // 100×1 + 100×2 = 300
        var ss = new ScoreSystem();
        ss.AddKill(100);
        ss.AddKill(100);

        Assert.Equal(300, ss.Total);
    }

    [Fact]
    public void SecondKill_MultiplierBecomesThree()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100);
        ss.AddKill(100);

        Assert.Equal(3, ss.Multiplier);
    }

    // ── 3. Multiplier caps at MaxMultiplier ──────────────────────────────────

    [Fact]
    public void Multiplier_CapsAtMaxMultiplier()
    {
        var ss = new ScoreSystem();

        // Kill MaxMultiplier times — the final kill should leave multiplier AT the cap
        for (int i = 0; i < ScoreSystem.MaxMultiplier; i++)
            ss.AddKill(1);

        Assert.Equal(ScoreSystem.MaxMultiplier, ss.Multiplier);
    }

    [Fact]
    public void Multiplier_DoesNotExceedMaxMultiplierAfterMoreKills()
    {
        var ss = new ScoreSystem();

        // Kill well beyond the cap
        for (int i = 0; i < ScoreSystem.MaxMultiplier + 10; i++)
            ss.AddKill(1);

        Assert.Equal(ScoreSystem.MaxMultiplier, ss.Multiplier);
    }

    [Fact]
    public void MaxMultiplier_ConstantIsEight()
    {
        // Ensures the constant value matches the spec and isn't accidentally changed.
        Assert.Equal(8, ScoreSystem.MaxMultiplier);
    }

    // ── 4. OnHitTaken resets multiplier, preserves Total ─────────────────────

    [Fact]
    public void OnHitTaken_ResetsMultiplierToOne()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100); // multiplier → 2
        ss.AddKill(100); // multiplier → 3

        ss.OnHitTaken();

        Assert.Equal(1, ss.Multiplier);
    }

    [Fact]
    public void OnHitTaken_DoesNotChangeTotal()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100); // Total = 100
        ss.AddKill(100); // Total = 300

        ss.OnHitTaken();

        Assert.Equal(300, ss.Total);
    }

    [Fact]
    public void OnHitTaken_MultiplierBuildsAgainAfterReset()
    {
        var ss = new ScoreSystem();
        ss.AddKill(1);   // multiplier → 2
        ss.AddKill(1);   // multiplier → 3
        ss.OnHitTaken(); // multiplier → 1
        ss.AddKill(1);   // multiplier → 2

        Assert.Equal(2, ss.Multiplier);
    }

    // ── 5. Reset zeros both fields ───────────────────────────────────────────

    [Fact]
    public void Reset_ZerosTotal()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100);
        ss.AddKill(100);

        ss.Reset();

        Assert.Equal(0, ss.Total);
    }

    [Fact]
    public void Reset_ResetsMultiplierToOne()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100);
        ss.AddKill(100);

        ss.Reset();

        Assert.Equal(1, ss.Multiplier);
    }

    [Fact]
    public void Reset_AllowsAccumulationToRestartCorrectly()
    {
        var ss = new ScoreSystem();
        ss.AddKill(100);
        ss.Reset();

        int awarded = ss.AddKill(50); // multiplier should be 1 after reset

        Assert.Equal(50, awarded);
        Assert.Equal(50, ss.Total);
    }

    // ── 6. AddKill(0) still increments multiplier ────────────────────────────

    [Fact]
    public void AddKill_WithZeroBaseValue_ReturnsZeroAwarded()
    {
        var ss = new ScoreSystem();

        int awarded = ss.AddKill(0);

        Assert.Equal(0, awarded);
    }

    [Fact]
    public void AddKill_WithZeroBaseValue_StillIncrementsMultiplier()
    {
        var ss = new ScoreSystem();

        ss.AddKill(0);

        Assert.Equal(2, ss.Multiplier);
    }

    [Fact]
    public void AddKill_WithZeroBaseValue_TotalRemainsZero()
    {
        var ss = new ScoreSystem();

        ss.AddKill(0);

        Assert.Equal(0, ss.Total);
    }

    // ── Streak arithmetic sanity check ───────────────────────────────────────

    [Theory]
    [InlineData(1, 100, 100)]   // multiplier=1 → awarded=100
    [InlineData(2, 100, 200)]   // multiplier=2 → awarded=200
    [InlineData(4, 50,  200)]   // multiplier=4 → awarded=200
    [InlineData(8, 25,  200)]   // multiplier=8 (cap) → awarded=200
    public void AddKill_AwardedEqualsBaseValueTimesCurrentMultiplier(
        int startMultiplier, int baseValue, int expectedAwarded)
    {
        // Pump the multiplier up to the desired starting value without
        // using the system under test's own multiplier (use 0-value kills).
        var ss = new ScoreSystem();
        for (int i = 1; i < startMultiplier; i++)
            ss.AddKill(0); // each zero-value kill still increments multiplier

        int awarded = ss.AddKill(baseValue);

        Assert.Equal(expectedAwarded, awarded);
    }
}
