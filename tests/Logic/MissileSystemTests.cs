// tests/Logic/MissileSystemTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for MissileSystem.
// Uses System.Numerics.Vector2 — no Godot dependencies.
// Runs with plain `dotnet test`.
//
// Scenarios covered (matching ticket spec):
//   1. TryFire with 0 ammo → empty list, ammo stays 0
//   2. TryFire with empty positions → empty list (ammo unchanged)
//   3. TryFire with 3 targets → all 3 returned, ammo decrements
//   4. TryFire with 7 targets → nearest 5 returned (verified by distance)
//   5. AddAmmo caps at MaxAmmo, returns actual added amount
//   6. AddAmmo when full returns 0
//   7. ResetToDefault sets ammo to DefaultAmmo regardless of current value
// ─────────────────────────────────────────────────────────────────────────────

using System.Numerics;
using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class MissileSystemTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly Vector2 Origin = Vector2.Zero;

    /// <summary>
    /// Creates a position exactly <paramref name="dist"/> units to the right of Origin.
    /// </summary>
    private static Vector2 At(float dist) => new(dist, 0f);

    /// <summary>
    /// Default MissileSystem: DefaultAmmo=3, MaxAmmo=6.
    /// Constructor sets Ammo = DefaultAmmo = 3.
    /// </summary>
    private static MissileSystem Default() => new();

    /// <summary>
    /// MissileSystem with custom MaxAmmo.
    /// Ammo is still initialised to DefaultAmmo (3) in the constructor.
    /// </summary>
    private static MissileSystem WithMaxAmmo(int maxAmmo) =>
        new() { MaxAmmo = maxAmmo };

    // ── Construction sanity ──────────────────────────────────────────────────

    [Fact]
    public void NewInstance_AmmoEqualsDefaultAmmo()
    {
        var ms = Default();
        Assert.Equal(ms.DefaultAmmo, ms.Ammo);
    }

    [Fact]
    public void NewInstance_DefaultAmmoIsThree()
    {
        var ms = Default();
        Assert.Equal(3, ms.Ammo);
    }

    // ── 1. TryFire with 0 ammo → empty, ammo unchanged ───────────────────────

    [Fact]
    public void TryFire_WithZeroAmmo_ReturnsEmpty()
    {
        var ms = Default();
        ms.ResetToDefault(); // Ammo = 3
        // Drain ammo completely
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin); // Ammo now 0

        var result = ms.TryFire(new[] { At(1f) }, Origin);

        Assert.Empty(result);
    }

    [Fact]
    public void TryFire_WithZeroAmmo_AmmoStaysZero()
    {
        var ms = Default();
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin); // Ammo = 0

        ms.TryFire(new[] { At(1f) }, Origin); // should be no-op

        Assert.Equal(0, ms.Ammo);
    }

    // ── 2. TryFire with empty positions → empty, ammo unchanged ──────────────

    [Fact]
    public void TryFire_WithEmptyPositions_ReturnsEmpty()
    {
        var ms = Default();

        var result = ms.TryFire(Array.Empty<Vector2>(), Origin);

        Assert.Empty(result);
    }

    [Fact]
    public void TryFire_WithEmptyPositions_AmmoUnchanged()
    {
        var ms = Default();
        int before = ms.Ammo;

        ms.TryFire(Array.Empty<Vector2>(), Origin);

        Assert.Equal(before, ms.Ammo);
    }

    // ── 3. TryFire with 3 targets → all 3 returned, ammo decrements ──────────

    [Fact]
    public void TryFire_WithThreeTargets_ReturnsThreePositions()
    {
        var ms = Default();
        var targets = new[] { At(1f), At(2f), At(3f) };

        var result = ms.TryFire(targets, Origin);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void TryFire_WithThreeTargets_DecrementsAmmoByOne()
    {
        var ms = Default();
        int before = ms.Ammo;

        ms.TryFire(new[] { At(1f), At(2f), At(3f) }, Origin);

        Assert.Equal(before - 1, ms.Ammo);
    }

    [Fact]
    public void TryFire_WithThreeTargets_ContainsAllInputPositions()
    {
        var ms    = Default();
        var p1    = At(1f);
        var p2    = At(2f);
        var p3    = At(3f);

        var result = ms.TryFire(new[] { p1, p2, p3 }, Origin);

        Assert.Contains(p1, result);
        Assert.Contains(p2, result);
        Assert.Contains(p3, result);
    }

    // ── 4. TryFire with 7 targets → nearest 5 returned ───────────────────────

    [Fact]
    public void TryFire_WithSevenTargets_ReturnsExactlyFive()
    {
        var ms = Default();
        // 7 targets at distances 1–7 from Origin
        var targets = Enumerable.Range(1, 7).Select(i => At(i)).ToArray();

        var result = ms.TryFire(targets, Origin);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void TryFire_WithSevenTargets_ReturnsNearestFive()
    {
        // Positions at distances 1, 2, 3, 4, 5 should be selected;
        // positions at distances 6 and 7 should be excluded.
        var ms = Default();
        var near = Enumerable.Range(1, 5).Select(i => At(i)).ToArray();
        var far  = new[] { At(6f), At(7f) };
        var all  = near.Concat(far).ToArray();

        var result = ms.TryFire(all, Origin);

        foreach (var pos in near)
            Assert.Contains(pos, result);
        foreach (var pos in far)
            Assert.DoesNotContain(pos, result);
    }

    [Fact]
    public void TryFire_WithSevenTargets_SortedNearestFirst()
    {
        // The first result entry should be the closest target.
        var ms = Default();
        // Provide in reverse order to verify the sort actually runs.
        var targets = new[] { At(5f), At(3f), At(1f), At(4f), At(2f), At(6f), At(7f) };

        var result = ms.TryFire(targets, Origin);

        // Result[0] should be the position at distance 1 from Origin.
        Assert.Equal(At(1f), result[0]);
    }

    [Fact]
    public void TryFire_UsesDistanceFromPlayerPosition_NotOrigin()
    {
        // Player is at (100, 0). Enemy at (101, 0) is 1 unit away;
        // enemy at (1, 0) is 99 units away.
        var ms         = Default();
        var player     = new Vector2(100f, 0f);
        var nearPlayer = new Vector2(101f, 0f); // dist from player = 1
        var farPlayer  = new Vector2(1f,   0f); // dist from player = 99

        var result = ms.TryFire(new[] { nearPlayer, farPlayer }, player);

        // First result (nearest to player) should be nearPlayer.
        Assert.Equal(nearPlayer, result[0]);
    }

    // ── 5. AddAmmo caps at MaxAmmo, returns actual added ─────────────────────

    [Fact]
    public void AddAmmo_BelowCap_ReturnsAmountAdded()
    {
        // Ammo=3, MaxAmmo=6: adding 2 → added=2, Ammo=5
        var ms = Default();

        int added = ms.AddAmmo(2);

        Assert.Equal(2, added);
    }

    [Fact]
    public void AddAmmo_BelowCap_IncreasesAmmo()
    {
        var ms = Default(); // Ammo = 3

        ms.AddAmmo(2);

        Assert.Equal(5, ms.Ammo);
    }

    [Fact]
    public void AddAmmo_WouldExceedCap_ClampedToMaxAmmo()
    {
        // Ammo=3, MaxAmmo=6: adding 10 → Ammo=6 (not 13)
        var ms = Default();

        ms.AddAmmo(10);

        Assert.Equal(ms.MaxAmmo, ms.Ammo);
    }

    [Fact]
    public void AddAmmo_WouldExceedCap_ReturnsActualAddedNotRequested()
    {
        // Ammo=3, MaxAmmo=6: only 3 slots free; adding 10 should return 3.
        var ms = Default();

        int added = ms.AddAmmo(10);

        Assert.Equal(3, added);
    }

    [Fact]
    public void AddAmmo_ExactlyFillsCap_ReturnsCorrectAmount()
    {
        // Ammo=3, MaxAmmo=6: adding exactly 3 → added=3, Ammo=6
        var ms = Default();

        int added = ms.AddAmmo(3);

        Assert.Equal(3, added);
        Assert.Equal(6, ms.Ammo);
    }

    // ── 6. AddAmmo when full returns 0 ───────────────────────────────────────

    [Fact]
    public void AddAmmo_WhenAtMaxAmmo_ReturnsZero()
    {
        var ms = Default();
        ms.AddAmmo(100); // fill to cap

        int added = ms.AddAmmo(1);

        Assert.Equal(0, added);
    }

    [Fact]
    public void AddAmmo_WhenAtMaxAmmo_AmmoUnchanged()
    {
        var ms = Default();
        ms.AddAmmo(100); // Ammo = MaxAmmo = 6

        ms.AddAmmo(5);

        Assert.Equal(ms.MaxAmmo, ms.Ammo);
    }

    // ── 7. ResetToDefault sets ammo to DefaultAmmo ────────────────────────────

    [Fact]
    public void ResetToDefault_FromFullAmmo_SetsToDefaultAmmo()
    {
        var ms = Default(); // DefaultAmmo = 3
        ms.AddAmmo(100);    // Ammo = MaxAmmo = 6

        ms.ResetToDefault();

        Assert.Equal(ms.DefaultAmmo, ms.Ammo);
        Assert.Equal(3, ms.Ammo);
    }

    [Fact]
    public void ResetToDefault_FromZeroAmmo_SetsToDefaultAmmo()
    {
        var ms = Default();
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin);
        ms.TryFire(new[] { At(1f) }, Origin); // Ammo = 0

        ms.ResetToDefault();

        Assert.Equal(ms.DefaultAmmo, ms.Ammo);
    }

    [Fact]
    public void ResetToDefault_WithCustomDefaultAmmo_SetsToCustomValue()
    {
        // Construct with a custom DefaultAmmo.
        // Because the constructor runs before the object initialiser, we call
        // ResetToDefault() explicitly to sync Ammo — same as game code at spawn.
        var ms = new MissileSystem { DefaultAmmo = 5 };
        ms.ResetToDefault(); // Ammo = 5

        ms.AddAmmo(100);      // fill to MaxAmmo
        ms.ResetToDefault();  // back to 5

        Assert.Equal(5, ms.Ammo);
    }

    // ── Ammo-drain sequence ───────────────────────────────────────────────────

    [Fact]
    public void TryFire_ConsecutiveCalls_DrainAmmoCorrectly()
    {
        var ms = Default(); // Ammo = 3
        var pos = new[] { At(1f) };

        ms.TryFire(pos, Origin); // 2
        ms.TryFire(pos, Origin); // 1
        ms.TryFire(pos, Origin); // 0

        Assert.Equal(0, ms.Ammo);
        Assert.Empty(ms.TryFire(pos, Origin)); // 4th fire: no ammo
    }

    // ── MaxTargets boundary ───────────────────────────────────────────────────

    [Fact]
    public void TryFire_WithExactlyFiveTargets_ReturnsAllFive()
    {
        var ms      = Default();
        var targets = Enumerable.Range(1, 5).Select(i => At(i)).ToArray();

        var result = ms.TryFire(targets, Origin);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void TryFire_WithOneTarget_ReturnsOne()
    {
        var ms = Default();

        var result = ms.TryFire(new[] { At(5f) }, Origin);

        Assert.Single(result);
        Assert.Equal(At(5f), result[0]);
    }
}
