// src/Enemies/HarbingerTurret.cs
// ─────────────────────────────────────────────────────────────────────────────
// Stationary ground-mounted turret with alien organic coral growth.
// Aims its barrel Node2D at the player each frame and fires OrganicSpore on a
// Timer.  Placed statically in the editor — NOT spawned by LevelDirector waves.
// PRD F-221–225.
//
// Extends Node2D (not BaseEnemy / CharacterBody2D) because turrets never move.
//
// TakeDamage is duck-typed (called via body.Call("TakeDamage", n)) by player
// projectile OnHit handlers — no interface required.
//
// Collision (set in HarbingerTurret.tscn):
//   StaticBody2D child handles physics; turret root is plain Node2D.
//   Enemy body collision_layer = 8 (Enemy, layer 4).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Enemies;

/// <summary>
/// Stationary turret that aims at the player and fires large organic spores.
/// TakeDamage is duck-typed from player projectiles.
/// </summary>
public partial class HarbingerTurret : Node2D
{
    // ── Exports ──────────────────────────────────────────────────────────────

    /// <summary>Maximum hit points before the turret is destroyed.</summary>
    [Export] public int MaxHealth  { get; set; } = 4;

    /// <summary>Score awarded on destruction (before multiplier).</summary>
    [Export] public int ScoreValue { get; set; } = 200;

    /// <summary>Seconds between OrganicSpore shots.</summary>
    [Export] public float FireRate { get; set; } = 2.5f;

    // ── Child node references ────────────────────────────────────────────────

    /// <summary>
    /// Node2D child that visually represents the barrel.  Rotated toward the
    /// player each frame; OrganicSpore spawns at its global position.
    /// </summary>
    [Export] public NodePath BarrelPath { get; set; } = "Barrel";

    // ── Private state ────────────────────────────────────────────────────────

    private int   _health;
    private bool  _dead;

    private Node2D?  _barrel;
    private Node2D?  _player;
    private Godot.Timer? _fireTimer;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _health = MaxHealth;

        AddToGroup("enemies");

        _barrel = GetNodeOrNull<Node2D>(BarrelPath);
        _player = GetNodeOrNull<Node2D>("/root/Level01/Entities/Player");

        // Create a Timer child for the fire cadence.
        _fireTimer          = new Godot.Timer();
        _fireTimer.WaitTime = FireRate;
        _fireTimer.Autostart = true;
        _fireTimer.Timeout  += OnFireTimerTimeout;
        AddChild(_fireTimer);
    }

    public override void _Process(double delta)
    {
        if (_dead) return;
        AimBarrelAtPlayer();
    }

    // ── Public API (duck-typed by player projectiles) ────────────────────────

    /// <summary>
    /// Reduce health by <paramref name="amount"/>.  Called via Godot duck-typing
    /// from player projectile <c>OnHit</c> handlers.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (_dead) return;

        _health -= amount;
        if (_health <= 0)
            Die();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void AimBarrelAtPlayer()
    {
        if (_barrel is null) return;
        if (_player is null || !IsInstanceValid(_player)) return;

        // Rotate the barrel toward the player's global position.
        Vector2 dir = _player.GlobalPosition - _barrel.GlobalPosition;
        _barrel.Rotation = dir.Angle();
    }

    private void OnFireTimerTimeout()
    {
        if (_dead) return;
        if (_player is null || !IsInstanceValid(_player)) return;

        FireOrganicSpore();
    }

    private void FireOrganicSpore()
    {
        var pool = ProjectilePool.Instance;
        if (pool is null) return;

        // Determine spawn position: barrel tip (offset along barrel direction).
        Vector2 spawnPos = _barrel is not null
            ? _barrel.GlobalPosition
            : GlobalPosition;

        // Compute aimed direction toward player and scale by NominalSpeed.
        Vector2 dir = Vector2.Zero;
        if (_player is not null && IsInstanceValid(_player))
            dir = (_player.GlobalPosition - spawnPos).Normalized();

        if (dir == Vector2.Zero)
            dir = Vector2.Left; // Fallback: leftward if no player.

        Vector2 velocity = dir * OrganicSpore.NominalSpeed;
        pool.Get(ProjectileType.OrganicSpore, spawnPos, velocity);

        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.EnemyShoot);
    }

    private void Die()
    {
        _dead = true;
        _fireTimer?.Stop();

        GameManager.Instance.OnEnemyKilled(ScoreValue);
        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.EnemyExplode);

        QueueFree();
    }
}
