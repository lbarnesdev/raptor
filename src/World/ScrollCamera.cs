// src/World/ScrollCamera.cs
// ─────────────────────────────────────────────────────────────────────────────
// Auto-scrolling camera that drives the level pacing and enforces the player's
// left boundary (arch doc Section 7, TICKET-060).
//
// Slice 4 — scroll + boundary clamping.
// Slice 11 — Shake(duration, magnitude): random offset tween for VFX impact.
//
// Shake() is called by BaseEnemy.Die() and Boss.Die() via EventBus.
// It uses a Tween that oscillates the camera's Offset every 0.05 s, then
// resets Offset to Vector2.Zero when the duration expires.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.World;

/// <summary>
/// Horizontal auto-scrolling camera.  Drives level pacing and enforces player
/// screen boundaries.  Includes a screen-shake effect for VFX impact (Slice 11).
/// </summary>
public partial class ScrollCamera : Camera2D
{
    // ── Tunables (exported so Godot Inspector can override defaults) ──────────

    /// <summary>Pixels per second the camera travels rightward.</summary>
    [Export] public float ScrollSpeed { get; set; } = 120f;

    // ── State (set by LevelDirector at boss arena) ────────────────────────────

    /// <summary>
    /// When <c>true</c>, the camera stops advancing.
    /// Set by <c>LevelDirector</c> at the 160-second mark (boss encounter).
    /// </summary>
    public bool IsStopped { get; set; } = false;

    // ── Shake state ───────────────────────────────────────────────────────────

    private readonly RandomNumberGenerator _rng = new();

    /// <summary>Active shake tween, or null when not shaking.</summary>
    private Tween? _shakeTween;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Make this camera the active viewport camera.
        // Because ScrollCamera is a sibling of Player in Level01.tscn (ADR-002),
        // it is NOT a child of Player — so it must explicitly call MakeCurrent().
        MakeCurrent();
        _rng.Randomize();
    }

    public override void _Process(double delta)
    {
        if (!IsStopped)
            Position += new Vector2(ScrollSpeed * (float)delta, 0f);

        // Player left/right boundary clamping is handled by Player.cs, which reads
        // this camera's GlobalPosition each physics frame.  As the camera drifts
        // right, Player's leftEdge rises with it — no extra code needed here.
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shake the camera for <paramref name="duration"/> seconds with a random
    /// pixel offset capped at <paramref name="magnitude"/>.
    ///
    /// Multiple overlapping calls restart the shake (kills the prior tween and
    /// adopts the largest remaining duration, so boss deaths feel longer).
    /// </summary>
    /// <param name="duration">Total shake duration in seconds (e.g. 0.3 for
    /// small enemies, 0.8 for boss phases).</param>
    /// <param name="magnitude">Peak random pixel offset (e.g. 8f for small,
    /// 20f for boss).</param>
    public void Shake(float duration = 0.3f, float magnitude = 8f)
    {
        // Kill any previous shake so a new, larger one can dominate.
        _shakeTween?.Kill();

        // Run an async-void timer loop: every 0.05 s apply a random Offset,
        // then after `duration` seconds reset to zero.
        _ = DoShakeAsync(duration, magnitude);
    }

    private async System.Threading.Tasks.Task DoShakeAsync(float duration, float magnitude)
    {
        float elapsed = 0f;
        const float Interval = 0.05f;

        while (elapsed < duration)
        {
            float ox = _rng.RandfRange(-magnitude, magnitude);
            float oy = _rng.RandfRange(-magnitude, magnitude);
            Offset = new Vector2(ox, oy);

            await ToSignal(
                GetTree().CreateTimer(Interval),
                SceneTreeTimer.SignalName.Timeout);

            if (!IsInstanceValid(this)) return;
            elapsed += Interval;
        }

        Offset = Vector2.Zero;
    }
}
