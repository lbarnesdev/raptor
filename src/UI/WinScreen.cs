// src/UI/WinScreen.cs
// ─────────────────────────────────────────────────────────────────────────────
// Victory screen — displayed by GameManager.OnLevelComplete after the boss
// fight ends (good ending: alien shot; bad ending: alien escaped).
//
// Node placement (WinScreen.tscn):
//   WinScreen (CanvasLayer)
//   ├── Background (ColorRect)      — tinted gold (good) or red (bad)
//   ├── EndingLabel (Label)         — "ALIEN NEUTRALIZED" or "ALIEN ESCAPED"
//   ├── ScoreLabel (Label)          — final score formatted 000000
//   ├── PlayAgainButton (Button)    — restarts Level01
//   └── MenuButton (Button)         — returns to MainMenu
//
// GameManager.GoodEnding is set before the scene transition so it is readable
// in _Ready() without any async delay.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.UI;

/// <summary>
/// Reads <see cref="GameManager.GoodEnding"/> and
/// <see cref="GameManager.CurrentScore"/> in <c>_Ready()</c> to populate
/// the ending variant label, score display, and background tint.
/// </summary>
public partial class WinScreen : CanvasLayer
{
    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        bool good  = GameManager.Instance.GoodEnding;
        int  score = GameManager.Instance.CurrentScore;

        // Ending text.
        GetNode<Label>("EndingLabel").Text =
            good ? "ALIEN NEUTRALIZED" : "ALIEN ESCAPED";

        // Score formatted as six digits with leading zeros.
        GetNode<Label>("ScoreLabel").Text = score.ToString("D6");

        // Background tint: gold for good ending, red for bad.
        GetNode<ColorRect>("Background").Color =
            good ? new Color(0.85f, 0.68f, 0.10f, 0.88f)
                 : new Color(0.75f, 0.10f, 0.10f, 0.88f);

        // Button wiring.
        GetNode<Button>("PlayAgainButton").Pressed += GameManager.RestartLevel;
        GetNode<Button>("MenuButton").Pressed      += GameManager.GoToMainMenu;
    }
}
