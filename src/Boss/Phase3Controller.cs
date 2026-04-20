// src/Boss/Phase3Controller.cs
// ─────────────────────────────────────────────────────────────────────────────
// Drives Phase 3 attack patterns for The Demagogue (PRD F-433–436).
//
// Node placement (Boss.tscn):
//   Boss (Node2D)
//   └── PhaseControllers (Node2D)
//       └── Phase3Controller (Node)  ← this script
//
// Cycle (repeats while active):
//   1. Fire FoxBroadcastPulse spread toward player ("between swings" opening).
//   2. Wait SwingInterval (default 4 s).
//   3. Fire 4–6 HateShuriken toward player with ±20° spread (the swing attack).
//   4. Tween StatueNode.Rotation from 0 → π (180°) over SwingDuration (1.8 s).
//   5. Recovery wait (RecoveryDuration = 0.8 s).
//   6. Shift WeakPoint_P3 position by RandRange(-30, 30) on local Y.
//   7. Loop to 1.
//
// Content warning:
//   HateShuriken.UseGenericSprite is set from GameSettings.ContentWarningEnabled
//   before each spawn so the pool slot picks up the current setting.
//
// SetActive(bool) is called by Boss.cs via duck-typing.
//
// Dependencies: Boss.cs, Phase3Controller ← FoxBroadcastPulse.cs,
//               HateShuriken.cs, ProjectilePool.cs, GameSettings.cs
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Projectiles;

namespace Raptor.Boss;

/// <summary>
/// Controls Phase 3: statue swing tween, shuriken fling, weak point position
/// jitter, and the between-swing broadcast spread.
/// </summary>
public partial class Phase3Controller : Node
{
    // ── Exported tuning ───────────────────────────────────────────────────────

    /// <summary>Seconds between the end of recovery and the next swing.</summary>
    [Export] public float SwingInterval    { get; set; } = 4f;

    /// <summary>Duration of the 180° StatueNode rotation tween.</summary>
    [Export] public float SwingDuration    { get; set; } = 1.8f;

    /// <summary>Seconds of recovery after the tween before the weak point shifts.</summary>
    [Export] public float RecoveryDuration { get; set; } = 0.8f;

    /// <summary>Minimum number of shurikens per swing attack.</summary>
    [Export] public int   ShurikenMin      { get; set; } = 4;

    /// <summary>Maximum number of shurikens per swing attack.</summary>
    [Export] public int   ShurikenMax      { get; set; } = 6;

    /// <summary>
    /// Half-angle of the shuriken fan spread in degrees.
    /// Shurikens are spread evenly ±this many degrees around the player direction.
    /// </summary>
    [Export] public float ShurikenHalfArc  { get; set; } = 20f;

    /// <summary>Speed of each HateShuriken in px/s.</summary>
    [Export] public float ShurikenSpeed    { get; set; } = 260f;

    /// <summary>Number of FoxBroadcastPulse bolts in the between-swing spread.</summary>
    [Export] public int   BroadcastCount   { get; set; } = 3;

    /// <summary>Half-angle of the broadcast spread in degrees.</summary>
    [Export] public float BroadcastHalfArc { get; set; } = 15f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool   _active     = false;
    private Node2D? _statueNode;
    private Node2D? _weakPoint3;
    private Node2D? _alienNode;
    private Node2D? _player;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _active = false;
    }

    // ── Duck-typed activation interface ──────────────────────────────────────

    public void SetActive(bool active)
    {
        _active = active;

        if (active)
        {
            _statueNode = Boss.Instance?.GetNodeOrNull<Node2D>("StatueNode");
            _weakPoint3 = Boss.Instance?.GetNodeOrNull<Node2D>("StatueNode/WeakPoint_P3");
            _alienNode  = Boss.Instance?.GetNodeOrNull<Node2D>("AlienPassenger");
            _player     = GetNodeOrNull<Node2D>("/root/Level01/Entities/Player");

            // Ensure statue starts at rotation 0 for the first tween.
            if (_statueNode is not null)
                _statueNode.Rotation = 0f;

            RunCycle();
        }
    }

    // ── Main attack cycle ─────────────────────────────────────────────────────

    private async void RunCycle()
    {
        while (_active && IsInstanceValid(this))
        {
            // Step 1 — Between-swing FoxBroadcastPulse spread.
            FireBroadcastSpread();

            // Step 2 — Wait before swinging.
            await ToSignal(
                GetTree().CreateTimer(SwingInterval),
                SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || !_active) return;

            // Step 3 — Fire shurikens at player.
            FireShurikens();

            // Step 4 — Tween statue 0 → 180°.
            if (_statueNode is not null)
            {
                _statueNode.Rotation = 0f;
                var tween = CreateTween();
                tween.TweenProperty(_statueNode, "rotation", Mathf.Pi, SwingDuration);

                await ToSignal(tween, Tween.SignalName.Finished);
                if (!IsInstanceValid(this) || !_active) return;

                _statueNode.Rotation = 0f;   // snap back ready for next swing
            }
            else
            {
                // No statue node — wait the equivalent time so timing stays consistent.
                await ToSignal(
                    GetTree().CreateTimer(SwingDuration),
                    SceneTreeTimer.SignalName.Timeout);
                if (!IsInstanceValid(this) || !_active) return;
            }

            // Step 5 — Recovery pause.
            await ToSignal(
                GetTree().CreateTimer(RecoveryDuration),
                SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || !_active) return;

            // Step 6 — Jitter the Phase 3 weak point on local Y.
            ShiftWeakPoint();
        }
    }

    // ── Firing helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Fires <see cref="ShurikenMin"/>–<see cref="ShurikenMax"/> HateShuriken
    /// projectiles in a fan toward the player's current position.
    /// </summary>
    private void FireShurikens()
    {
        if (ProjectilePool.Instance is null || _player is null) return;

        Vector2 origin = _statueNode?.GlobalPosition
                         ?? Boss.Instance?.GlobalPosition
                         ?? Vector2.Zero;

        int count = (int)GD.RandRange(ShurikenMin, ShurikenMax + 1);
        bool useGeneric = !(GameSettings.Instance?.ContentWarningEnabled ?? false);

        FireFan(
            ProjectileType.HateShuriken,
            origin,
            count,
            Mathf.DegToRad(ShurikenHalfArc),
            ShurikenSpeed,
            preFire: p =>
            {
                if (p is HateShuriken hs)
                    hs.UseGenericSprite = useGeneric;
            });
    }

    /// <summary>
    /// Fires <see cref="BroadcastCount"/> FoxBroadcastPulse projectiles in a
    /// narrow spread toward the player — the "between swings" harass fire.
    /// Origin is the AlienPassenger node (thematically: the alien is broadcasting).
    /// </summary>
    private void FireBroadcastSpread()
    {
        if (ProjectilePool.Instance is null || _player is null) return;

        Vector2 origin = _alienNode?.GlobalPosition
                         ?? Boss.Instance?.GlobalPosition
                         ?? Vector2.Zero;

        FireFan(
            ProjectileType.FoxBroadcastPulse,
            origin,
            BroadcastCount,
            Mathf.DegToRad(BroadcastHalfArc),
            FoxBroadcastPulse.DefaultSpeed,
            preFire: null);
    }

    /// <summary>
    /// Fires <paramref name="count"/> projectiles of <paramref name="type"/> in
    /// a fan of total arc <c>2 × halfArcRadians</c> centred on the direction
    /// toward the player.
    /// </summary>
    private void FireFan(
        ProjectileType type,
        Vector2 origin,
        int count,
        float halfArcRadians,
        float speed,
        System.Action<Projectile>? preFire)
    {
        if (_player is null || count <= 0) return;

        Vector2 toPlayer = (_player.GlobalPosition - origin).Normalized();
        if (toPlayer == Vector2.Zero) toPlayer = Vector2.Left;

        float centerAngle = Mathf.Atan2(toPlayer.Y, toPlayer.X);
        float totalArc    = halfArcRadians * 2f;
        float step        = count > 1 ? totalArc / (count - 1) : 0f;

        for (int i = 0; i < count; i++)
        {
            float angle = centerAngle - halfArcRadians + i * step;
            var   vel   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

            var p = ProjectilePool.Instance.Get(type, origin, vel);
            if (p is not null)
                preFire?.Invoke(p);
        }
    }

    // ── Weak point jitter ─────────────────────────────────────────────────────

    /// <summary>
    /// Offsets WeakPoint_P3's local Y position by a random amount in [−30, 30]
    /// so the player cannot predict the exact hit location each phase.
    /// </summary>
    private void ShiftWeakPoint()
    {
        if (_weakPoint3 is null) return;

        float yShift = (float)GD.RandRange(-30.0, 30.0);
        _weakPoint3.Position = new Vector2(
            _weakPoint3.Position.X,
            _weakPoint3.Position.Y + yShift);
    }
}
