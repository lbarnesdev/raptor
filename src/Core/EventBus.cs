// src/Core/EventBus.cs
// ─────────────────────────────────────────────────────────────────────────────
// Global event bus — Godot 4 autoload singleton.
//
// Registered in project.godot as:
//   [autoload]
//   EventBus="*res://src/Core/EventBus.cs"
//
// The leading '*' tells Godot to instantiate the class directly rather than
// treating the path as a scene file.
//
// ── Usage ─────────────────────────────────────────────────────────────────────
//
// Emitting a signal (from any node):
//   EventBus.Instance.EmitSignal(EventBus.SignalName.ScoreChanged, newScore, multiplier);
//
// Connecting to a signal (from any node, e.g. in _Ready):
//   EventBus.Instance.ScoreChanged += OnScoreChanged;
//
//   private void OnScoreChanged(int newScore, int multiplier) { ... }
//
// Disconnecting (e.g. in _ExitTree to avoid dangling references):
//   EventBus.Instance.ScoreChanged -= OnScoreChanged;
//
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Core;

/// <summary>
/// Application-wide event bus.  All game systems emit signals here so that
/// emitters and listeners remain fully decoupled — no direct node references
/// are required across subsystem boundaries.
/// </summary>
public partial class EventBus : Node
{
    // ── Singleton accessor ───────────────────────────────────────────────────

    /// <summary>
    /// The single autoloaded instance.  Available from any script after
    /// <c>_Ready()</c> has run on the autoload node (i.e., always safe to use
    /// from any node's <c>_Ready()</c> or later).
    /// </summary>
    public static EventBus Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
    }

    // ── Player signals ───────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the shield transitions between states.
    /// <paramref name="newState"/> is one of <c>"Active"</c>,
    /// <c>"GracePeriod"</c>, <c>"Broken"</c>, or <c>"Recharging"</c>.
    /// </summary>
    [Signal] public delegate void ShieldStateChangedEventHandler(string newState);

    /// <summary>Fired once when the player's remaining lives reach zero.</summary>
    [Signal] public delegate void PlayerDiedEventHandler();

    /// <summary>
    /// Fired whenever the player gains or loses a life.
    /// <paramref name="remaining"/> is the new life count.
    /// </summary>
    [Signal] public delegate void LivesChangedEventHandler(int remaining);

    // ── Weapon signals ───────────────────────────────────────────────────────

    /// <summary>
    /// Fired immediately after the missile weapon fires a salvo.
    /// <paramref name="ammoRemaining"/> reflects the count after decrement.
    /// </summary>
    [Signal] public delegate void MissileFiredEventHandler(int ammoRemaining);

    /// <summary>
    /// Fired when the player picks up a missile ammo pack.
    /// <paramref name="ammoTotal"/> is the new total after the pickup.
    /// </summary>
    [Signal] public delegate void AmmoGainedEventHandler(int ammoTotal);

    // ── Score signals ────────────────────────────────────────────────────────

    /// <summary>
    /// Fired after every kill that changes the score.
    /// <paramref name="newScore"/> is the running total;
    /// <paramref name="multiplier"/> is the current streak multiplier (1–8).
    /// </summary>
    [Signal] public delegate void ScoreChangedEventHandler(int newScore, int multiplier);

    // ── Level signals ────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the level transitions to a new act.
    /// <paramref name="act"/> is 1, 2, or 3.
    /// </summary>
    [Signal] public delegate void ActChangedEventHandler(int act);

    /// <summary>
    /// Fired when the player passes a checkpoint.
    /// <paramref name="index"/> is 0, 1, or 2.
    /// </summary>
    [Signal] public delegate void CheckpointReachedEventHandler(int index);

    /// <summary>
    /// Fired when the player has no lives remaining and the game ends.
    /// Triggers the Game Over screen.
    /// </summary>
    [Signal] public delegate void GameOverEventHandler();

    /// <summary>
    /// Fired when the level finishes.
    /// <paramref name="goodEnding"/> is <c>true</c> if the boss was defeated
    /// before time expired, <c>false</c> for the bad/timeout ending.
    /// </summary>
    [Signal] public delegate void LevelCompleteEventHandler(bool goodEnding);

    // ── Boss signals ─────────────────────────────────────────────────────────

    /// <summary>Fired once when the boss node enters the scene.</summary>
    [Signal] public delegate void BossSpawnedEventHandler();

    /// <summary>
    /// Fired whenever the boss advances to a new combat phase.
    /// <paramref name="phase"/> is 1, 2, or 3.
    /// </summary>
    [Signal] public delegate void BossPhaseChangedEventHandler(int phase);

    /// <summary>
    /// Fired after every hit that changes the boss's health.
    /// Drives the boss HP bar UI.
    /// <paramref name="current"/> and <paramref name="max"/> are both
    /// expressed in the current phase's HP scale.
    /// </summary>
    [Signal] public delegate void BossHpChangedEventHandler(int current, int max);

    /// <summary>
    /// Fired when the boss completes its defeat animation and is removed.
    /// Triggers the level-complete flow.
    /// </summary>
    [Signal] public delegate void BossDefeatedEventHandler();
}
