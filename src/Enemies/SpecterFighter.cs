// src/Enemies/SpecterFighter.cs
// ─────────────────────────────────────────────────────────────────────────────
// Su-57 variant with red crystalline alien possession.  PRD F-211–215.
//
// Behavior sequence (async, fire-and-forget from _Ready):
//
//   Entry:
//     → Spawns at screen-right edge moving at DiagonalSpeed (280 px/s) on a
//       random diagonal angle (±30° from horizontal).
//
//   Crossing (2–3 direction changes):
//     → At each segment: flies straight for a random duration (0.3–0.7 s).
//     → At each turn point: drops a SporeCloud at current GlobalPosition.
//     → Fires 1 CrystalSpine per crossing aimed at the player.
//
//   Exit:
//     → After all segments: heads left at DiagonalSpeed.
//     → QueueFree() once off-screen.
//
// Collision (set in SpecterFighter.tscn):
//   collision_layer = 8   (Enemy, layer 4)
//   collision_mask  = 0
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Enemies;

/// <summary>
/// High-speed diagonal enemy that drops SporeCloud hazards at turn points
/// and fires CrystalSpine projectiles.
/// </summary>
public partial class SpecterFighter : BaseEnemy
{
    // ── Exports ──────────────────────────────────────────────────────────────

    /// <summary>PackedScene for the SporeCloud hazard dropped at turn points.</summary>
    [Export] public PackedScene? SporeCloudScene { get; set; }

    // ── Private constants ────────────────────────────────────────────────────

    private const float DiagonalSpeed    = 280f;
    private const float MaxAngleDegrees  = 30f;   // ± deviation from horizontal
    private const float MinSegmentTime   = 0.3f;
    private const float MaxSegmentTime   = 0.7f;
    private const int   MinTurns         = 2;
    private const int   MaxTurns         = 3;

    // ── Private state ────────────────────────────────────────────────────────

    private readonly RandomNumberGenerator _rng = new();

    private Vector2 _currentVelocity = Vector2.Zero;

    private Node2D? _player;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        base._Ready();

        MaxHealth  = 3;
        ScoreValue = 150;

        _player = GetNodeOrNull<Node2D>("/root/Level01/Entities/Player");
        _rng.Randomize();

        // Initial velocity: generally leftward with a random vertical component.
        _currentVelocity = RandomDiagonalVelocity();

        _ = RunBehaviorAsync();
    }

    public override void _PhysicsProcess(double delta)
    {
        MoveAndCollide(_currentVelocity * (float)delta);

        // Free once fully off-screen to the left.
        if (GlobalPosition.X < -200f)
            QueueFree();
    }

    // ── Behavior coroutine ───────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunBehaviorAsync()
    {
        int turns = _rng.RandiRange(MinTurns, MaxTurns);
        bool hasFired = false;

        for (int i = 0; i < turns; i++)
        {
            // Fly on current heading for a random segment duration.
            float segTime = _rng.RandfRange(MinSegmentTime, MaxSegmentTime);
            await ToSignal(GetTree().CreateTimer(segTime), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this)) return;

            // Drop a SporeCloud at the turn point.
            DropSporeCloud();

            // Fire one crystal spine on the first turn (aimed at player).
            if (!hasFired)
            {
                FireCrystalSpine();
                hasFired = true;
            }

            // Pick a new diagonal heading, preserving leftward X direction.
            _currentVelocity = RandomDiagonalVelocity();
        }

        // After all turns, point directly left and exit.
        _currentVelocity = new Vector2(-DiagonalSpeed, 0f);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Vector2 RandomDiagonalVelocity()
    {
        float angleDeg = _rng.RandfRange(-MaxAngleDegrees, MaxAngleDegrees);
        float angleRad = Mathf.DegToRad(angleDeg);
        // Always leftward (negative X), vertical component from angle.
        return new Vector2(-Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * DiagonalSpeed;
    }

    private void DropSporeCloud()
    {
        if (SporeCloudScene is null) return;

        // Spawn in the EffectsContainer so it persists independently of this enemy.
        var container = GetNodeOrNull<Node2D>("/root/Level01/EffectsContainer");
        if (container is null) return;

        var cloud = SporeCloudScene.Instantiate<Node2D>();
        cloud.GlobalPosition = GlobalPosition;
        container.AddChild(cloud);
    }

    private void FireCrystalSpine()
    {
        var pool = ProjectilePool.Instance;
        if (pool is null) return;

        // Aim at the player's current position; fall back to default leftward.
        Vector2 velocity = CrystalSpine.DefaultVelocity;
        if (_player is not null && IsInstanceValid(_player))
        {
            Vector2 dir = (_player.GlobalPosition - GlobalPosition).Normalized();
            velocity = dir * Mathf.Abs(CrystalSpine.DefaultVelocity.X);
        }

        pool.Get(ProjectileType.CrystalSpine, GlobalPosition, velocity);
        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.EnemyShoot);
    }
}
