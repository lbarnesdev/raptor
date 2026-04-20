// src/UI/BossHealthBar.cs
// ─────────────────────────────────────────────────────────────────────────────
// Three-segment boss health bar driven by EventBus signals.
//
// Node placement (Level01.tscn):
//   HUD (CanvasLayer)
//   ├── ScoreLabel (Label)
//   └── BossHealthBar (Control)  ← this script
//       ├── Seg1 (ProgressBar)   — Phase 1 HP
//       ├── Seg2 (ProgressBar)   — Phase 2 HP
//       └── Seg3 (ProgressBar)   — Phase 3 HP
//
// Lifecycle:
//   Starts invisible.  BossSpawned → visible; resets all segments.
//   BossHpChanged    → updates the active segment's fill.
//   BossPhaseChanged → greys the completed segment, activates the next.
//   BossDefeated     → hides the bar.
//
// Colour convention:
//   Active segment   — white modulate (1, 1, 1)
//   Completed / not-yet-active segments — grey modulate (0.45, 0.45, 0.45)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.UI;

/// <summary>
/// Three-segment progress bar that tracks the boss's phase-based health.
/// Each segment corresponds to one boss phase.  Only the active segment shows
/// live HP; completed segments are zeroed and greyed; future segments are
/// grey at full until their phase activates.
/// </summary>
public partial class BossHealthBar : Control
{
    // ── Cached node references ────────────────────────────────────────────────

    private ProgressBar _seg1 = null!;
    private ProgressBar _seg2 = null!;
    private ProgressBar _seg3 = null!;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Which phase segment is currently being updated (1–3).</summary>
    private int _currentPhase = 1;

    // ── Colours ───────────────────────────────────────────────────────────────

    /// <summary>Full-brightness tint for the live phase segment.</summary>
    private static readonly Color ActiveModulate = new(1f, 1f, 1f, 1f);

    /// <summary>Dim tint applied to completed and not-yet-active segments.</summary>
    private static readonly Color GreyModulate = new(0.45f, 0.45f, 0.45f, 1f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _seg1 = GetNode<ProgressBar>("Seg1");
        _seg2 = GetNode<ProgressBar>("Seg2");
        _seg3 = GetNode<ProgressBar>("Seg3");

        // Bar stays invisible until BossSpawned fires.
        Visible = false;

        EventBus.Instance.BossSpawned      += OnBossSpawned;
        EventBus.Instance.BossHpChanged    += OnBossHpChanged;
        EventBus.Instance.BossPhaseChanged += OnBossPhaseChanged;
        EventBus.Instance.BossDefeated     += OnBossDefeated;
    }

    public override void _ExitTree()
    {
        // Disconnect before freeing to prevent callbacks into a freed node.
        EventBus.Instance.BossSpawned      -= OnBossSpawned;
        EventBus.Instance.BossHpChanged    -= OnBossHpChanged;
        EventBus.Instance.BossPhaseChanged -= OnBossPhaseChanged;
        EventBus.Instance.BossDefeated     -= OnBossDefeated;
    }

    // ── EventBus handlers ─────────────────────────────────────────────────────

    private void OnBossSpawned()
    {
        _currentPhase = 1;

        // Seg1 is active (white); Seg2/Seg3 are pending (grey, full).
        // Boss.StartBoss() immediately follows with BossHpChanged(20, 20) which
        // sets Seg1's MaxValue/Value to the real phase HP.
        foreach (var seg in new[] { _seg1, _seg2, _seg3 })
        {
            seg.Value    = seg.MaxValue;   // show full until BossHpChanged arrives
            seg.Modulate = GreyModulate;
        }
        _seg1.Modulate = ActiveModulate;

        Visible = true;
    }

    /// <summary>
    /// Fired after every weak-point hit.  Drives only the active segment's fill.
    /// <paramref name="current"/> and <paramref name="max"/> are expressed in
    /// the current phase's HP scale (e.g. 20/20, 25/25, 30/30 at phase start).
    /// </summary>
    private void OnBossHpChanged(int current, int max)
    {
        var seg = SegmentForPhase(_currentPhase);
        if (seg is null) return;

        seg.MaxValue = max;
        seg.Value    = current;
    }

    /// <summary>
    /// Fired when the boss advances to a new phase.
    /// Zeroes and greys the just-completed segment, then whites the next one.
    /// <paramref name="phase"/> is 2 or 3 (never 1 — that is the opening state).
    /// </summary>
    private void OnBossPhaseChanged(int phase)
    {
        // Lock in the completed segment as empty + grey.
        var oldSeg = SegmentForPhase(_currentPhase);
        if (oldSeg is not null)
        {
            oldSeg.Value    = 0;
            oldSeg.Modulate = GreyModulate;
        }

        _currentPhase = phase;

        // Light up the incoming phase segment.
        // BossHpChanged fires immediately after this with the new phase's HP,
        // so MaxValue/Value will be corrected within the same frame.
        var newSeg = SegmentForPhase(_currentPhase);
        if (newSeg is not null)
            newSeg.Modulate = ActiveModulate;
    }

    private void OnBossDefeated()
    {
        Visible = false;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private ProgressBar? SegmentForPhase(int phase) => phase switch
    {
        1 => _seg1,
        2 => _seg2,
        3 => _seg3,
        _ => null,
    };
}
