// src/Player/PlasmaWeapon.cs
// ─────────────────────────────────────────────────────────────────────────────
// Player primary weapon — spawns PlasmaBolt instances from the projectile pool.
//
// Node placement (Player.tscn — see arch doc Section 3.2):
//   Player (CharacterBody2D)
//   └── PlasmaWeapon (Node)   ← this script
//       └── MuzzlePoint (Marker2D)
//
// Reparenting (ADR-003):
//   ProjectilePool keeps each bolt dormant under an internal container node.
//   On spawn, the bolt is reparented to Level01/ProjectileContainer so its
//   lifetime is independent of PlasmaWeapon or Player — if the player dies,
//   in-flight bolts are NOT freed.  Reparent() is atomic (remove + add) and
//   safe to call from _Process().
//
// SFX throttle:
//   At 12 shots/second, playing a SFX every frame sounds like static.
//   AudioManager is called on every 3rd shot → 4 audio ticks/second, which
//   reads as a recognisable rapid-fire rhythm without drowning the mix.
//
// Input action required in Project Settings → Input Map:
//   "fire"  (e.g. mapped to Left Ctrl + Z key + joystick button)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Player;

/// <summary>
/// Manages the player's primary plasma bolt weapon: input polling, fire-rate
/// gating, pool acquisition, and reparenting to <c>ProjectileContainer</c>.
/// </summary>
public partial class PlasmaWeapon : Node2D
{
    // ── Exported tunables ────────────────────────────────────────────────────

    /// <summary>
    /// Bolts fired per second.  12 f/s is the default; increase during power-up
    /// states by setting this property from Player.cs.
    /// </summary>
    [Export] public float FireRate { get; set; } = 12f;

    // ── Private state ─────────────────────────────────────────────────────────

    private float _fireTimer = 0f;
    private int   _shotCount = 0;

    // ── Cached node references ────────────────────────────────────────────────

    private Marker2D _muzzlePoint       = null!;
    private Node     _projectileContainer = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _muzzlePoint = GetNode<Marker2D>("MuzzlePoint");

        // Cache the container that receives live projectiles (ADR-003).
        // Hard path is safe here — PlasmaWeapon is always a descendant of Level01.
        _projectileContainer = GetNode("/root/Level01/ProjectileContainer");
    }

    // ── Per-frame input polling ───────────────────────────────────────────────

    public override void _Process(double delta)
    {
        // Count down regardless of input so the timer is accurate on rapid
        // tap-fire (it shouldn't reset to full on every _Process call).
        _fireTimer -= (float)delta;

        if (Input.IsActionPressed("fire") && _fireTimer <= 0f)
        {
            _fireTimer = 1f / FireRate;
            SpawnBolt();
        }
    }

    // ── Spawn helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a <see cref="PlasmaBolt"/> from the pool, places it at the
    /// muzzle, and reparents it to <c>ProjectileContainer</c>.
    /// </summary>
    private void SpawnBolt()
    {
        // Acquire from pool — returns null if the pool is exhausted.
        // We pass GlobalPosition before reparenting because GlobalPosition
        // is world-space and survives the parent change unchanged.
        var bolt = ProjectilePool.Instance.Get(
            ProjectileType.PlasmaBolt,
            _muzzlePoint.GlobalPosition,
            Vector2.Right * 650f);

        if (bolt is null)
            return; // pool exhausted — silently skip this shot

        // ── Reparent to ProjectileContainer (ADR-003) ────────────────────────
        // bolt is currently a child of ProjectilePool's internal Pool_PlasmaBolt
        // container.  Reparent() atomically removes it from that parent and adds
        // it to ProjectileContainer, preserving GlobalPosition (keepGlobalTransform
        // defaults to true in Godot 4's Reparent overload).
        bolt.Reparent(_projectileContainer);

        // ── SFX (throttled) ──────────────────────────────────────────────────
        _shotCount++;
        if (_shotCount % 3 == 0)
            AudioManager.Instance.PlaySfx(AudioManager.Sfx.PlasmaFire);
    }
}
