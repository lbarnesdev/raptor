// src/Projectiles/FoxBroadcastPulse.cs
// ─────────────────────────────────────────────────────────────────────────────
// Boss Phase 2 projectile — a medium-speed red broadcast pulse.
//
// Used in two patterns by Phase2Controller (TICKET-602):
//   Fan burst  — 5 pulses spread across ±45° from straight left, fired every 2 s
//                while the Toupee hatch is Open.
//   Radial ring — 8 pulses in a full 360° ring every 5 s, independent of
//                 the Open/Closed cycle.
//
// Node placement (FoxBroadcastPulse.tscn):
//   FoxBroadcastPulse (Area2D, extends Projectile)
//   ├── CollisionShape2D  (RectangleShape2D 10×10 matching diamond hitbox)
//   └── PlaceholderShape  (Polygon2D red diamond ~14 px)
//
// Collision setup (set in .tscn):
//   collision_layer = 32   (EnemyProjectile = layer 6 = 2^5)
//   collision_mask  =  6   (Player=2 + PlayerShield=4)
//
// Velocity is always set by Phase2Controller before pool activation —
// there is no single "default" direction because both patterns vary it.
// DefaultSpeed is exposed as a constant for the controller to use.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Projectiles;

/// <summary>
/// Phase 2 boss projectile.  Direction and speed are set by
/// <see cref="Raptor.Boss.Phase2Controller"/> on each spawn.
/// </summary>
public partial class FoxBroadcastPulse : Projectile
{
    // ── Speed constant ────────────────────────────────────────────────────────

    /// <summary>Pixels per second for all FoxBroadcastPulse instances.</summary>
    public const float DefaultSpeed = 200f;

    // ── Projectile override ───────────────────────────────────────────────────

    /// <summary>
    /// Despawns on first contact.  Full player damage deferred to Slice 7.
    /// </summary>
    public override void OnHit(Node body)
    {
        if (body.HasMethod("TakeHit"))
            body.Call("TakeHit", 1);

        ReturnToPool();
    }
}
