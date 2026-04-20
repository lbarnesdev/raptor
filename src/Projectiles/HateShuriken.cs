// src/Projectiles/HateShuriken.cs
// ─────────────────────────────────────────────────────────────────────────────
// Boss Phase 3 projectile — a spinning shuriken that reflects off the
// left/right screen edges once before despawning.
//
// Fired by Phase3Controller during the Statue swing attack (PRD F-434).
//
// Reflection rules:
//   First horizontal boundary crossing → negate X velocity (reflect), nudge
//   back inside bounds to avoid retriggering in the same frame.
//   Second horizontal crossing OR any vertical crossing → ReturnToPool.
//
// Content toggle:
//   UseGenericSprite (set by Phase3Controller from GameSettings) hides the
//   WarningSprite and shows GenericSprite instead.  Both are placeholder
//   polygons in Slice 6; real sprites added in Slice 11.
//
// Node placement (HateShuriken.tscn):
//   HateShuriken (Area2D, extends Projectile)
//   ├── CollisionShape2D  (CircleShape2D r=8)
//   ├── GenericSprite     (Polygon2D cross, silver) — shown when content off
//   └── WarningSprite     (Polygon2D cross, dark)   — shown when content on
//
// Collision setup (set in .tscn):
//   collision_layer = 32   (EnemyProjectile = layer 6 = 2^5)
//   collision_mask  =  6   (Player=2 + PlayerShield=4)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.Projectiles;

/// <summary>
/// Spinning shuriken that bounces off horizontal screen edges once.
/// Overrides <c>_Process</c> to implement reflection in place of the base
/// class's straight off-screen cull.
/// </summary>
public partial class HateShuriken : Projectile
{
    // ── Content toggle ────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c> the generic (non-offensive) sprite is shown regardless
    /// of <see cref="GameSettings.ContentWarningEnabled"/>.
    /// Set by <see cref="Raptor.Boss.Phase3Controller"/> before activation.
    /// </summary>
    [Export] public bool UseGenericSprite { get; set; } = false;

    // ── Reflection state ──────────────────────────────────────────────────────

    /// <summary>
    /// Number of times this shuriken has bounced off a horizontal boundary.
    /// Reset to 0 by <see cref="OnActivation"/> each time the pool re-uses it.
    /// </summary>
    private int _reflectCount = 0;

    // ── Cached viewport size ──────────────────────────────────────────────────

    // Mirrored from base (base caches it privately; we need our own copy
    // because we're overriding _Process and not calling base).
    private Vector2 _viewHalfSize;

    private const float BoundaryMargin = 64f;   // extra slack beyond visible edge

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        base._Ready();   // wires BodyEntered → OnHit
        _viewHalfSize = GetViewportRect().Size / 2f;
        ApplySpriteToggle();
    }

    /// <summary>
    /// Called by <see cref="ProjectilePool"/> just before each activation.
    /// Resets the reflect counter so a recycled shuriken starts fresh.
    /// </summary>
    protected override void OnActivation()
    {
        _reflectCount = 0;
        ApplySpriteToggle();
    }

    // ── Off-screen handling (overrides base cull with reflection) ─────────────

    /// <summary>
    /// Replaces the base <c>_Process</c> cull.  Reflects X velocity on the
    /// first horizontal boundary hit; despawns on the second hit or any
    /// vertical boundary hit.
    /// </summary>
    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera2D();
        if (cam is null) return;

        var c = cam.GlobalPosition;

        float leftBound   = c.X - _viewHalfSize.X - BoundaryMargin;
        float rightBound  = c.X + _viewHalfSize.X + BoundaryMargin;
        float topBound    = c.Y - _viewHalfSize.Y - BoundaryMargin;
        float bottomBound = c.Y + _viewHalfSize.Y + BoundaryMargin;

        bool hitHoriz = GlobalPosition.X < leftBound || GlobalPosition.X > rightBound;
        bool hitVert  = GlobalPosition.Y < topBound  || GlobalPosition.Y > bottomBound;

        if (hitHoriz)
        {
            if (_reflectCount == 0)
            {
                _reflectCount++;
                Velocity = new Vector2(-Velocity.X, Velocity.Y);

                // Nudge back inside so this same check doesn't re-trigger
                // on the very next frame while the shuriken is still outside.
                float clampedX = Mathf.Clamp(
                    GlobalPosition.X,
                    leftBound  + 2f,
                    rightBound - 2f);
                GlobalPosition = new Vector2(clampedX, GlobalPosition.Y);
            }
            else
            {
                ReturnToPool();
            }
        }
        else if (hitVert)
        {
            ReturnToPool();
        }
    }

    // ── Hit handling ──────────────────────────────────────────────────────────

    public override void OnHit(Node body)
    {
        if (body.HasMethod("TakeHit"))
            body.Call("TakeHit", 1);

        ReturnToPool();
    }

    // ── Content toggle helper ─────────────────────────────────────────────────

    private void ApplySpriteToggle()
    {
        bool contentOn  = GameSettings.Instance?.ContentWarningEnabled ?? false;
        bool showGeneric = UseGenericSprite || !contentOn;

        GetNodeOrNull<Node2D>("GenericSprite")?.Set("visible", showGeneric);
        GetNodeOrNull<Node2D>("WarningSprite")?.Set("visible", !showGeneric);
    }
}
