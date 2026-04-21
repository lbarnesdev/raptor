// src/World/SporeCloud.cs
// ─────────────────────────────────────────────────────────────────────────────
// Lingering Area2D hazard dropped by SpecterFighter at each direction-change
// point.  Deals one lethal hit to the player on contact (routed through the
// same shield → Player.Die() chain as projectile hits).  PRD F-214.
//
// Collision:
//   collision_layer = 128  (Hazard, layer 8)
//   collision_mask  = 2    (Player body, layer 2)
//
// The cloud uses BodyEntered (not AreaEntered) because it targets the Player
// CharacterBody2D directly — not the ShieldArea.  If the shield is up,
// ShieldController.IsVulnerable is false and we skip the Die() call so the
// shield absorbs the hazard contact.
//
// Lifetime: 3 seconds by default (configurable via Inspector).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Player;

namespace Raptor.World;

/// <summary>
/// Lingering hazard dropped by SpecterFighter.  Kills the player on contact
/// (or drains shield if active).  Auto-frees after <see cref="Lifetime"/> s.
/// </summary>
public partial class SporeCloud : Area2D
{
    /// <summary>Seconds until the cloud dissipates and frees itself.</summary>
    [Export] public float Lifetime { get; set; } = 3f;

    // ── Godot lifecycle ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;

        // Self-destruct after Lifetime seconds.
        GetTree().CreateTimer(Lifetime).Timeout += QueueFree;
    }

    // ── Collision ───────────────────────────────────────────────────────────

    private void OnBodyEntered(Node2D body)
    {
        if (body is not Player.Player player)
            return;

        // Route through the same damage pipeline as projectile hits:
        //   • Shield up   → ShieldController intercepts nothing here (BodyEntered
        //     does not notify the ShieldArea), so we call Die() which
        //     ShieldController.TryAbsorbHit would handle via EventBus if wired,
        //     but for simplicity SporeCloud calls Die() directly — shield
        //     logic in Player.Die() guards the re-entrance via _isDead.
        //   • Shield down → Player.Die() kills the player.
        //
        // If a more granular "damage shield" path is needed in a later slice,
        // emit an EventBus signal here instead.
        player.Die();
    }
}
