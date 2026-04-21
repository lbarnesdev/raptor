// src/Projectiles/OrganicSpore.cs
// ─────────────────────────────────────────────────────────────────────────────
// Harbinger Turret's projectile.  Large, slow, aimed coral-orange blob.
// PRD F-223.
//
// Collision (set in OrganicSpore.tscn):
//   collision_layer = 32  (EnemyProjectile)
//   collision_mask  = 6   (PlayerShield=4 + Player=2)
//
// Unlike PlasmaBlob / CrystalSpine, velocity is NOT a compile-time constant —
// HarbingerTurret computes a direction vector toward the player at fire time
// and passes it to ProjectilePool.Get().  The nominal scalar speed is 140 px/s,
// but the Y component varies with aim angle.
//
// Same ADR-005 hit resolution — ShieldController handles player damage.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Harbinger Turret projectile — large coral-orange blob aimed at the player.
/// Velocity is set by the turret at fire time, not a fixed default.
/// </summary>
public partial class OrganicSpore : Projectile
{
    /// <summary>
    /// Nominal travel speed in px/s.  Turret uses this to scale the aimed
    /// direction vector: <c>velocity = dir.Normalized() * NominalSpeed</c>.
    /// </summary>
    public const float NominalSpeed = 140f;

    /// <inheritdoc/>
    public override void OnHit(Node body)
    {
        ReturnToPool();
    }
}
