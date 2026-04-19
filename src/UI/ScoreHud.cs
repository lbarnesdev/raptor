// src/UI/ScoreHud.cs
// ─────────────────────────────────────────────────────────────────────────────
// Score display — a Label subscribed to EventBus.ScoreChanged.
//
// Node placement (Level01.tscn):
//   CanvasLayer
//   └── ScoreLabel (Label)  ← this script
//
// The CanvasLayer keeps the label fixed to the screen regardless of the
// ScrollCamera position — standard Godot pattern for HUD elements.
//
// Format:  "SCORE: 1 200  ×3"
//          (commas every 3 digits, multiplier always shown)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.UI;

/// <summary>
/// Listens to <see cref="EventBus.ScoreChangedEventHandler"/> and keeps the
/// score label text in sync.  Also subscribes to no other events — purely
/// a display node.
/// </summary>
public partial class ScoreHud : Label
{
    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        EventBus.Instance.ScoreChanged += OnScoreChanged;
        UpdateDisplay(0, 1);
    }

    public override void _ExitTree()
    {
        // Always disconnect — prevents callbacks into a freed node if the HUD
        // is removed before EventBus (e.g., during scene reload).
        EventBus.Instance.ScoreChanged -= OnScoreChanged;
    }

    // ── Signal handler ────────────────────────────────────────────────────────

    private void OnScoreChanged(int newScore, int multiplier)
        => UpdateDisplay(newScore, multiplier);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateDisplay(int score, int multiplier)
    {
        Text = $"SCORE: {score:N0}  ×{multiplier}";
    }
}
