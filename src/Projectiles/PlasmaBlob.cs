// src/Projectiles/PlasmaBlob.cs
// ─────────────────────────────────────────────────────────────────────────────
// Wraith Fighter's projectile.  Medium-speed bioluminescent blob that travels
// leftward toward the player.  PRD F-204.
//
// Collision (set in PlasmaBlob.tscn):
//   collision_layer = 32  (EnemyProjectile)
//   collision_mask  = 6   (PlayerShield=4 + Player=2)
//
// Hit resolution follows ADR-005:
//   ShieldArea (Area2D, mask 32) intercepts the blob via area_entered before
//   the Player body can receive body_entered.  ShieldController.TryAbsorbHit()
//   absorbs the hit if the shield is up, or calls Player.Die() if broken.
//   ReturnToPool() is called by ShieldController regardless.
//
//   If the blob somehow reaches Player's body_entered (e.g. shield area missed),
//   OnHit fires and returns the blob to the pool.  Player damage is handled
//   separately through the shield → Player.Die() pipeline.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Wraith Fighter projectile — medium green blob traveling leftward.
/// </summary>
public partial class PlasmaBlob : Projectile
{
    /// <summary>Default leftward velocity used by WraithFighter when spawning.</summary>
    public static readonly Vector2 DefaultVelocity = new(-240f, 0f);

    /// <inheritdoc/>
    public override void OnHit(Node body)
    {
        // ShieldController intercepts most hits via AreaEntered (ADR-005).
        // If this fires, the blob has reached a body that was not the shield —
        // just despawn.  Player damage is routed through ShieldController.
        ReturnToPool();
    }
}
