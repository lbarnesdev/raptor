// src/Boss/AlienPassenger.cs
// ─────────────────────────────────────────────────────────────────────────────
// The alien creature driving The Demagogue.  Always visible on the boss body.
// After Phase 3 dies, Boss.cs calls Detach() to begin the flee sequence.
//
// Node placement (Boss.tscn):
//   Boss (Node2D)
//   └── AlienPassenger (Node2D)  ← this script
//       ├── AlienSprite (Polygon2D)     ← idle placeholder; AnimatedSprite2D Slice 11
//       └── HitArea (Area2D)            ← layer 9 (AlienFlee=256), mask 16 (PlayerProjectile)
//           └── CollisionShape2D        ← monitoring=false until Detach() enables it
//
// Flee sequence (PRD F-441–446):
//   1. Detach() reparents self to Level01 root (survives Boss.QueueFree).
//   2. _PhysicsProcess moves right at FleeSped px/s.
//   3. A 5 s SceneTreeTimer fires bad ending (LevelComplete false) on timeout.
//   4. If a player projectile enters HitArea before the timer fires:
//        → good ending (LevelComplete true), timer cancelled, QueueFree.
//
// Note: HitArea uses AreaEntered (not BodyEntered) because player projectiles
// extend Projectile which extends Area2D — BodyEntered only fires for
// PhysicsBody2D.  The collision mask still correctly limits detection to
// collision_layer 16 (PlayerProjectile).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Boss;

/// <summary>
/// After <see cref="Detach"/> is called by <see cref="Boss.OnBossDefeated"/>,
/// the alien reparents itself to the level root, moves rightward at
/// <see cref="FleeSped"/> px/s for up to 5 seconds, and emits
/// <c>LevelComplete(true)</c> if shot or <c>LevelComplete(false)</c> on escape.
/// </summary>
public partial class AlienPassenger : Node2D
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Rightward flee speed in pixels per second (PRD F-442).</summary>
    private const float FleeSped = 240f;

    /// <summary>Seconds before the alien escapes and triggers the bad ending.</summary>
    private const float FleeTimeout = 5f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool   _fleeing       = false;

    /// <summary>
    /// Set to <c>true</c> when the player lands a hit, so the
    /// <see cref="SceneTreeTimer"/> timeout handler is a no-op.
    /// </summary>
    private bool   _fleeCancelled = false;

    // ── Cached node references ────────────────────────────────────────────────

    private Area2D? _hitArea;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _hitArea = GetNodeOrNull<Area2D>("HitArea");

        if (_hitArea is not null)
        {
            // Stays disabled until Detach() enables it.
            _hitArea.Monitoring = false;
            _hitArea.AreaEntered += OnHitAreaAreaEntered;
        }
    }

    public override void _ExitTree()
    {
        if (_hitArea is not null)
            _hitArea.AreaEntered -= OnHitAreaAreaEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_fleeing) return;

        GlobalPosition += new Vector2(FleeSped * (float)delta, 0f);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Boss.OnBossDefeated"/> when Phase 3 HP reaches 0.
    /// Reparents to the level root, begins rightward movement, enables the hit
    /// area, and starts the 5-second escape timer.
    /// </summary>
    public void Detach()
    {
        _fleeing       = true;
        _fleeCancelled = false;

        // Reparent to Level01 so the alien persists when Boss.QueueFree fires.
        // keepGlobalTransform=true keeps our world position unchanged.
        Reparent(GetNode<Node>("/root/Level01"), keepGlobalTransform: true);

        // Enable the hit area — player projectiles can now register kills.
        if (_hitArea is not null)
            _hitArea.Monitoring = true;

        // Enable per-frame movement now that we're detached.
        SetPhysicsProcess(true);

        // Bad-ending timer: if the alien reaches the edge before being shot,
        // the player gets the bad ending.
        var timer = GetTree().CreateTimer(FleeTimeout);
        timer.Timeout += OnFleeTimerTimeout;

        GD.Print("AlienPassenger: flee sequence started.");
    }

    // ── Signal handlers ───────────────────────────────────────────────────────

    private void OnFleeTimerTimeout()
    {
        // Guard: good ending already triggered — do nothing.
        if (_fleeCancelled || !IsInstanceValid(this)) return;

        GD.Print("AlienPassenger: escaped — bad ending.");
        EventBus.Instance.EmitSignal(EventBus.SignalName.LevelComplete, false);
        QueueFree();
    }

    private void OnHitAreaAreaEntered(Area2D area)
    {
        // Only accept hits from player projectiles.
        if (area is not Projectile projectile) return;

        // Cancel the escape timer so bad-ending doesn't also fire.
        _fleeCancelled = true;

        // Return the projectile to its pool through the guarded path.
        projectile.ReturnToPool();

        AudioManager.Instance.PlaySfx(AudioManager.Sfx.AlienDeath);

        GD.Print("AlienPassenger: shot — good ending.");
        EventBus.Instance.EmitSignal(EventBus.SignalName.LevelComplete, true);

        QueueFree();
    }
}
