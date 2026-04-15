// src/Logic/ScoreSystem.cs
// ─────────────────────────────────────────────────────────────────────────────
// Score accumulation with a kill-streak multiplier.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// Rules:
//   • Each kill awards  baseValue × Multiplier  points.
//   • After every kill the Multiplier increments by 1, capped at MaxMultiplier.
//   • Taking any hit resets Multiplier to 1 (Total is unchanged).
//   • Reset() clears both Total and Multiplier.
// ─────────────────────────────────────────────────────────────────────────────

namespace Raptor.Logic;

/// <summary>
/// Tracks the player's score and kill-streak multiplier.
/// Caller is responsible for wiring <see cref="OnHitTaken"/> to damage events
/// and <see cref="AddKill"/> to enemy-death events.
/// </summary>
public sealed class ScoreSystem
{
    // ── Public state ────────────────────────────────────────────────────────

    /// <summary>Accumulated score across all kills since last <see cref="Reset"/>.</summary>
    public int Total { get; private set; }

    /// <summary>
    /// Current kill-streak multiplier (1–<see cref="MaxMultiplier"/>).
    /// Resets to 1 when the player takes a hit.
    /// </summary>
    public int Multiplier { get; private set; } = 1;

    /// <summary>Highest multiplier the streak can reach.</summary>
    public const int MaxMultiplier = 8;

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Record a kill and update score + multiplier.
    /// </summary>
    /// <param name="baseValue">Raw point value of the enemy (before multiplier).</param>
    /// <returns>Actual points awarded (<c>baseValue × Multiplier</c>).</returns>
    public int AddKill(int baseValue)
    {
        int awarded = baseValue * Multiplier;
        Total      += awarded;
        Multiplier  = Math.Min(Multiplier + 1, MaxMultiplier);
        return awarded;
    }

    /// <summary>
    /// Called whenever the player takes damage.
    /// Resets the streak multiplier to 1; Total is not affected.
    /// </summary>
    public void OnHitTaken()
    {
        Multiplier = 1;
    }

    /// <summary>Zeros both <see cref="Total"/> and <see cref="Multiplier"/>.</summary>
    public void Reset()
    {
        Total      = 0;
        Multiplier = 1;
    }
}
