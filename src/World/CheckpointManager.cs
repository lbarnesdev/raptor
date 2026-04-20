// src/World/CheckpointManager.cs
// ─────────────────────────────────────────────────────────────────────────────
// Tracks the furthest checkpoint the player has passed and provides the
// respawn world position when the player dies.
//
// Node placement (Level01.tscn):
//   Level01
//   ├── Checkpoints (Node2D)
//   │   ├── Checkpoint_Act1  (Marker2D  x=300,   y=540)  — index 0, level start
//   │   ├── Checkpoint_Act2  (Marker2D  x=9600,  y=540)  — index 1, t≈80s
//   │   └── Checkpoint_Boss  (Marker2D  x=19200, y=540)  — index 2, t≈160s
//   └── CheckpointManager (Node)   ← this script
//         Checkpoints = [Checkpoint_Act1, Checkpoint_Act2, Checkpoint_Boss]
//
// Registration flow:
//   level_01_waves.json fires   { "type": "RegisterCheckpoint", "index": 1 }
//   LevelDirector.Dispatch  →   CheckpointManager.Instance.RegisterCheckpoint(1)
//   CheckpointManager stores    CurrentCheckpointIndex = 1
//   Player.Die()  →  GameManager.OnPlayerDied()  →  GetRespawnPosition()
//
// Only-advances invariant:
//   RegisterCheckpoint(n) is a no-op if n ≤ CurrentCheckpointIndex.
//   This protects against duplicate JSON entries or out-of-order calls.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.World;

/// <summary>
/// Singleton node that records which checkpoint the player has reached and
/// returns the respawn world position when requested.
/// </summary>
public partial class CheckpointManager : Node
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Set in <see cref="_Ready"/>.  Valid for the lifetime of Level01.tscn.
    /// </summary>
    public static CheckpointManager Instance { get; private set; } = null!;

    // ── Exported ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered array of respawn markers.  Element 0 is the level-start position.
    /// Wire all three <c>Marker2D</c> nodes in the Godot Inspector.
    /// </summary>
    [Export] public Marker2D[] Checkpoints { get; set; } = System.Array.Empty<Marker2D>();

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Index into <see cref="Checkpoints"/> of the furthest checkpoint reached.
    /// Starts at 0 (level-start marker) and only ever increases.
    /// </summary>
    public int CurrentCheckpointIndex { get; private set; } = 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world position the player should respawn at.
    /// Always valid as long as <see cref="Checkpoints"/> is non-empty.
    /// </summary>
    public Vector2 GetRespawnPosition()
    {
        if (Checkpoints.Length == 0)
        {
            GD.PushWarning("CheckpointManager: Checkpoints array is empty — returning origin.");
            return Vector2.Zero;
        }

        return Checkpoints[CurrentCheckpointIndex].GlobalPosition;
    }

    /// <summary>
    /// Records that the player has passed checkpoint <paramref name="index"/>.
    /// Only advances — a lower or equal index is silently ignored, preserving
    /// the "furthest reached" invariant.
    /// </summary>
    /// <param name="index">
    /// Zero-based index into <see cref="Checkpoints"/>.
    /// </param>
    public void RegisterCheckpoint(int index)
    {
        if (index <= CurrentCheckpointIndex)
            return;

        if (index >= Checkpoints.Length)
        {
            GD.PushWarning(
                $"CheckpointManager: RegisterCheckpoint({index}) out of range " +
                $"(have {Checkpoints.Length} checkpoints).");
            return;
        }

        CurrentCheckpointIndex = index;
        GD.Print(
            $"CheckpointManager: checkpoint {index} registered " +
            $"at {Checkpoints[index].GlobalPosition}.");
    }
}
