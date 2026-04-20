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
// Die() / Respawn() lifecycle:
//   ShieldController.OnShieldAreaAreaEntered → not absorbed → Player.Die()
//   Player.Die() → SetPhysicsProcess(false), play hurt_flash, emit PlayerDied
//   GameManager.OnPlayerDied() → await 2s → CheckpointManager.GetRespawnPosition()
//                              → Player.Respawn(pos)
//   Player.Respawn() → GlobalPosition, _isDead=false, SetPhysicsProcess(true),
//                      ShieldController.Reset()
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

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

    // ── Death guard ───────────────────────────────────────────────────────────

    /// <summary>
    /// Set to <c>true</c> between <see cref="Die"/> and <see cref="Respawn"/>.
    /// Guards against <see cref="Die"/> being called twice (e.g. two projectiles
    /// arriving in the same physics step when the shield is Broken).
    /// </summary>
    private bool _isDead;

    // ── Cached node references ────────────────────────────────────────────────

    // GetNode is called once in _Ready and the result is cached.
    // Calling GetNode every _PhysicsProcess frame is cheap but unnecessary noise;
    // caching also makes the null-check happen at startup rather than at runtime.
    private Camera2D          _camera          = null!;
    private ShieldController  _shieldController = null!;
    private AnimationPlayer   _hurtFlash        = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Camera is a sibling in the Level01 scene (ADR-002: not a child of Player).
        _camera           = GetNode<Camera2D>("/root/Level01/ScrollCamera");
        _shieldController = GetNode<ShieldController>("ShieldController");
        _hurtFlash        = GetNode<AnimationPlayer>("HurtFlash");
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
    /// Kills the player.  Called by <see cref="ShieldController"/> when an enemy
    /// projectile hits while the shield is <see cref="ShieldState.Broken"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent — safe to call from multiple projectiles arriving the same frame.
    /// The 2-second respawn window and lives decrement are handled by
    /// <see cref="GameManager.OnPlayerDied"/> which subscribes to
    /// <see cref="EventBus.PlayerDied"/>.
    /// </remarks>
    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        // Stop processing input/movement until Respawn() re-enables it.
        SetPhysicsProcess(false);

        // Visual feedback — hurt_flash animation flickers PlaceholderShape
        // red/transparent three times over 0.5 s, ending at normal blue.
        _hurtFlash.Play("hurt_flash");

        // Audio + score multiplier reset are handled by ShieldController on the
        // GracePeriod → Broken transition.  We only need the death SFX here.
        AudioManager.Instance.PlaySfx(AudioManager.Sfx.PlayerDeath);

        // Notify GameManager: decrement lives, schedule respawn or Game Over.
        EventBus.Instance.EmitSignal(EventBus.SignalName.PlayerDied);
    }

    /// <summary>
    /// Teleports the player to <paramref name="position"/> and resets transient
    /// state.  Called by <see cref="GameManager"/> after the 2-second death pause.
    /// </summary>
    /// <param name="position">
    /// World-space respawn point from <c>CheckpointManager.GetRespawnPosition()</c>.
    /// </param>
    public void Respawn(Vector2 position)
    {
        GlobalPosition = position;
        _isDead        = false;

        // Re-enable physics so the player can move again.
        SetPhysicsProcess(true);

        // Restore the shield to Active so the player doesn't respawn defenceless.
        _shieldController.Reset();
    }

    // ── Shield state pass-through ─────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when the shield is currently absorbing hits
    /// (states <c>Active</c>, <c>GracePeriod</c>, or <c>Recharging</c>).
    /// Read by the player body's own collision handler if it ever needs to
    /// distinguish "hit while shielded" from "hit while broken".
    /// </summary>
    public bool ShieldIsActive => !_shieldController.IsVulnerable;
}
