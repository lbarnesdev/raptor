// src/Boss/WeakPoint.cs
// ─────────────────────────────────────────────────────────────────────────────
// Collidable weak point exposed on the boss during active phases.
//
// Node placement (Boss.tscn — arch doc Section 3.3):
//   Boss (Node2D)
//   ├── TinyHandNode (Node2D)
//   │   └── WeakPoint_P1 (Area2D)  ← this script, PhaseIndex = 1
//   ├── ToupeeNode (Node2D)
//   │   └── WeakPoint_P2 (Area2D)  ← this script, PhaseIndex = 2
//   └── StatueNode (Node2D)
//       └── WeakPoint_P3 (Area2D)  ← this script, PhaseIndex = 3
//
// Collision setup (set in Boss.tscn Inspector, NOT in code):
//   collision_layer = 64   (layer 7 = BossWeakPoint = 2^6)
//   collision_mask  = 16   (layer 5 = PlayerProjectile = 2^4)
//
// Lifecycle:
//   All three WeakPoint nodes start with Monitoring = false and Visible = false.
//   Boss.cs calls SetActive(true) on the correct phase's node when that phase
//   becomes active, and SetActive(false) again when the phase ends or a
//   transition begins.
//
// Hit routing:
//   WeakPoint does not apply damage itself — it is a pure detector.  Every
//   validated hit is forwarded to Boss.Instance.OnWeakPointHit() which owns
//   the BossPhaseHealth instance and the phase transition logic.  This keeps
//   damage accounting in one place and makes WeakPoint re-usable without
//   knowledge of the health system internals.
//
// Missile detection:
//   The projectile pool returns Missile instances that extend Projectile.
//   "body is Missile" is a simple type check — no tag or string matching needed.
//
// HitParticles:
//   Optional PackedScene assigned in the Inspector.  If assigned, one instance
//   is created at GlobalPosition on each valid hit and added to Level01's
//   EffectsContainer.  The particle scene is responsible for its own lifetime
//   (QueueFree after its animation completes).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Projectiles; // Projectile, Missile

namespace Raptor.Boss;

/// <summary>
/// An <see cref="Area2D"/> child on the boss that intercepts player projectiles
/// during its assigned phase.  Forwards every validated hit to
/// <see cref="Raptor.Boss.Boss.OnWeakPointHit"/> and optionally spawns a hit-
/// particle effect at the impact point.
/// </summary>
public partial class WeakPoint : Area2D
{
    // ── Inspector properties ─────────────────────────────────────────────────

    /// <summary>
    /// 1-indexed phase number this weak point belongs to.
    /// Set in the Godot Inspector on each of the three WeakPoint nodes:
    /// <c>WeakPoint_P1 = 1</c>, <c>WeakPoint_P2 = 2</c>, <c>WeakPoint_P3 = 3</c>.
    /// </summary>
    [Export] public int PhaseIndex { get; set; } = 1;

    /// <summary>
    /// Optional particle / VFX scene instantiated at <see cref="Node.GlobalPosition"/>
    /// on each valid hit.  Leave unassigned for no impact effect (safe — guarded
    /// with a null check before instantiation).
    /// </summary>
    [Export] public PackedScene? HitParticles { get; set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Wire the Area2D body_entered signal to the local handler.
        // Projectiles are Area2D nodes, not CharacterBody2D/StaticBody2D, so
        // they raise AreaEntered rather than BodyEntered.
        // Use BodyEntered if the projectile extends PhysicsBody2D; use AreaEntered
        // if it extends Area2D.  PlasmaBolt/Missile extend Area2D → AreaEntered.
        AreaEntered += OnAreaEntered;

        // All weak points start fully disabled.  Boss.cs enables the correct one
        // when the corresponding phase becomes active.
        Monitoring = false;
        Visible    = false;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enable or disable this weak point.
    /// <para>
    /// Sets both <see cref="Area2D.Monitoring"/> (collision detection) and
    /// <see cref="CanvasItem.Visible"/> (sprite) together so the visual and the
    /// hitbox are always in sync.
    /// </para>
    /// </summary>
    /// <param name="active">
    /// <c>true</c>  — hitbox active, sprite visible (phase is live).<br/>
    /// <c>false</c> — hitbox inactive, sprite hidden (phase not yet started
    ///               or transition in progress).
    /// </param>
    public void SetActive(bool active)
    {
        Monitoring = active;
        Visible    = active;
    }

    // ── Signal handler ────────────────────────────────────────────────────────

    /// <summary>
    /// Called when another <see cref="Area2D"/> enters this weak point's
    /// collision shape.  Only acts on <see cref="Projectile"/> instances so
    /// stray non-projectile areas (e.g. SporeCloud) are silently ignored.
    /// </summary>
    private void OnAreaEntered(Area2D area)
    {
        // Only react to player projectiles (PlasmaBolt, Missile).
        if (area is not Projectile projectile)
            return;

        bool isMissile = projectile is Missile;

        // Return the projectile to its pool through the guarded ReturnToPool()
        // path so the _returned flag is set.  This prevents a double-return if
        // the off-screen cull in Projectile._Process() also fires this frame.
        // (ReturnToPool() was promoted from protected → public for exactly this use.)
        projectile.ReturnToPool();

        // Spawn optional hit VFX at the impact world position.
        SpawnHitParticles();

        // Forward the hit to Boss.  Boss.OnWeakPointHit guards against
        // wrong-phase hits and transition-locked frames, so WeakPoint
        // does not need to replicate that logic here.
        Boss.Instance?.OnWeakPointHit(PhaseIndex, isMissile);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates <see cref="HitParticles"/> at the current world position
    /// and adds it to the level's <c>EffectsContainer</c> so its lifetime is
    /// independent of the boss node.  No-op if <see cref="HitParticles"/> is
    /// not assigned.
    /// </summary>
    private void SpawnHitParticles()
    {
        if (HitParticles is null) return;

        var fx = HitParticles.Instantiate<Node2D>();
        fx.GlobalPosition = GlobalPosition;

        // Add to EffectsContainer if it exists; fall back to the scene root.
        // This keeps transient effects out of the boss subtree.
        var container = GetTree().Root.FindChild("EffectsContainer", true, false) as Node;
        if (container is not null)
            container.AddChild(fx);
        else
            GetTree().Root.AddChild(fx);
    }
}
