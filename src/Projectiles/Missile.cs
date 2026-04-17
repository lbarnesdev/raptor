// src/Projectiles/Missile.cs
// ─────────────────────────────────────────────────────────────────────────────
// Homing missile — player special weapon projectile.
//
// Extends the Projectile Area2D base (TICKET-040).  All pool bookkeeping,
// off-screen culling, and BodyEntered wiring are handled by the base class.
// Missile only overrides _PhysicsProcess (to apply homing) and OnHit (damage).
//
// Homing model:
//   Each physics frame, the missile steers its Velocity vector toward
//   TargetPosition using Vector2.MoveToward.  The steering rate is bounded by
//   HomingStrength (px/s²), so the missile cannot instantly snap direction —
//   it traces a curved arc.  At HomingStrength=300 and InitialSpeed=220 the arc
//   radius is roughly 3–4 screen tiles, which looks natural.
//
//   TargetPosition is a snapshot of where the enemy was when the missile fired.
//   It does NOT auto-track the enemy's movement.  This keeps behaviour predictable
//   and avoids the need for a live enemy reference (which could become null if the
//   enemy dies in flight).  Full tracking can be added later by storing a WeakRef
//   to the enemy and updating TargetPosition each frame if it is still alive.
//
// Despawn paths (inherited from Projectile):
//   1. OnHit() fires when the Area2D overlaps an enemy or boss weak point.
//   2. Off-screen cull fires in Projectile._Process() when GlobalPosition leaves
//      the padded viewport bounds.
//   Both paths call ReturnToPool() which is idempotent within a frame.
//
// Node placement (Missile.tscn):
//   Missile (Area2D, extends Projectile)
//   ├── CollisionShape2D  (CapsuleShape2D, elongated on X)
//   └── Sprite2D          (missile_body.png + missile_trail.png, via animated frames)
//
// Collision setup (set in Missile.tscn Inspector, NOT in code):
//   collision_layer = 5  (PlayerProjectile)
//   collision_mask  = 72  (layers 4 + 7 = Enemy + BossWeakPoint)
//   Bitmask reference:
//     Layer 4 → 2^3 =  8  (Enemy)
//     Layer 7 → 2^6 = 64  (BossWeakPoint)
//     Total   = 72
//
// TICKET note:
//   The missile homing behaviour specified in TICKET-035 is included here
//   (together with TICKET-033 / MissileWeapon.cs) because MissileWeapon needs
//   Missile.InitialSpeed and Missile.TargetPosition to compile.  The sprite-tilt
//   addition from TICKET-035 (Player._Process) remains in that ticket.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Homing missile fired by <see cref="Raptor.Player.MissileWeapon"/>.
/// Steers toward <see cref="TargetPosition"/> each physics frame and deals
/// <see cref="DamagePerHit"/> damage on contact.
/// </summary>
public partial class Missile : Projectile
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Launch speed in pixels per second.  MissileWeapon passes
    /// <c>Vector2.Right * InitialSpeed</c> to <see cref="ProjectilePool.Get"/>
    /// so the missile starts moving forward immediately.
    /// The homing system then bends this velocity toward the target each frame.
    /// </summary>
    public const float InitialSpeed = 220f;

    /// <summary>Damage dealt on impact.  Missiles deal 3× a plasma bolt (1 pt).</summary>
    private const int DamagePerHit = 3;

    // ── Homing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// World-space position this missile homes toward.
    /// Set by <see cref="Raptor.Player.MissileWeapon"/> immediately after
    /// <see cref="ProjectilePool.Get"/> returns the instance.
    /// </summary>
    /// <remarks>
    /// This is a snapshot of the enemy's position at fire time, not a live
    /// reference — see file header for rationale and upgrade path.
    /// </remarks>
    public Vector2 TargetPosition { get; set; }

    /// <summary>
    /// Maximum steering rate in px/s per second applied to
    /// <see cref="Projectile.Velocity"/> each physics frame.
    /// Higher = tighter homing arc.  Default 300 px/s².
    /// Exposed as <c>[Export]</c> for per-scene tuning in the Inspector.
    /// </summary>
    [Export] public float HomingStrength { get; set; } = 300f;

    // ── Projectile overrides ──────────────────────────────────────────────────

    /// <summary>
    /// Steers the missile toward <see cref="TargetPosition"/> and advances
    /// its position.  Overrides the base class which would apply straight-line
    /// movement at the current <see cref="Projectile.Velocity"/>.
    /// </summary>
    /// <remarks>
    /// We do NOT call <c>base._PhysicsProcess(delta)</c> — the base method
    /// also does <c>Position += Velocity * delta</c>, which would double the
    /// movement if called alongside our own position update.
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        // ── Homing ───────────────────────────────────────────────────────────
        // Maintain current speed while gradually rotating toward target.
        // MoveToward changes the vector by at most (HomingStrength * delta) px/s
        // per frame, bounding the turn rate so the arc looks physical.

        // Preserve the missile's current speed (not InitialSpeed) so any
        // external velocity adjustments survive across frames.
        float currentSpeed = Velocity.Length();
        if (currentSpeed < 1f)
            currentSpeed = InitialSpeed; // recover from near-zero edge case

        Vector2 toTarget       = (TargetPosition - GlobalPosition).Normalized();
        Vector2 desiredVelocity = toTarget * currentSpeed;

        Velocity = Velocity.MoveToward(desiredVelocity, HomingStrength * (float)delta);

        // ── Position update ──────────────────────────────────────────────────
        Position += Velocity * (float)delta;
    }

    /// <summary>
    /// Called by the base <see cref="Projectile"/> when this missile's
    /// <c>Area2D</c> detects a body entering its collision shape.
    /// Applies damage (if the body has <c>TakeDamage</c>) and returns to pool.
    /// </summary>
    /// <param name="body">
    /// The colliding <see cref="Node2D"/> — expected to be an enemy or a boss
    /// weak point.  Calls <c>TakeDamage</c> via duck-typing so this file
    /// compiles before <c>BaseEnemy</c> and <c>WeakPoint</c> are written.
    /// </param>
    public override void OnHit(Node body)
    {
        // Duck-typed damage application (see PlasmaBolt.cs header for rationale).
        // TODO (BaseEnemy / WeakPoint tickets): replace with IDamageable cast.
        if (body.HasMethod("TakeDamage"))
            body.Call("TakeDamage", DamagePerHit);

        // Return unconditionally — missile is single-hit.
        // ReturnToPool() is idempotent; double-calling from the off-screen cull
        // path in Projectile._Process() is safe.
        ReturnToPool();
    }
}
