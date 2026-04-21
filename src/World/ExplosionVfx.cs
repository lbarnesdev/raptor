// src/World/ExplosionVfx.cs
// ─────────────────────────────────────────────────────────────────────────────
// One-shot explosion visual effect.  Instantiated into EffectsContainer by
// BaseEnemy.Die() and Boss.Die() at the enemy's GlobalPosition.
//
// Behavior:
//   1. Plays the "explode" AnimatedSprite2D animation (6 frames from the
//      explosion_small_*/medium_*/large_* sprite sheets, 12 fps).
//   2. After AnimationFinished fires, calls QueueFree().
//   3. Optionally triggers a camera shake of configurable duration/magnitude.
//
// Scene sizes:
//   ExplosionSmall.tscn  — for basic enemies and turrets (magnitude 6, 0.25 s)
//   ExplosionLarge.tscn  — for boss phase transitions   (magnitude 20, 0.8 s)
//
// Camera shake:
//   ExplosionVfx resolves the ScrollCamera at the known path when it first
//   needs it.  If the path is invalid (e.g. running in the editor) the shake
//   is silently skipped rather than crashing.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.World;

/// <summary>
/// One-shot explosion sprite animation that auto-frees itself on completion
/// and optionally shakes the camera.
/// </summary>
public partial class ExplosionVfx : Node2D
{
    // ── Exports ──────────────────────────────────────────────────────────────

    /// <summary>Camera shake duration in seconds (0 = no shake).</summary>
    [Export] public float ShakeDuration  { get; set; } = 0.25f;

    /// <summary>Camera shake magnitude in pixels.</summary>
    [Export] public float ShakeMagnitude { get; set; } = 6f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        var sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (sprite is null)
        {
            QueueFree();
            return;
        }

        // Auto-free when the animation finishes.
        sprite.AnimationFinished += QueueFree;

        // Play the single animation this scene defines (named "explode").
        sprite.Play("explode");

        // Screen shake (if configured).
        if (ShakeDuration > 0f)
        {
            var cam = GetNodeOrNull<ScrollCamera>("/root/Level01/ScrollCamera");
            cam?.Shake(ShakeDuration, ShakeMagnitude);
        }
    }
}
