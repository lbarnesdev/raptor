// src/Player/Player.cs
// ─────────────────────────────────────────────────────────────────────────────
// Player controller — Godot 4 CharacterBody2D.
//
// Collision setup (set in Player.tscn Inspector, not in code):
//   collision_layer = 2  (Player)
//   collision_mask  = 161  (layers 1 + 6 + 8 = World + EnemyProjectile + Hazard)
//   Bitmask reference:
//     Layer 1 → 2^0 = 1   (World / terrain)
//     Layer 6 → 2^5 = 32  (EnemyProjectile)
//     Layer 8 → 2^7 = 128 (Hazard / SporeCloud)
//     Total   = 161
//
// Scene tree (Player.tscn — see arch doc Section 3.2):
//   Player (CharacterBody2D)  ← this script
//   ├── CollisionShape2D
//   ├── AnimatedSprite2D
//   ├── ShieldController (Node)
//   ├── PlasmaWeapon (Node)
//   ├── MissileWeapon (Node)
//   └── HurtFlash (AnimationPlayer)
//
// Input map (must be configured in Project Settings → Input Map):
//   move_left, move_right, move_up, move_down
//
// Boundaries:
//   • Vertical:    clamped against camera centre ± half-height.
//   • Left/Right:  clamped against the camera's visible edges ± EdgeMargin.
//                  When ScrollCamera scrolls (Slice 4) the left wall moves with
//                  the camera, naturally pushing the player forward.
//
// Stub methods Die() and Respawn() are intentionally empty.  They will be
// fleshed out in the ShieldController and CheckpointManager tickets respectively.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Player;

/// <summary>
/// Handles player movement, screen-boundary enforcement, and exposes
/// lifecycle stubs (<see cref="Die"/>, <see cref="Respawn"/>) for use by
/// <c>GameManager.cs</c> and <c>ShieldController.cs</c>.
/// </summary>
public partial class Player : CharacterBody2D
{
    // ── Exported tunable ─────────────────────────────────────────────────────

    /// <summary>
    /// Maximum movement speed in pixels per second.
    /// Exposed as an <c>[Export]</c> so it can be tweaked in the Godot Inspector
    /// without recompiling — useful during gameplay balance passes.
    /// </summary>
    [Export] public float MoveSpeed { get; set; } = 220f;

    // ── Screen-boundary margin ────────────────────────────────────────────────

    /// <summary>
    /// Pixels of padding kept between the player's origin and each screen edge.
    /// Prevents the sprite from clipping into the HUD or the terrain border.
    /// </summary>
    private const float EdgeMargin = 32f;

    // ── Cached node references ────────────────────────────────────────────────

    // GetNode is called once in _Ready and the result is cached.
    // Calling GetNode every _PhysicsProcess frame is cheap but unnecessary noise;
    // caching also makes the null-check happen at startup rather than at runtime.
    private Camera2D _camera = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Camera is a sibling in the Level01 scene (ADR-002: not a child of Player).
        _camera = GetNode<Camera2D>("/root/Level01/ScrollCamera");
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        // ── Input ────────────────────────────────────────────────────────────
        // GetVector normalises diagonal input to length 1.0 automatically,
        // so diagonal movement is the same speed as cardinal movement.
        Vector2 inputDir = Input.GetVector(
            "move_left", "move_right",
            "move_up",   "move_down");

        Velocity = inputDir * MoveSpeed;
        MoveAndSlide();

        // ── Vertical boundary ─────────────────────────────────────────────
        // Clamp the player's Y so it cannot leave the visible viewport area.
        // The camera's GlobalPosition is the centre of the screen.
        Rect2  viewport = GetViewportRect();
        float  halfH    = viewport.Size.Y / 2f;
        float  camY     = _camera.GlobalPosition.Y;

        GlobalPosition = new Vector2(
            GlobalPosition.X,
            Mathf.Clamp(
                GlobalPosition.Y,
                camY - halfH + EdgeMargin,   // top boundary
                camY + halfH - EdgeMargin)); // bottom boundary

        // ── Horizontal boundaries ─────────────────────────────────────────
        // Right: player cannot exceed the right screen edge.
        // Left:  player cannot go behind the left screen edge.
        //        When ScrollCamera begins scrolling (Slice 4) this will
        //        automatically act as a moving wall that pushes the player
        //        forward — no code change required here.
        float camX     = _camera.GlobalPosition.X;
        float halfW    = viewport.Size.X / 2f;
        float leftEdge  = camX - halfW + EdgeMargin;
        float rightEdge = camX + halfW  - EdgeMargin;

        GlobalPosition = new Vector2(
            Mathf.Clamp(GlobalPosition.X, leftEdge, rightEdge),
            GlobalPosition.Y);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Kills the player.  Called by the projectile handler when a hit lands
    /// while the shield is <see cref="ShieldIsActive"/> == <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Full implementation delivered in the ShieldController ticket (TICKET-031).
    /// At that point this method will:
    ///   1. Play the death animation.
    ///   2. Emit <c>EventBus.Instance.EmitSignal(EventBus.SignalName.PlayerDied)</c>.
    ///   3. Disable input processing until <see cref="Respawn"/> is called.
    /// </remarks>
    public void Die()
    {
        // TODO (TICKET-031): animate death, emit PlayerDied, disable input.
    }

    /// <summary>
    /// Teleports the player to <paramref name="position"/> and resets
    /// transient state (shield, ammo).  Called by <c>GameManager.OnPlayerDied</c>
    /// after the 2-second respawn delay.
    /// </summary>
    /// <param name="position">
    /// World-space spawn point supplied by <c>CheckpointManager</c>.
    /// </param>
    /// <remarks>
    /// Full implementation delivered in the CheckpointManager ticket.
    /// At that point this method will:
    ///   1. Set <c>GlobalPosition = position</c>.
    ///   2. Call <c>ShieldController.Reset()</c> to restore the shield.
    ///   3. Re-enable input processing.
    /// </remarks>
    public void Respawn(Vector2 position)
    {
        // TODO (CheckpointManager ticket): teleport, reset shield + ammo, re-enable input.
        GlobalPosition = position; // minimal stub so GameManager can call this safely
    }

    // ── Shield state pass-through ─────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when the shield is currently absorbing hits
    /// (states <c>Active</c>, <c>GracePeriod</c>, or <c>Recharging</c>).
    /// </summary>
    /// <remarks>
    /// Replaced by a real implementation in TICKET-031 once
    /// <c>ShieldController</c> is wired up.  The stub returns <c>false</c>
    /// so projectile hit handlers compile and run without crashing — they
    /// will always route through <see cref="Die"/> until the shield is live.
    /// </remarks>
    public bool ShieldIsActive => false; // TICKET-031: return _shieldController.IsActive
}
