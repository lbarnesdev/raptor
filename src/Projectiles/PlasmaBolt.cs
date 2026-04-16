// src/Projectiles/PlasmaBolt.cs
// ─────────────────────────────────────────────────────────────────────────────
// Player primary projectile — a fast plasma bolt fired by PlasmaWeapon.
//
// Extends the Projectile base class (TICKET-040 / src/Projectiles/Projectile.cs).
// All movement, off-screen culling, pool acquisition, and BodyEntered wiring
// are handled by Projectile._Process() and Projectile._Ready().  PlasmaBolt
// only needs to override OnHit() to apply damage.
//
// Node placement (PlasmaBolt.tscn):
//   PlasmaBolt (Area2D, extends Projectile)
//   ├── CollisionShape2D  (CapsuleShape2D, elongated on X axis)
//   └── Sprite2D          (cyan capsule sprite, elongated on X axis)
//
// Collision setup (set in PlasmaBolt.tscn Inspector, NOT in code):
//   collision_layer = 5  (PlayerProjectile)
//   collision_mask  = 72  (layers 4 + 7 = Enemy + EnemyShield)
//   Bitmask reference:
//     Layer 4 → 2^3 = 8   (Enemy)
//     Layer 7 → 2^6 = 64  (EnemyShield)
//     Total   = 72
//
// Damage application — duck-typing with HasMethod/Call:
//   BaseEnemy and Boss both expose TakeDamage(int amount).  We use Godot's
//   duck-typing (HasMethod + Call) instead of an IDamageable C# interface so
//   the code compiles before those classes exist.
//
//   TODO (BaseEnemy ticket): once IDamageable is defined, replace the
//   HasMethod / Call pattern with a direct cast:
//     if (body is IDamageable damageable) damageable.TakeDamage(1);
//
// Lifetime:
//   After OnHit fires, ReturnToPool() is called unconditionally.  The bolt
//   disappears on first contact; multi-hit (piercing) can be added later by
//   counting hits before returning to the pool.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Fast, single-hit plasma bolt fired by <see cref="Raptor.Player.PlasmaWeapon"/>.
/// Extends <see cref="Projectile"/>; overrides <see cref="OnHit"/> to apply
/// 1 point of damage to the struck body (if it exposes <c>TakeDamage</c>)
/// and return itself to the pool.
/// </summary>
public partial class PlasmaBolt : Projectile
{
    // ── Damage constant ───────────────────────────────────────────────────────

    /// <summary>
    /// Damage dealt per impact.  Exposed as a constant so design can tune it
    /// here before a full stat-sheet system exists.
    /// </summary>
    private const int DamagePerHit = 1;

    // ── Projectile override ───────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Projectile"/> when this bolt's <c>Area2D</c>
    /// detects a body entering its collision shape.
    /// </summary>
    /// <param name="body">
    /// The <see cref="Node2D"/> that entered the collision area.
    /// Expected to be an enemy or enemy shield; other bodies are ignored.
    /// </param>
    public override void OnHit(Node body)
    {
        // Apply damage via duck-typing so this file compiles before
        // IDamageable / BaseEnemy exists (see header TODO).
        //
        // HasMethod searches the GDScript/C# method table.  Because all enemy
        // classes will be pure C# Node subclasses, HasMethod correctly finds
        // public methods — no need for [Export] on TakeDamage.
        if (body.HasMethod("TakeDamage"))
            body.Call("TakeDamage", DamagePerHit);

        // Return to the pool unconditionally — the bolt vanishes on first hit
        // regardless of whether the target had a TakeDamage method.
        // ReturnToPool() is idempotent; calling it from both here and the
        // off-screen cull path (in Projectile._Process) is safe.
        ReturnToPool();
    }
}
