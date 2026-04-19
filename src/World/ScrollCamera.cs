// src/World/ScrollCamera.cs
// ─────────────────────────────────────────────────────────────────────────────
// Auto-scrolling camera that drives the level pacing and enforces the player's
// left boundary (arch doc Section 7, TICKET-060).
//
// Slice 1 — minimal stub:
//   Just calls MakeCurrent() so the camera is active.  IsStopped and
//   ScrollSpeed are already declared so LevelDirector can call them without
//   needing changes when the full implementation lands in Slice 4.
//
// Slice 4 will add:
//   • _Process: advances Position.X by ScrollSpeed each frame (unless IsStopped)
//   • Left-boundary push: prevents player from lagging behind the camera
//   • Right-boundary clamp: prevents player from exiting screen right
//   • Smooth deceleration to zero when IsStopped is set
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.World;

/// <summary>
/// Horizontal auto-scrolling camera.  Drives level pacing and enforces player
/// screen boundaries.  Full scroll implementation added in Slice 4.
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Make this camera the active viewport camera.
        // Because ScrollCamera is a sibling of Player in Level01.tscn (ADR-002),
        // it is NOT a child of Player — so it must explicitly call MakeCurrent().
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (!IsStopped)
            Position += new Vector2(ScrollSpeed * (float)delta, 0f);

        // Player left/right boundary clamping is handled by Player.cs, which reads
        // this camera's GlobalPosition each physics frame.  As the camera drifts
        // right, Player's leftEdge rises with it — no extra code needed here.
    }
}
