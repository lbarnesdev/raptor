// src/Projectiles/ConstitutionBlast.cs
// ─────────────────────────────────────────────────────────────────────────────
// Boss Phase 1 projectile — a large, slow gold energy blast.
//
// Fired by Phase1Controller in two patterns (PRD F-413/414):
//   Pattern A (default): horizontal rows at a fixed velocity of (-160, 0) px/s.
//   Pattern B (≤30% HP): aimed at the player's position; velocity set by
//                         Phase1Controller before calling ProjectilePool.Get().
//
// Node placement (ConstitutionBlast.tscn):
//   ConstitutionBlast (Area2D, extends Projectile)
//   ├── CollisionShape2D  (CircleShape2D r=18)
//   └── PlaceholderShape  (Polygon2D octagon, gold ~28 px radius)
//
// Collision setup (set in .tscn, not here):
//   collision_layer = 32   (EnemyProjectile = layer 6 = 2^5)
//   collision_mask  =  6   (Player=2 + PlayerShield=4)
//
// Damage pipeline:
//   OnHit uses duck-typing ("TakeHit") matching the pattern used in PlasmaBolt.
//   The full player-damage chain (shield absorption, life decrement) is wired in
//   Slice 7 (TICKET-701/702).  Until then, the projectile still disappears on
//   contact so feedback is correct; only the damage value is silently dropped.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Slow, large energy blast fired by <see cref="Raptor.Boss.Phase1Controller"/>.
/// Despawns on contact with the player body or player shield area.
/// </summary>
public partial class ConstitutionBlast : Projectile
{
    // ── Motion defaults ───────────────────────────────────────────────────────

    /// <summary>
    /// Default leftward velocity for Pattern A (horizontal row fire).
    /// Phase1Controller overrides this for Pattern B (aimed shots) by setting
    /// <see cref="Projectile.Velocity"/> after calling <c>ProjectilePool.Get()</c>.
    /// </summary>
    public static readonly Vector2 DefaultVelocity = new(-160f, 0f);

    // ── Projectile override ───────────────────────────────────────────────────

    /// <summary>
    /// Called when this blast's <c>Area2D</c> detects a body entering its
    /// collision shape.  The expected body is the player's <c>CharacterBody2D</c>
    /// (layer 2); the player's <c>ShieldArea</c> is handled separately in
    /// Slice 7 via <c>AreaEntered</c> on the <c>ShieldController</c>.
    /// </summary>
    public override void OnHit(Node body)
    {
        // Duck-typed damage call — Player.TakeHit() will be implemented in
        // Slice 7 (TICKET-702).  HasMethod returns false until then, so this
        // is a safe no-op stub that keeps the projectile despawning correctly.
        if (body.HasMethod("TakeHit"))
            body.Call("TakeHit", 1);

        ReturnToPool();
    }
}
