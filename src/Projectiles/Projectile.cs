// src/Projectiles/Projectile.cs
// ─────────────────────────────────────────────────────────────────────────────
// Abstract base class for every projectile type in the game.
//
// Extends Area2D so physics overlap callbacks are used for hit detection
// rather than CharacterBody2D/move_and_collide — this is cheaper for fast
// objects that don't need response forces.
//
// Pool contract:
//   Every Projectile is owned by ProjectilePool.  Subclasses MUST call
//   ReturnToPool() when the projectile should despawn.  Two paths lead here:
//     1. Off-screen cull   — _Process() detects when GlobalPosition exits the
//                            screen bounds and calls ReturnToPool() directly.
//     2. On-hit despawn    — Subclass OnHit() handler calls ReturnToPool()
//                            after applying its hit effect.
//   ReturnToPool() is idempotent within a single frame via the _returned guard.
//
// Subclass minimal implementation:
//   public partial class PlasmaBolt : Projectile
//   {
//       public override void OnHit(Node body)
//       {
//           // apply damage to body...
//           ReturnToPool();
//       }
//   }
//
// Collision setup (must match in .tscn):
//   collision_layer = 5 (PlayerProjectile) or 6 (EnemyProjectile)
//   collision_mask  = 4 (enemies) | 7 (boss weak points) — for player projectiles
//                     3 (shield)  | 2 (player body)      — for enemy projectiles
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Abstract pooled projectile.  Moves at constant <see cref="Velocity"/> each
/// physics frame and culls itself when it exits the visible screen area.
/// </summary>
public abstract partial class Projectile : Area2D
{
    // ── Pool metadata ────────────────────────────────────────────────────────

    /// <summary>
    /// Identifies which pool queue this instance belongs to.
    /// Set once by <see cref="ProjectilePool._Ready"/> at instantiation time;
    /// never changed after that.
    /// </summary>
    public ProjectileType PoolType { get; internal set; }

    // ── Motion ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Pixels-per-second velocity applied each physics frame.
    /// Set by <see cref="ProjectilePool.Get"/> before the projectile is activated.
    /// </summary>
    public Vector2 Velocity { get; set; }

    // ── Off-screen cull ──────────────────────────────────────────────────────

    // Extra pixels beyond each viewport edge before culling fires.
    // Gives fast projectiles one frame to register a hit even when they are
    // slightly outside the visible area.
    private const float CullMargin = 64f;

    // Cached half-size of the viewport — constant for the lifetime of the game.
    private Vector2 _viewHalfSize;

    // Guard flag: prevents double-return if OnHit() and the off-screen check
    // both fire in the same frame (unlikely but theoretically possible).
    private bool _returned;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Connect the Area2D body_entered signal to the virtual OnHit handler.
        // Using a lambda here keeps the base class self-contained — subclasses
        // override OnHit() rather than re-connecting the signal themselves.
        BodyEntered += body => OnHit(body);

        // Cache viewport half-size once — it never changes at runtime.
        _viewHalfSize = GetViewportRect().Size / 2f;
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += Velocity * (float)delta;
    }

    public override void _Process(double delta)
    {
        // Off-screen cull — return to pool once we leave the padded camera bounds.
        // Bounds are computed relative to the *current* camera position so this
        // stays correct as ScrollCamera advances rightward (a fixed Rect2 would
        // cull bolts that are still on-screen after the camera has scrolled).
        var cam = GetViewport().GetCamera2D();
        if (cam is null) return;

        var c = cam.GlobalPosition;
        if (GlobalPosition.X < c.X - _viewHalfSize.X - CullMargin ||
            GlobalPosition.X > c.X + _viewHalfSize.X + CullMargin ||
            GlobalPosition.Y < c.Y - _viewHalfSize.Y - CullMargin ||
            GlobalPosition.Y > c.Y + _viewHalfSize.Y + CullMargin)
        {
            ReturnToPool();
        }
    }

    // ── Hit handling (override in subclasses) ────────────────────────────────

    /// <summary>
    /// Called when this projectile's Area2D overlaps a body on a masked layer.
    /// Subclasses apply damage here and call <see cref="ReturnToPool"/> when done.
    /// </summary>
    /// <param name="body">The colliding <see cref="Node"/>.</param>
    public virtual void OnHit(Node body) { }

    // ── Pool return ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns this projectile to <see cref="ProjectilePool"/>.
    /// Safe to call multiple times in one frame — only the first call acts.
    /// </summary>
    protected void ReturnToPool()
    {
        if (_returned) return;
        _returned = true;

        // Delegate all disable logic to the pool so the disable sequence stays
        // in one place (pool sets Visible, Monitoring, SetProcess, etc.).
        ProjectilePool.Instance.Return(this);
    }

    /// <summary>
    /// Called by <see cref="ProjectilePool.Get"/> just before handing this
    /// instance to the caller.  Resets per-activation state.
    /// </summary>
    internal void OnActivated()
    {
        _returned = false;
    }
}
