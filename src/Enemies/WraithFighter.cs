// src/Enemies/WraithFighter.cs
// ─────────────────────────────────────────────────────────────────────────────
// J-20 variant with alien bioluminescent possession.  PRD F-201–206.
//
// Behavior sequence (async, driven by _Ready via a fire-and-forget Task):
//
//   FormationHold (2 s)
//     → moves leftward at 80 px/s while drifting toward target formation Y
//
//   AttackRun (1.5 s)
//     → Tween arcs toward player Y position
//     → fires 1–2 PlasmaBlob mid-arc
//
//   Fleeing
//     → exits left at 220 px/s
//     → QueueFree() once off-screen left edge
//
// EnemyStateMachine enforces the state sequence.  Timers/awaits are driven
// by this class — the FSM owns no timers.
//
// Collision (set in WraithFighter.tscn):
//   collision_layer = 8   (Enemy, layer 4)
//   collision_mask  = 0   (no collision response needed for movement)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Logic;
using Raptor.Projectiles;

namespace Raptor.Enemies;

/// <summary>
/// Flying enemy that makes a formation approach, arcs at the player firing
/// plasma blobs, then flees left.
/// </summary>
public partial class WraithFighter : BaseEnemy
{
    // ── Exports ──────────────────────────────────────────────────────────────

    /// <summary>Speed during the attack arc in px/s.</summary>
    [Export] public float AttackSpeed { get; set; } = 180f;

    // ── Private constants ────────────────────────────────────────────────────

    private const float FormationDriftSpeed = 80f;     // leftward drift during FormationHold
    private const float FleeSpeed           = 220f;    // exit speed
    private const float FormationHoldTime   = 2.0f;    // seconds in FormationHold before attack
    private const float ArcDuration         = 1.5f;    // seconds for the attack arc Tween

    // ── Private state ────────────────────────────────────────────────────────

    private readonly EnemyStateMachine _fsm = new();
    private readonly RandomNumberGenerator _rng = new();

    // Cached node path to the player — resolved once in _Ready.
    private Node2D? _player;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        base._Ready(); // AddToGroup("enemies"), _health = MaxHealth

        MaxHealth  = 2;
        ScoreValue = 100;

        // Resolve player reference from the known scene-tree path (ADR-004).
        _player = GetNodeOrNull<Node2D>("/root/Level01/Entities/Player");

        _rng.Randomize();

        // Kick off the behavior loop without blocking _Ready.
        _ = RunBehaviorAsync();
    }

    public override void _PhysicsProcess(double delta)
    {
        switch (_fsm.CurrentState)
        {
            case EnemyState.FormationHold:
                // Drift leftward at low speed.
                MoveAndCollide(new Vector2(-FormationDriftSpeed * (float)delta, 0f));
                break;

            case EnemyState.Fleeing:
                // Sprint left; free once off the left edge of the visible world.
                MoveAndCollide(new Vector2(-FleeSpeed * (float)delta, 0f));
                if (GlobalPosition.X < -200f)
                    QueueFree();
                break;

            // AttackRun movement is handled entirely by the Tween — nothing here.
        }
    }

    // ── Behavior coroutine ───────────────────────────────────────────────────

    /// <summary>
    /// Top-level async behavior: FormationHold → AttackRun → Fleeing.
    /// Uses <c>await ToSignal</c> for timer waits so it runs on Godot's main thread.
    /// </summary>
    private async System.Threading.Tasks.Task RunBehaviorAsync()
    {
        // ── Phase 1: FormationHold ───────────────────────────────────────────
        // Drift for 2 seconds; _PhysicsProcess handles lateral movement.
        await ToSignal(GetTree().CreateTimer(FormationHoldTime), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(this)) return;

        // ── Phase 2: AttackRun ───────────────────────────────────────────────
        _fsm.StartAttackRun();

        if (_player is not null && IsInstanceValid(_player))
        {
            float targetY = _player.GlobalPosition.Y;

            // Arc toward the player's current Y using a Tween.
            var tween = CreateTween();
            tween.TweenProperty(this, "global_position:y", targetY, ArcDuration)
                 .SetTrans(Tween.TransitionType.Sine)
                 .SetEase(Tween.EaseType.InOut);

            // Fire 1–2 blobs at a random point mid-arc.
            int shotsToFire = _rng.RandiRange(1, 2);
            double fireDelay = ArcDuration * _rng.RandfRange(0.3f, 0.7f);

            await ToSignal(GetTree().CreateTimer(fireDelay), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this)) return;

            for (int i = 0; i < shotsToFire; i++)
            {
                FirePlasmaBlob();
                if (i < shotsToFire - 1)
                {
                    await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
                    if (!IsInstanceValid(this)) return;
                }
            }

            // Wait for the remainder of the arc to complete.
            double remainingArc = ArcDuration - fireDelay;
            if (remainingArc > 0.0)
            {
                await ToSignal(GetTree().CreateTimer(remainingArc), SceneTreeTimer.SignalName.Timeout);
                if (!IsInstanceValid(this)) return;
            }
        }
        else
        {
            // No player — still wait out the arc duration.
            await ToSignal(GetTree().CreateTimer(ArcDuration), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this)) return;
        }

        // ── Phase 3: Fleeing ─────────────────────────────────────────────────
        _fsm.StartFlee();
        // _PhysicsProcess takes over at FleeSpeed; QueueFree when off-screen.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void FirePlasmaBlob()
    {
        var pool = ProjectilePool.Instance;
        if (pool is null) return;

        pool.Get(ProjectileType.PlasmaBlob, GlobalPosition, PlasmaBlob.DefaultVelocity);
        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.EnemyShoot);
    }
}
