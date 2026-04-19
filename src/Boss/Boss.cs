// src/Boss/Boss.cs
// ─────────────────────────────────────────────────────────────────────────────
// STUB — satisfies the WeakPoint.cs compiler reference only.
// Full implementation: TICKET-502.
//
// Replace the entire contents of this file when implementing TICKET-502.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Boss;

/// <summary>
/// Root node of the boss scene.  Owns <c>BossStateMachine</c> and
/// <c>BossPhaseHealth</c>; coordinates phase transitions and emits
/// <c>EventBus</c> boss signals.
/// <para>
/// This is a compile stub.  TICKET-502 replaces this file with the full
/// implementation.
/// </para>
/// </summary>
public partial class Boss : Node2D
{
    // ── Singleton ────────────────────────────────────────────────────────────

    /// <summary>
    /// The live boss instance.  Null when no boss is present in the scene.
    /// Set in <c>_Ready()</c> and cleared in <c>_ExitTree()</c>.
    /// <see cref="WeakPoint"/> uses null-conditional access (<c>Boss.Instance?.OnWeakPointHit</c>)
    /// so stray hits during despawn do not throw.
    /// </summary>
    public static Boss? Instance { get; private set; }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Public API (stubs — replaced in TICKET-502) ───────────────────────────

    /// <summary>
    /// Called by <see cref="WeakPoint.OnAreaEntered"/> when a player projectile
    /// hits the active weak point.  Full implementation in TICKET-502.
    /// </summary>
    /// <param name="phaseIndex">1-indexed phase the hit occurred in.</param>
    /// <param name="isMissile"><c>true</c> if the projectile was a <see cref="Raptor.Projectiles.Missile"/>.</param>
    public void OnWeakPointHit(int phaseIndex, bool isMissile)
    {
        GD.Print($"[Boss stub] WeakPoint hit — phase {phaseIndex}, missile={isMissile}");
    }

    /// <summary>
    /// Called by <see cref="Raptor.World.LevelDirector"/> when the
    /// <c>SpawnBoss</c> timeline event fires.  Full implementation in TICKET-502.
    /// </summary>
    public void StartBoss()
    {
        GD.Print("[Boss stub] StartBoss() called — implement in TICKET-502.");
    }
}
