// src/Enemies/BaseEnemy.cs
// ─────────────────────────────────────────────────────────────────────────────
// Base class for all enemies.
//
// Extends CharacterBody2D so PlasmaBolt's Area2D.BodyEntered callback fires
// when a bolt overlaps the enemy's collision shape.
//
// Collision setup (set in each enemy's .tscn, NOT here):
//   collision_layer = 8   (Enemy = layer 4 = 2^3)
//   collision_mask  = 0   (stationary enemies need no collision response)
//
// TakeDamage wiring:
//   PlasmaBolt.OnHit calls body.HasMethod("TakeDamage") + body.Call("TakeDamage", 1).
//   This is the duck-typed entry point — no interface needed until IDamageable
//   is defined in a later ticket.
//
// Score wiring:
//   Die() calls GameManager.Instance.OnEnemyKilled(ScoreValue), which delegates
//   to ScoreSystem.AddKill and emits EventBus.ScoreChanged.
//
// Group membership:
//   _Ready() calls AddToGroup("enemies") so MissileWeapon can find live enemies
//   when computing homing targets.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.Enemies;

/// <summary>
/// Minimal enemy: absorbs hits, awards score on death.
/// Subclass to add movement, shooting, or phase transitions.
/// </summary>
public partial class BaseEnemy : CharacterBody2D
{
    // ── Exported tunables ────────────────────────────────────────────────────

    /// <summary>Hit points before this enemy dies.</summary>
    [Export] public int MaxHealth  { get; set; } = 3;

    /// <summary>
    /// Base score awarded on death (before GameManager applies the multiplier).
    /// </summary>
    [Export] public int ScoreValue { get; set; } = 100;

    // ── Private state ────────────────────────────────────────────────────────

    private int _health;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _health = MaxHealth;

        // MissileWeapon.TryFire queries the "enemies" group for target positions.
        AddToGroup("enemies");
    }

    // ── Public API (duck-typed by PlasmaBolt / Missile) ──────────────────────

    /// <summary>
    /// Subtract <paramref name="amount"/> hit points.  Calls <see cref="Die"/>
    /// when health reaches zero.
    ///
    /// Called via Godot duck-typing (<c>body.Call("TakeDamage", 1)</c>) from
    /// projectile <c>OnHit</c> handlers — no C# interface needed yet.
    /// </summary>
    public void TakeDamage(int amount)
    {
        _health -= amount;
        if (_health <= 0)
            Die();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Die()
    {
        // Award score via GameManager — keeps ScoreSystem off the enemy.
        GameManager.Instance.OnEnemyKilled(ScoreValue);

        // TODO (Slice 4): play death animation / spawn explosion VFX before freeing.
        QueueFree();
    }
}
