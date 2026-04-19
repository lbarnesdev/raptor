// src/Player/MissileWeapon.cs
// ─────────────────────────────────────────────────────────────────────────────
// Bridges the pure-C# MissileSystem (ammo tracking + target selection) with
// Godot for input, pool acquisition, and event emission.
//
// Node placement (Player.tscn — see arch doc Section 3.2):
//   Player (CharacterBody2D)
//   └── MissileWeapon (Node)  ← this script
//       └── MuzzlePoint (Marker2D)
//
// Ammo gain wiring (design note):
//   MissileSystem owns the ammo count and MissileWeapon is the only class that
//   should mutate it.  Rather than subscribing to EventBus.AmmoGained (which
//   would create a re-emit loop — GameManager fires AmmoGained, MissileWeapon
//   catches it, calls AddAmmo, re-emits AmmoGained; HUD receives two calls with
//   two different totals on the same frame), GameManager calls GainAmmo(1)
//   directly.  MissileWeapon then emits AmmoGained with the authoritative total.
//
//   GameManager wires this in its _Ready():
//     _missileWeapon = GetNode<Player>("/root/Level01/Entities/Player")
//                          .GetNode<MissileWeapon>("MissileWeapon");
//   And inside OnEnemyKilled():
//     _missileWeapon?.GainAmmo(1);
//
// Reparenting (ADR-003):
//   Missiles are reparented to ProjectileContainer after pool acquisition so
//   their lifetime is independent of Player — in-flight missiles survive a
//   player death.
//
// Vector2 conversion:
//   MissileSystem uses System.Numerics.Vector2 for Godot-free testability in
//   xUnit.  All conversion is confined to ToSysVec / ToGodotVec helpers at the
//   bottom of this file.
//
// N-002 compliance:
//   Enemy positions are written into a pre-allocated _positionBuffer list.
//   No List/array is allocated on the input-fired frame.
//
// Input action required in Project Settings → Input Map:
//   "special"  (e.g. X key + Left Shift + joystick button 2)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Logic;
using Raptor.Projectiles;
using SysVec = System.Numerics.Vector2;

namespace Raptor.Player;

/// <summary>
/// Player missile weapon — queries visible enemy positions, delegates target
/// selection to <see cref="MissileSystem"/>, and spawns homing
/// <see cref="Missile"/> instances from the projectile pool.
/// </summary>
public partial class MissileWeapon : Node2D
{
    // ── Pure-logic system ─────────────────────────────────────────────────────

    /// <summary>
    /// Owns ammo count and nearest-target selection.  Zero Godot dependencies
    /// so it can be constructed and tested in plain xUnit.
    /// </summary>
    private readonly MissileSystem _missiles = new();

    // ── Pre-allocated enemy position buffer (N-002) ───────────────────────────

    /// <summary>
    /// Reused on every fire input — populated with enemy positions then passed
    /// to <see cref="MissileSystem.TryFire"/>.  Never heap-allocated on fire.
    /// Capacity 32 is well above the expected maximum on-screen enemy count.
    /// </summary>
    private readonly List<SysVec> _positionBuffer = new(32);

    // ── Cached node references ────────────────────────────────────────────────

    private Marker2D _muzzlePoint         = null!;
    private Node     _projectileContainer = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _muzzlePoint         = GetNode<Marker2D>("MuzzlePoint");
        _projectileContainer = GetNode("/root/Level01/ProjectileContainer");

        // Emit initial state so HUD icon row is correct at scene load.
        EventBus.Instance.EmitSignal(EventBus.SignalName.AmmoGained, _missiles.Ammo);
    }

    // ── Per-frame input ───────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        // IsActionJustPressed fires once per keydown — no repeat handling needed.
        if (!Input.IsActionJustPressed("special"))
            return;

        // ── Gather live enemy positions — manual loop, no LINQ (N-002) ───────
        _positionBuffer.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Node2D enemy2d)
                _positionBuffer.Add(ToSysVec(enemy2d.GlobalPosition));
        }

        var player     = GetParent<Node2D>();
        var targets    = _missiles.TryFire(_positionBuffer, ToSysVec(player.GlobalPosition));

        if (targets.Count == 0)
        {
            // 0 ammo or no visible targets — HUD pulses red via MissileFired(0).
            EventBus.Instance.EmitSignal(EventBus.SignalName.MissileFired, 0);
            return;
        }

        // Single lock-on tone for the whole salvo (not one per missile).
        AudioManager.Instance.PlaySfx(AudioManager.Sfx.MissileLock);

        foreach (SysVec target in targets)
            SpawnMissile(ToGodotVec(target));

        // Emit post-decrement count so HUD knows the new ammo level.
        EventBus.Instance.EmitSignal(EventBus.SignalName.MissileFired, _missiles.Ammo);
    }

    // ── Spawn helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a <see cref="Missile"/> from the pool, assigns its homing target,
    /// and reparents it to <c>ProjectileContainer</c> (ADR-003).
    /// </summary>
    /// <param name="targetWorldPos">
    /// World-space position of the selected enemy at the moment of firing.
    /// The missile homes toward this point; it does not track movement after
    /// spawn (tracking behaviour can be added later by updating
    /// <see cref="Missile.TargetPosition"/> each frame from outside).
    /// </param>
    private void SpawnMissile(Vector2 targetWorldPos)
    {
        // Initial velocity is straight right — the homing system bends it
        // toward the target each physics frame.
        var missile = ProjectilePool.Instance.Get(
            ProjectileType.Missile,
            _muzzlePoint.GlobalPosition,
            Vector2.Right * Missile.InitialSpeed);

        if (missile is null)
            return; // pool exhausted — silently skip

        // Assign homing destination before the missile is activated.
        if (missile is Missile m)
            m.TargetPosition = targetWorldPos;

        // Reparent so the missile outlives the Player node if it dies (ADR-003).
        missile.Reparent(_projectileContainer);

        // Per-missile whoosh SFX (the lock tone was played once above).
        AudioManager.Instance.PlaySfx(AudioManager.Sfx.MissileFire);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Awards <paramref name="count"/> missiles (capped at
    /// <see cref="MissileSystem.MaxAmmo"/>).  Called by
    /// <c>GameManager.OnEnemyKilled</c> — see file header for wiring notes.
    /// Emits <c>EventBus.AmmoGained</c> so the HUD icon row updates.
    /// </summary>
    /// <param name="count">Number of missiles to award (default 1).</param>
    public void GainAmmo(int count = 1)
    {
        int added = _missiles.AddAmmo(count);

        // Only emit if anything was actually added — avoids redundant HUD redraws
        // when the player is already at MaxAmmo.
        if (added > 0)
            EventBus.Instance.EmitSignal(EventBus.SignalName.AmmoGained, _missiles.Ammo);
    }

    /// <summary>
    /// Resets ammo to the default respawn amount.
    /// Called by <c>Player.Respawn()</c> (TICKET-034).
    /// Emits <c>AmmoGained</c> so the HUD immediately reflects the reset.
    /// </summary>
    public void ResetAmmo()
    {
        _missiles.ResetToDefault();
        EventBus.Instance.EmitSignal(EventBus.SignalName.AmmoGained, _missiles.Ammo);
    }

    /// <summary>
    /// Current missile ammo count.  Exposed so HUD can read the starting value
    /// before the first <c>AmmoGained</c> signal if needed.
    /// </summary>
    public int Ammo => _missiles.Ammo;

    // ── Vector2 conversion helpers ─────────────────────────────────────────────
    // These are the only places where System.Numerics.Vector2 ↔ Godot.Vector2
    // conversion occurs.  Keeping them here isolates the impedance mismatch.

    /// <summary>Godot.Vector2 → System.Numerics.Vector2 for MissileSystem calls.</summary>
    private static SysVec ToSysVec(Vector2 v) => new(v.X, v.Y);

    /// <summary>System.Numerics.Vector2 → Godot.Vector2 for spawn calls.</summary>
    private static Vector2 ToGodotVec(SysVec v) => new(v.X, v.Y);
}
