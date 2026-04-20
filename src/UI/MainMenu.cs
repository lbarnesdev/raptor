// src/UI/MainMenu.cs
// ─────────────────────────────────────────────────────────────────────────────
// Title screen — the application's entry point once project.godot is updated
// to set MainMenu.tscn as run/main_scene.
//
// Node placement (MainMenu.tscn):
//   MainMenu (CanvasLayer)
//   ├── Background (ColorRect)  — dark space-blue fill
//   ├── TitleLabel (Label)      — "RAPTOR"
//   ├── PlayButton (Button)     — loads Level01.tscn
//   └── QuitButton (Button)     — exits the application
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.UI;

/// <summary>
/// Title screen.  Wires Play → Level01 and Quit → application exit.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    public override void _Ready()
    {
        GetNode<Button>("PlayButton").Pressed += OnPlayPressed;
        GetNode<Button>("QuitButton").Pressed += OnQuitPressed;
    }

    private void OnPlayPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/world/Level01.tscn");
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
