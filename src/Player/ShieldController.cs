// src/Player/ShieldController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Godot-layer glue that owns a ShieldStateMachine, drives shield visuals, and
// routes incoming projectiles through the pure-C# state machine.
//
// Node placement (Player.tscn — see arch doc Section 3.2):
//   Player (CharacterBody2D)
//   └── ShieldController (Node)   ← this script
//       └── ShieldArea (Area2D)   layer 3, mask 6
//           ├── CollisionShape2D  (CircleShape2D, slightly larger than ship)
//           └── ShieldSprite (Sprite2D)
//
// Collision layer note:
//   ShieldArea is on layer 3 (PlayerShield) and masks layer 6 (EnemyProjectile).
//   The player body is on layer 2 and ALSO masks layer 6.  Both receive
//   body_entered events for the same projectile.  The shield handler fires first
//   (ADR-005), absorbs or passes the hit, then returns the projectile to the
//   pool.  The player's handler (not yet implemented) must check ShieldIsActive
//   and bail if the shield already consumed the hit.
//
// OnStateChanged contract:
//   ShieldStateMachine.OnStateChanged fires at the END of OnEnter(), after
//   internal timers are set.  This means the handler below sees a fully
//   initialised state — safe to read CurrentState, GraceDuration, etc.
//
// Score-multiplier reset:
//   GameManager.OnPlayerHit() is called on GracePeriod (first real hit absorbed)
//   and again on Broken (grace expires, player is now truly vulnerable).
//   Both represent the same physical "taking damage" event from the player's
//   perspective — the GracePeriod reset covers the hit itself; the Broken reset
//   is intentionally redundant under the current spec.  If that proves wrong
//   during playtesting, remove the Broken branch from OnShieldStateChanged.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Logic;
using Raptor.Projectiles;

namespace Raptor.Player;

/// <summary>
/// Bridges <see cref="ShieldStateMachine"/> (pure C#) with the Godot scene:
/// intercepts projectiles via <c>ShieldArea</c>, updates shield visuals, and
/// forwards state changes to <see cref="EventBus"/> and <see cref="AudioManager"/>.
/// </summary>
public partial class ShieldController : Node
{
    // ── Shield state machine ─────────────────────────────────────────────────

    /// <summary>
    /// The pure-C# state machine.  Created here so its lifetime is tied to
    /// this node rather than the Player, and so it's testable independently.
    /// </summary>
    private readonly ShieldStateMachine _shield = new();

    // ── Cached node references ────────────────────────────────────────────────

    private Area2D   _shieldArea   = null!;
    private Sprite2D _shieldSprite = null!;
    private Player   _player       = null!;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when the shield is in <see cref="ShieldState.Broken"/> and
    /// incoming hits reach the player's health.  Checked by the player's own
    /// body_entered handler to decide whether to call <see cref="Player.Die"/>.
    /// </summary>
    public bool IsVulnerable => _shield.IsVulnerable;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Node references — paths relative to ShieldController's position in
        // the Player.tscn hierarchy (see arch doc Section 3.2).
        _shieldArea   = GetNode<Area2D>("ShieldArea");
        _shieldSprite = GetNode<Sprite2D>("ShieldArea/ShieldSprite");
        _player       = GetParent<Player>();

        // Wire projectile interception.
        _shieldArea.BodyEntered += OnShieldAreaBodyEntered;

        // Wire state-change notifications from the pure-C# machine.
        // OnStateChanged fires inside ShieldStateMachine.OnEnter(), after timers
        // are set, so the handler always receives a fully-initialised state.
        _shield.OnStateChanged += OnShieldStateChanged;

        // Apply initial visual to match the starting Active state.
        OnShieldStateChanged(ShieldState.Active);
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        // Tick the pure-C# machine.  It counts down grace and recharge timers
        // and calls Transition() internally when they expire, which in turn
        // fires OnStateChanged so we see the transition here too.
        _shield.Update(delta);
    }

    // ── Signal handlers ───────────────────────────────────────────────────────

    /// <summary>
    /// Called when a body enters the <c>ShieldArea</c> (layer 3, mask 6).
    /// Only enemy projectiles land here; terrain and hazards are not on layer 6.
    /// </summary>
    private void OnShieldAreaBodyEntered(Node2D body)
    {
        // Guard: only handle Projectile instances.
        // ShieldArea masks layer 6 (EnemyProjectile) so non-projectile bodies
        // should never arrive here, but the type check is cheap insurance.
        if (body is not Projectile projectile)
            return;

        bool absorbed = _shield.TryAbsorbHit();

        // Return the projectile to the pool unconditionally — it has hit the
        // shield zone regardless of whether the shield absorbed the damage.
        ProjectilePool.Instance.Return(projectile);

        // If the shield was Broken, the hit passes through to the player.
        if (!absorbed)
            _player.Die();
    }

    /// <summary>
    /// Reacts to every shield state transition: updates the sprite colour,
    /// emits the EventBus signal, and plays the appropriate SFX.
    /// </summary>
    private void OnShieldStateChanged(ShieldState state)
    {
        // ── Visual ────────────────────────────────────────────────────────────
        _shieldSprite.Modulate = state switch
        {
            ShieldState.Active      => new Color(0.3f, 0.7f, 1.0f, 0.8f),  // opaque blue
            ShieldState.GracePeriod => new Color(1.0f, 0.3f, 0.3f, 0.6f),  // red, semi-transparent
            ShieldState.Broken      => new Color(1.0f, 0.0f, 0.0f, 0.0f),  // fully transparent (invisible)
            ShieldState.Recharging  => new Color(0.3f, 0.7f, 1.0f, 0.4f),  // dim blue
            _                       => Colors.White,
        };

        // ── EventBus ─────────────────────────────────────────────────────────
        // HUD subscribes to ShieldStateChanged to update the shield icon colour.
        EventBus.Instance.EmitSignal(
            EventBus.SignalName.ShieldStateChanged,
            state.ToString());

        // ── Audio + score side-effects ────────────────────────────────────────
        switch (state)
        {
            case ShieldState.GracePeriod:
            case ShieldState.Broken:
                // A hit was absorbed (GracePeriod) or the shield is now down (Broken).
                // Both states represent taking damage — reset the score multiplier.
                GameManager.Instance.OnPlayerHit();
                AudioManager.Instance.PlaySfx(AudioManager.Sfx.ShieldBreak);
                break;

            case ShieldState.Active:
                // Recharge complete — shield is back up.
                AudioManager.Instance.PlaySfx(AudioManager.Sfx.ShieldRecharge);
                break;
        }
    }
}
