// src/Boss/Phase1Controller.cs
// ─────────────────────────────────────────────────────────────────────────────
// Drives Phase 1 attack patterns for The Demagogue (PRD F-413/414).
//
// Node placement (Boss.tscn):
//   Boss (Node2D)
//   └── PhaseControllers (Node2D)
//       └── Phase1Controller (Node)  ← this script
//
// Attack cycle (PRD F-413):
//   Open  (default 3 s) — weak point exposed; fires ConstitutionBlast bursts.
//   Closed (default 2 s) — invulnerable pause, no firing.
//
// Firing patterns:
//   Pattern A (HP > 30%) — 3 horizontal blasts per burst: one at hand level,
//                           one 150 px above, one 150 px below.  All travel
//                           leftward at ConstitutionBlast.DefaultVelocity.
//   Pattern B (HP ≤ 30%) — 1 slow blast aimed at the player's current position.
//
// SetActive(bool) wiring:
//   Boss.cs calls SetActive(true) when entering Phase 1, SetActive(false) when
//   Phase 1 HP is depleted.  The method is discovered via duck-typing
//   (HasMethod / Call) so no base class contract is required.
//
// Dependencies: Boss.cs (Boss.Instance), ProjectilePool.cs, ConstitutionBlast.cs,
//               EventBus.cs (tracks BossHpChanged to decide pattern)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Boss;

/// <summary>
/// Controls Phase 1 attack timing and projectile spawning.
/// Driven by <see cref="Boss"/> via the <c>SetActive</c> duck-typed interface.
/// </summary>
public partial class Phase1Controller : Node
{
    // ── Exported tuning ───────────────────────────────────────────────────────

    /// <summary>Seconds the hand stays Open (weak point visible, firing).</summary>
    [Export] public float OpenDuration   { get; set; } = 3f;

    /// <summary>Seconds the hand stays Closed (invulnerable, no fire).</summary>
    [Export] public float ClosedDuration { get; set; } = 2f;

    /// <summary>Seconds between ConstitutionBlast bursts while Open.</summary>
    [Export] public float FireInterval   { get; set; } = 2f;

    /// <summary>
    /// Speed of the Pattern B aimed blast (px/s).  Slower than Pattern A so the
    /// player has time to dodge a shot aimed directly at them.
    /// </summary>
    [Export] public float PatternBSpeed  { get; set; } = 100f;

    /// <summary>
    /// Vertical offset from the boss hand position for the upper and lower
    /// Pattern A rows.
    /// </summary>
    [Export] public float RowSpread      { get; set; } = 150f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool  _active  = false;

    /// <summary>
    /// HP ratio of the current phase, kept in sync via <see cref="EventBus.BossHpChanged"/>.
    /// Pattern B activates when this drops to 0.3 or below.
    /// </summary>
    private float _hpRatio = 1f;

    // ── Node references (resolved when SetActive(true) first called) ──────────

    private Node2D? _handNode;   // TinyHandNode in Boss.tscn — fire origin
    private Node2D? _player;     // Player.tscn root — target for Pattern B

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Stay dormant until Boss.cs enables us.
        _active = false;

        EventBus.Instance.BossHpChanged += OnBossHpChanged;
    }

    public override void _ExitTree()
    {
        EventBus.Instance.BossHpChanged -= OnBossHpChanged;
    }

    // ── Duck-typed activation interface ──────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Boss.SetControllerActive"/> when entering or
    /// leaving Phase 1.  Starts or stops the Open/Closed attack cycle.
    /// </summary>
    public void SetActive(bool active)
    {
        _active = active;

        if (active)
        {
            // Reset HP ratio — Boss.StartBoss already emitted BossHpChanged(20,20),
            // so _hpRatio should already be 1.0; this is just defensive.
            _hpRatio = 1f;

            // Resolve scene references once.
            _handNode = Boss.Instance?.GetNodeOrNull<Node2D>("TinyHandNode");
            _player   = GetNodeOrNull<Node2D>("/root/Level01/Entities/Player");

            RunCycle();
        }
        // If active == false the while (_active) guard in RunCycle exits naturally.
    }

    // ── EventBus handler ─────────────────────────────────────────────────────

    private void OnBossHpChanged(int current, int max)
    {
        _hpRatio = max > 0 ? (float)current / max : 0f;
    }

    // ── Attack cycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// Async loop: Open (fire on FireInterval) → Closed → repeat.
    /// Exits cleanly when <c>_active</c> is set to false or the node is freed.
    /// </summary>
    private async void RunCycle()
    {
        while (_active && IsInstanceValid(this))
        {
            // ── Open phase ────────────────────────────────────────────────────
            float openRemaining = OpenDuration;

            while (openRemaining > 0f && _active && IsInstanceValid(this))
            {
                FireBurst();

                // Wait FireInterval, but don't overshoot the open window.
                float wait = Mathf.Min(FireInterval, openRemaining);
                await ToSignal(
                    GetTree().CreateTimer(wait),
                    SceneTreeTimer.SignalName.Timeout);

                if (!IsInstanceValid(this)) return;

                openRemaining -= wait;
            }

            if (!_active || !IsInstanceValid(this)) return;

            // ── Closed phase ──────────────────────────────────────────────────
            await ToSignal(
                GetTree().CreateTimer(ClosedDuration),
                SceneTreeTimer.SignalName.Timeout);

            if (!IsInstanceValid(this)) return;
        }
    }

    // ── Firing helpers ────────────────────────────────────────────────────────

    private void FireBurst()
    {
        if (ProjectilePool.Instance is null) return;

        // Spawn origin: the TinyHandNode's world position.
        // Falls back to Boss root if the hand node isn't resolved.
        Vector2 origin = _handNode?.GlobalPosition
                         ?? Boss.Instance?.GlobalPosition
                         ?? Vector2.Zero;

        if (_hpRatio <= 0.3f)
            FirePatternB(origin);
        else
            FirePatternA(origin);
    }

    /// <summary>
    /// Pattern A: three horizontal blasts — one at hand level and one each
    /// <see cref="RowSpread"/> px above and below.
    /// </summary>
    private void FirePatternA(Vector2 origin)
    {
        FireHorizontal(origin + new Vector2(0f, -RowSpread));
        FireHorizontal(origin);
        FireHorizontal(origin + new Vector2(0f,  RowSpread));
    }

    /// <summary>
    /// Pattern B: one slow blast aimed directly at the player's current position.
    /// </summary>
    private void FirePatternB(Vector2 origin)
    {
        if (_player is null || !IsInstanceValid(_player)) return;

        var dir = (_player.GlobalPosition - origin).Normalized();
        if (dir == Vector2.Zero) dir = Vector2.Left;   // guard for zero-length

        ProjectilePool.Instance.Get(
            ProjectileType.ConstitutionBlast,
            origin,
            dir * PatternBSpeed);
    }

    private static void FireHorizontal(Vector2 origin)
    {
        ProjectilePool.Instance?.Get(
            ProjectileType.ConstitutionBlast,
            origin,
            ConstitutionBlast.DefaultVelocity);
    }
}
