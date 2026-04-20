// src/Boss/Phase2Controller.cs
// ─────────────────────────────────────────────────────────────────────────────
// Drives Phase 2 attack patterns for The Demagogue (PRD F-423–426).
//
// Node placement (Boss.tscn):
//   Boss (Node2D)
//   └── PhaseControllers (Node2D)
//       └── Phase2Controller (Node)  ← this script
//
// Attack cycles (all run concurrently while active):
//
//   Burst fire (Open/Closed cycle, PRD F-423/424):
//     Open  (default 4 s) — fires 5 FoxBroadcastPulse in a ±45° fan every 2 s.
//     Closed (default 3 s) — hatch invulnerable, no burst fire.
//
//   Radial pulse (PRD F-425):
//     Every 5 s (independent of Open/Closed), fires 8 FoxBroadcastPulse in a
//     full 360° ring from the ToupeeNode origin.
//
//   Bonus wave (PRD F-426):
//     Every 8 s, emits EventBus.BossSpawnBonusWave so LevelDirector spawns
//     a 2-enemy line formation.
//
// SetActive(bool) is called by Boss.cs via duck-typing (HasMethod / Call).
// Setting active=false stops all three async loops cleanly.
//
// Dependencies: Boss.cs, FoxBroadcastPulse.cs, ProjectilePool.cs, EventBus.cs
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Boss;

/// <summary>
/// Controls Phase 2 attack timing: a burst-fire hatch cycle, an independent
/// radial pulse ring, and a periodic bonus enemy wave.
/// </summary>
public partial class Phase2Controller : Node
{
    // ── Exported tuning ───────────────────────────────────────────────────────

    /// <summary>Seconds the toupee hatch stays Open (burst fire active).</summary>
    [Export] public float OpenDuration      { get; set; } = 4f;

    /// <summary>Seconds the toupee hatch stays Closed (invulnerable).</summary>
    [Export] public float ClosedDuration    { get; set; } = 3f;

    /// <summary>Seconds between fan bursts while the hatch is Open.</summary>
    [Export] public float BurstInterval     { get; set; } = 2f;

    /// <summary>Seconds between radial ring pulses (Open/Closed-independent).</summary>
    [Export] public float RadialInterval    { get; set; } = 5f;

    /// <summary>Seconds between bonus enemy-wave signals.</summary>
    [Export] public float BonusWaveInterval { get; set; } = 8f;

    /// <summary>Number of projectiles in each fan burst.</summary>
    [Export] public int   FanCount          { get; set; } = 5;

    /// <summary>Total arc of the fan burst in radians (default ±45° = π/2 total).</summary>
    [Export] public float FanArcRadians     { get; set; } = Mathf.Pi / 2f;

    /// <summary>Number of projectiles in the radial ring.</summary>
    [Export] public int   RingCount         { get; set; } = 8;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool   _active     = false;
    private Node2D? _toupeeNode;    // ToupeeNode in Boss.tscn — fire origin
    private Node2D? _alienNode;     // AlienPassenger in Boss.tscn — radial origin

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _active = false;
    }

    // ── Duck-typed activation interface ──────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Boss.SetControllerActive"/> on phase entry/exit.
    /// Starts or stops all three attack loops.
    /// </summary>
    public void SetActive(bool active)
    {
        _active = active;

        if (active)
        {
            _toupeeNode = Boss.Instance?.GetNodeOrNull<Node2D>("ToupeeNode");
            _alienNode  = Boss.Instance?.GetNodeOrNull<Node2D>("AlienPassenger");

            RunBurstCycle();
            RunRadialPulse();
            RunBonusWave();
        }
        // If active=false the while (_active) guards in each loop exit naturally.
    }

    // ── Burst fire — Open/Closed hatch cycle ─────────────────────────────────

    private async void RunBurstCycle()
    {
        while (_active && IsInstanceValid(this))
        {
            // ── Open phase ────────────────────────────────────────────────────
            float openRemaining = OpenDuration;

            while (openRemaining > 0f && _active && IsInstanceValid(this))
            {
                FireFanBurst();

                float wait = Mathf.Min(BurstInterval, openRemaining);
                await ToSignal(
                    GetTree().CreateTimer(wait),
                    SceneTreeTimer.SignalName.Timeout);

                if (!IsInstanceValid(this)) return;
                openRemaining -= wait;
            }

            if (!_active || !IsInstanceValid(this)) return;

            // ── Closed phase ──────────────────────────────────────────────────
            await ToSignal(
                GetTree().CreateTimer(ClosedDuration),
                SceneTreeTimer.SignalName.Timeout);

            if (!IsInstanceValid(this)) return;
        }
    }

    /// <summary>
    /// Fires <see cref="FanCount"/> pulses spread evenly across
    /// <see cref="FanArcRadians"/> centred on the left (π radians) direction.
    /// </summary>
    private void FireFanBurst()
    {
        if (ProjectilePool.Instance is null) return;

        Vector2 origin = _toupeeNode?.GlobalPosition
                         ?? Boss.Instance?.GlobalPosition
                         ?? Vector2.Zero;

        float startAngle = Mathf.Pi - FanArcRadians / 2f;
        float step       = FanCount > 1 ? FanArcRadians / (FanCount - 1) : 0f;

        for (int i = 0; i < FanCount; i++)
        {
            float angle = startAngle + i * step;
            var   vel   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                          * FoxBroadcastPulse.DefaultSpeed;

            ProjectilePool.Instance.Get(ProjectileType.FoxBroadcastPulse, origin, vel);
        }
    }

    // ── Radial pulse — independent ring timer ────────────────────────────────

    private async void RunRadialPulse()
    {
        // First pulse fires after one full interval to avoid clustering with
        // the initial burst-fire on phase entry.
        await ToSignal(
            GetTree().CreateTimer(RadialInterval),
            SceneTreeTimer.SignalName.Timeout);

        while (_active && IsInstanceValid(this))
        {
            FireRadialRing();

            await ToSignal(
                GetTree().CreateTimer(RadialInterval),
                SceneTreeTimer.SignalName.Timeout);

            if (!IsInstanceValid(this)) return;
        }
    }

    /// <summary>
    /// Fires <see cref="RingCount"/> pulses evenly spaced around the full 360°
    /// from the AlienPassenger node (the source of the radial "broadcast").
    /// </summary>
    private void FireRadialRing()
    {
        if (ProjectilePool.Instance is null) return;

        Vector2 origin = _alienNode?.GlobalPosition
                         ?? Boss.Instance?.GlobalPosition
                         ?? Vector2.Zero;

        float step = Mathf.Tau / RingCount;   // Mathf.Tau = 2π

        for (int i = 0; i < RingCount; i++)
        {
            float angle = i * step;
            var   vel   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                          * FoxBroadcastPulse.DefaultSpeed;

            ProjectilePool.Instance.Get(ProjectileType.FoxBroadcastPulse, origin, vel);
        }
    }

    // ── Bonus wave — independent enemy spawn timer ───────────────────────────

    private async void RunBonusWave()
    {
        await ToSignal(
            GetTree().CreateTimer(BonusWaveInterval),
            SceneTreeTimer.SignalName.Timeout);

        while (_active && IsInstanceValid(this))
        {
            EventBus.Instance.EmitSignal(EventBus.SignalName.BossSpawnBonusWave);

            await ToSignal(
                GetTree().CreateTimer(BonusWaveInterval),
                SceneTreeTimer.SignalName.Timeout);

            if (!IsInstanceValid(this)) return;
        }
    }
}
