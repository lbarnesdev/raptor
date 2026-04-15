// src/Logic/MissileSystem.cs
// ─────────────────────────────────────────────────────────────────────────────
// Ammo tracking and nearest-target selection for the player's missile weapon.
//
// ZERO Godot dependencies.  Uses System.Numerics.Vector2 so this class
// compiles under plain .NET 8 without the engine.
//
// In-engine wiring (MissileWeapon.cs):
//   var targets = _missileSystem.TryFire(
//       enemies.Select(e => e.GlobalPosition.ToNumerics()),   // Godot→Numerics conversion
//       Player.GlobalPosition.ToNumerics());
//
// N-002 compliance:
//   TryFire performs ZERO heap allocations in the hot path.
//   All working storage lives in pre-allocated List<T> fields.
//   The sort uses a cached static Comparison<T> delegate (no closure).
//
// ⚠ Init-property ordering note:
//   The constructor sets Ammo = DefaultAmmo at construction time.
//   If you customise DefaultAmmo via an object initialiser:
//       var ms = new MissileSystem { DefaultAmmo = 5 };
//   …the initialiser runs AFTER the constructor, so Ammo will still be 3.
//   Call ms.ResetToDefault() immediately after construction to sync them.
// ─────────────────────────────────────────────────────────────────────────────

using System.Numerics;

namespace Raptor.Logic;

/// <summary>
/// Manages missile ammo and selects up to 5 nearest targets on each firing.
/// </summary>
public sealed class MissileSystem
{
    // ── Public state ────────────────────────────────────────────────────────

    /// <summary>Missiles currently available to fire.</summary>
    public int Ammo { get; private set; }

    /// <summary>Hard cap on ammo. Default 6.</summary>
    public int MaxAmmo { get; init; } = 6;

    /// <summary>Ammo restored on respawn (via <see cref="ResetToDefault"/>). Default 3.</summary>
    public int DefaultAmmo { get; init; } = 3;

    // ── Pre-allocated working storage (N-002) ───────────────────────────────

    /// <summary>Reusable sort buffer — populated and sorted on every <see cref="TryFire"/> call.</summary>
    private readonly List<(float Dist, Vector2 Pos)> _sortBuffer = new(20);

    /// <summary>Reusable result list returned by <see cref="TryFire"/>.</summary>
    private readonly List<Vector2> _result = new(5);

    /// <summary>
    /// Cached comparison delegate so <see cref="List{T}.Sort(Comparison{T})"/>
    /// never allocates a closure.
    /// </summary>
    private static readonly Comparison<(float Dist, Vector2 Pos)> DistAscending =
        (a, b) => a.Dist.CompareTo(b.Dist);

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the system with <see cref="Ammo"/> initialised to
    /// <see cref="DefaultAmmo"/> (3 unless overridden — see class-level note).
    /// </summary>
    public MissileSystem()
    {
        Ammo = DefaultAmmo;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to fire missiles.  Selects up to 5 nearest visible targets,
    /// decrements ammo by 1, and returns the selected positions.
    /// </summary>
    /// <param name="visiblePositions">
    /// World positions of all currently visible enemies (from the Godot layer,
    /// converted to <see cref="System.Numerics.Vector2"/> before calling).
    /// </param>
    /// <param name="playerPosition">Player's current world position.</param>
    /// <returns>
    /// Up to 5 target positions sorted nearest-first, or an empty list if
    /// ammo is zero or no visible enemies exist.  The list is a reused
    /// internal buffer — do not hold a reference across frames.
    /// </returns>
    public IReadOnlyList<Vector2> TryFire(
        IEnumerable<Vector2> visiblePositions,
        Vector2              playerPosition)
    {
        // Always clear the result first so early returns hand back an empty list.
        _result.Clear();

        if (Ammo <= 0)
            return _result;

        // Populate sort buffer.
        _sortBuffer.Clear();
        foreach (Vector2 pos in visiblePositions)
        {
            float dist = Vector2.Distance(playerPosition, pos);
            _sortBuffer.Add((dist, pos));
        }

        if (_sortBuffer.Count == 0)
            return _result;

        // Sort ascending by distance — List.Sort with a cached delegate is O(n log n)
        // and allocation-free after the first call.
        _sortBuffer.Sort(DistAscending);

        // Take up to 5 nearest.
        int take = Math.Min(5, _sortBuffer.Count);
        for (int i = 0; i < take; i++)
            _result.Add(_sortBuffer[i].Pos);

        Ammo -= 1;
        return _result;
    }

    /// <summary>
    /// Add ammo, capped at <see cref="MaxAmmo"/>.
    /// </summary>
    /// <param name="count">Amount to add (default 1).</param>
    /// <returns>Actual amount added (may be less than <paramref name="count"/> if near cap).</returns>
    public int AddAmmo(int count = 1)
    {
        int added = Math.Min(count, MaxAmmo - Ammo);
        Ammo      = Math.Min(Ammo + count, MaxAmmo);
        return added;
    }

    /// <summary>
    /// Reset ammo to <see cref="DefaultAmmo"/>.
    /// Call on player respawn, and immediately after constructing with a
    /// custom <see cref="DefaultAmmo"/> value.
    /// </summary>
    public void ResetToDefault()
    {
        Ammo = DefaultAmmo;
    }
}
