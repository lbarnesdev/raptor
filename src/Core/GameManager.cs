// src/Core/GameManager.cs
// ─────────────────────────────────────────────────────────────────────────────
// Global game-state manager — Godot 4 autoload singleton.
//
// Registered in project.godot as:
//   [autoload]
//   GameManager="*res://src/Core/GameManager.cs"
//
// Responsibilities:
//   • Lives tracking and respawn orchestration
//   • Owns the pure-C# ScoreSystem; exposes score mutations via public methods
//     so Godot-layer callers never touch ScoreSystem directly
//   • Drives scene transitions (Game Over, Win, Level restart, Main Menu)
//   • Subscribes to EventBus signals that require game-state side-effects
//
// Dependency on EventBus:
//   GameManager._Ready() runs after EventBus._Ready() because autoloads are
//   initialised in the order they appear in project.godot. Ensure EventBus is
//   listed BEFORE GameManager in the [autoload] block.
//
// EnemyDied wiring note:
//   There is currently no EnemyDied signal on EventBus.  Each enemy calls
//   GameManager.Instance.OnEnemyKilled(baseValue) directly after its death
//   animation completes.  If EnemyDied is added to EventBus in a later ticket,
//   replace the direct call with:
//     EventBus.Instance.EnemyDied += (baseValue) => OnEnemyKilled(baseValue);
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Logic;

namespace Raptor.Core;

/// <summary>
/// Application-wide game-state controller.  Owns lives, score, and scene
/// routing.  Survives scene changes because it is an autoload.
/// </summary>
public partial class GameManager : Node
{
    // ── Singleton accessor ───────────────────────────────────────────────────

    /// <summary>
    /// The single autoloaded instance.  Safe to access from any node after
    /// the autoload phase (i.e., from any node's <c>_Ready()</c> or later).
    /// </summary>
    public static GameManager Instance { get; private set; } = null!;

    // ── Public state ─────────────────────────────────────────────────────────

    /// <summary>Remaining player lives.  Starts at 3; Game Over triggers when it reaches 0.</summary>
    public int Lives { get; private set; } = 3;

    /// <summary>
    /// Set to <c>true</c> if the player defeated the boss before the flee timer
    /// expired.  <c>WinScreen.cs</c> reads this to choose which ending to display.
    /// </summary>
    public bool GoodEnding { get; private set; }

    /// <summary>Pass-through to the owned <see cref="ScoreSystem"/>.</summary>
    public int CurrentScore => _scoreSystem.Total;

    /// <summary>Pass-through to the owned <see cref="ScoreSystem"/>.</summary>
    public int CurrentMultiplier => _scoreSystem.Multiplier;

    // ── Private state ────────────────────────────────────────────────────────

    /// <summary>
    /// Pure-C# score logic.  Lives inside GameManager so it persists across
    /// scene loads (GameManager is an autoload; Level01 is not).
    /// </summary>
    private readonly ScoreSystem _scoreSystem = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        // ── Subscribe to EventBus signals that require game-state side-effects ──
        //
        // EventBus is listed first in [autoload], so EventBus.Instance is valid here.
        EventBus.Instance.PlayerDied    += OnPlayerDied;
        EventBus.Instance.GameOver      += OnGameOver;
        EventBus.Instance.LevelComplete += OnLevelComplete;

        // EnemyDied: wired per-enemy via direct call to OnEnemyKilled() for now.
        // See file header comment for the EventBus migration path.
    }

    // ── EventBus handlers ────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player dies (shield broken and player body hit).
    /// Decrements lives, emits <see cref="EventBus.LivesChangedEventHandler"/>,
    /// and either respawns the player or triggers Game Over.
    /// </summary>
    private async void OnPlayerDied()
    {
        Lives -= 1;
        EventBus.Instance.EmitSignal(EventBus.SignalName.LivesChanged, Lives);

        if (Lives <= 0)
        {
            // Emit GameOver — GameManager will receive it via OnGameOver() below.
            EventBus.Instance.EmitSignal(EventBus.SignalName.GameOver);
            return;
        }

        // Brief pause before respawning so the death flash reads clearly.
        await ToSignal(
            GetTree().CreateTimer(2.0f),
            SceneTreeTimer.SignalName.Timeout);

        // Guard: the scene may have changed during the await (e.g. all lives exhausted
        // and GameOver fired while we were waiting — OnGameOver already changed scenes).
        if (!IsInstanceValid(this)) return;

        // Find player and checkpoint manager — both are scene-scoped (not autoloads).
        // GetNodeOrNull returns null if the scene has already been torn down.
        var player = GetNodeOrNull<Raptor.Player.Player>("/root/Level01/Entities/Player");
        if (player is null) return;

        Vector2 respawnPos = Raptor.World.CheckpointManager.Instance.GetRespawnPosition();
        player.Respawn(respawnPos);
    }

    /// <summary>
    /// Called when all lives are exhausted.  Waits 2 seconds then loads the
    /// Game Over screen.
    /// </summary>
    private async void OnGameOver()
    {
        await ToSignal(
            GetTree().CreateTimer(2.0f),
            SceneTreeTimer.SignalName.Timeout);

        GetTree().ChangeSceneToFile("res://scenes/ui/GameOverScreen.tscn");
    }

    /// <summary>
    /// Called when the level ends (boss defeated or flee-timer expired).
    /// Stores the ending flag so <c>WinScreen.cs</c> can read it after the
    /// scene transition.
    /// </summary>
    private void OnLevelComplete(bool goodEnding)
    {
        GoodEnding = goodEnding;
        GetTree().ChangeSceneToFile("res://scenes/ui/WinScreen.tscn");
    }

    // ── Score API (called by Godot-layer nodes) ───────────────────────────────

    /// <summary>
    /// Call this from <c>BaseEnemy.Die()</c> (or from an EnemyDied EventBus
    /// handler once that signal exists).
    /// Delegates to <see cref="ScoreSystem.AddKill"/> and emits
    /// <see cref="EventBus.ScoreChangedEventHandler"/>.
    /// </summary>
    /// <param name="baseValue">Point value of the killed enemy before multiplier.</param>
    public void OnEnemyKilled(int baseValue)
    {
        _scoreSystem.AddKill(baseValue);
        EventBus.Instance.EmitSignal(
            EventBus.SignalName.ScoreChanged,
            _scoreSystem.Total,
            _scoreSystem.Multiplier);
    }

    /// <summary>
    /// Call this from <c>Player.cs</c> whenever the player takes a hit that
    /// was NOT absorbed by the shield (i.e., shield was Broken).
    /// Resets the score multiplier to ×1 and emits
    /// <see cref="EventBus.ScoreChangedEventHandler"/>.
    /// </summary>
    public void OnPlayerHit()
    {
        _scoreSystem.OnHitTaken();
        EventBus.Instance.EmitSignal(
            EventBus.SignalName.ScoreChanged,
            _scoreSystem.Total,
            _scoreSystem.Multiplier);
    }

    // ── Scene-routing helpers (called from UI scenes) ─────────────────────────

    /// <summary>
    /// Resets game state and reloads Level01.  Called by
    /// <c>GameOverScreen.cs</c> → Retry button.
    /// </summary>
    public static void RestartLevel()
    {
        Instance.Lives = 3;
        Instance._scoreSystem.Reset();
        Instance.GetTree().ChangeSceneToFile("res://scenes/world/Level01.tscn");
    }

    /// <summary>
    /// Resets game state and loads the Main Menu.  Called by
    /// <c>GameOverScreen.cs</c> → Menu button and
    /// <c>WinScreen.cs</c> → Menu button.
    /// </summary>
    public static void GoToMainMenu()
    {
        Instance.Lives = 3;
        Instance._scoreSystem.Reset();
        Instance.GetTree().ChangeSceneToFile("res://scenes/ui/MainMenu.tscn");
    }
}
