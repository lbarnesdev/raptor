// src/UI/GameOverScreen.cs
// ─────────────────────────────────────────────────────────────────────────────
// Game Over screen — loaded by GameManager.OnGameOver when the player
// exhausts all lives.
//
// Node placement (GameOverScreen.tscn):
//   GameOverScreen (CanvasLayer)
//   ├── Background (ColorRect)   — dark red tint
//   ├── TitleLabel (Label)       — "MISSION FAILED"
//   ├── ScoreLabel (Label)       — final score formatted 000000
//   ├── RetryButton (Button)     — resets state and restarts Level01
//   └── MenuButton (Button)      — resets state and returns to MainMenu
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.UI;

/// <summary>
/// Displays the Game Over state with the final score and navigation buttons.
/// </summary>
public partial class GameOverScreen : CanvasLayer
{
    public override void _Ready()
    {
        GetNode<Label>("ScoreLabel").Text =
            GameManager.Instance.CurrentScore.ToString("D6");

        GetNode<Button>("RetryButton").Pressed += GameManager.RestartLevel;
        GetNode<Button>("MenuButton").Pressed  += GameManager.GoToMainMenu;
    }
}
