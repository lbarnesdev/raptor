// src/Projectiles/CrystalSpine.cs
// ─────────────────────────────────────────────────────────────────────────────
// Specter Fighter's projectile.  Narrow, fast, red crystalline shard.  PRD F-213.
//
// Collision (set in CrystalSpine.tscn):
//   collision_layer = 32  (EnemyProjectile)
//   collision_mask  = 6   (PlayerShield=4 + Player=2)
//
// Same ADR-005 hit resolution as PlasmaBlob — ShieldController handles damage.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Specter Fighter projectile — fast narrow crystal traveling leftward.
/// </summary>
public partial class CrystalSpine : Projectile
{
    /// <summary>Default leftward velocity used by SpecterFighter when spawning.</summary>
    public static readonly Vector2 DefaultVelocity = new(-320f, 0f);

    /// <inheritdoc/>
    public override void OnHit(Node body)
    {
        ReturnToPool();
    }
}
