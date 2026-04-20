// src/World/LevelDirector.cs
// ─────────────────────────────────────────────────────────────────────────────
// Reads level_01_waves.json, ticks the LevelDirectorTimeline each frame, and
// dispatches spawning and camera events.
//
// Node placement (Level01.tscn):
//   Level01
//   └── LevelDirector (Node)  ← this script
//
// Data file:
//   res://data/level_01_waves.json — loaded once in _Ready via FileAccess.
//
// Supported TimelineEventType values (Slices 4–5):
//   SpawnWave          — instantiates BasicEnemyScene N times in a formation
//   StopScroll         — sets ScrollCamera.IsStopped = true
//   DespawnAllEnemies  — QueueFrees every node in the "enemies" group
//   SpawnBoss          — instantiates BossScene, positions it, calls StartBoss()
//
// RegisterCheckpoint — implemented in Slice 7 (TICKET-703).
// CrossfadeMusic     — recognised but no-op'd with a warning until Slice 8.
//
// Formations (SpawnWave "formation" param):
//   "line" — N enemies at equal vertical spacing, same X
//   "V"    — wedge pointing right; each row one step further right and wider
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Boss;
using Raptor.Core;
using Raptor.Logic;

namespace Raptor.World;

/// <summary>
/// Drives level pacing by ticking a <see cref="LevelDirectorTimeline"/> and
/// responding to its events: enemy wave spawning, scroll control, and (in later
/// slices) music crossfades and boss triggers.
/// </summary>
public partial class LevelDirector : Node
{
    // ── Exported scene references (set in Godot Inspector) ───────────────────

    /// <summary>
    /// Scene to instantiate for SpawnWave events with <c>"enemy": "BasicEnemy"</c>.
    /// Assign <c>scenes/enemies/BasicEnemy.tscn</c> in the Inspector.
    /// </summary>
    [Export] public PackedScene BasicEnemyScene { get; set; } = null!;

    /// <summary>
    /// Scene to instantiate when the <c>SpawnBoss</c> timeline event fires at t=162s.
    /// Assign <c>scenes/boss/Boss.tscn</c> in the Inspector.
    /// </summary>
    [Export] public PackedScene BossScene { get; set; } = null!;

    // ── Cached node references ────────────────────────────────────────────────

    private ScrollCamera _camera         = null!;
    private Node2D       _enemyContainer = null!;

    // ── Timeline ─────────────────────────────────────────────────────────────

    private LevelDirectorTimeline _timeline = null!;

    // ── Viewport size (cached once) ───────────────────────────────────────────

    private float _viewHalfWidth;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _camera         = GetNode<ScrollCamera>("/root/Level01/ScrollCamera");
        _enemyContainer = GetNode<Node2D>("/root/Level01/EnemyContainer");
        _viewHalfWidth  = GetViewport().GetVisibleRect().Size.X / 2f;

        EventBus.Instance.BossSpawnBonusWave += OnBossSpawnBonusWave;

        var json = Godot.FileAccess.GetFileAsString("res://data/level_01_waves.json");
        if (string.IsNullOrEmpty(json))
        {
            GD.PushError("LevelDirector: could not read res://data/level_01_waves.json");
            return;
        }

        _timeline = LevelDirectorTimeline.FromJson(json);
    }

    public override void _ExitTree()
    {
        EventBus.Instance.BossSpawnBonusWave -= OnBossSpawnBonusWave;
    }

    // ── Bonus wave handler (Phase 2) ──────────────────────────────────────────

    private void OnBossSpawnBonusWave()
    {
        // Spawn a 2-enemy line formation just off the right edge of the screen,
        // matching the same formation logic used by the JSON timeline SpawnWave.
        SpawnWave(new Dictionary<string, object>
        {
            ["count"]     = 2,
            ["formation"] = "line",
            ["y"]         = 540,   // vertical screen centre
            ["spread"]    = 200,
        });
    }

    // ── Per-frame tick ────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_timeline is null) return;

        // Update() returns a lazy iterator — enumerate it fully this frame.
        foreach (var ev in _timeline.Update(delta))
            Dispatch(ev);
    }

    // ── Event dispatch ────────────────────────────────────────────────────────

    private void Dispatch(TimelineEvent ev)
    {
        switch (ev.Type)
        {
            case TimelineEventType.SpawnWave:
                SpawnWave(ev.Params);
                break;

            case TimelineEventType.StopScroll:
                _camera.IsStopped = true;
                break;

            case TimelineEventType.DespawnAllEnemies:
                foreach (Node n in GetTree().GetNodesInGroup("enemies"))
                    n.QueueFree();
                break;

            case TimelineEventType.SpawnBoss:
                SpawnBoss();
                break;

            case TimelineEventType.RegisterCheckpoint:
                CheckpointManager.Instance.RegisterCheckpoint(
                    GetInt(ev.Params, "index", 0));
                break;

            // ── Future slices ─────────────────────────────────────────────────
            case TimelineEventType.CrossfadeMusic:
                GD.Print($"LevelDirector: {ev.Type} not yet implemented (future slice).");
                break;
        }
    }

    // ── Wave spawning ─────────────────────────────────────────────────────────

    private void SpawnWave(IReadOnlyDictionary<string, object> p)
    {
        int    count     = GetInt  (p, "count",     1);
        float  yCenter   = GetFloat(p, "y",         540f);
        float  spread    = GetFloat(p, "spread",    300f);
        string formation = GetStr  (p, "formation", "line");

        // Enemies spawn just past the right edge of the current camera view.
        float spawnX = _camera.GlobalPosition.X + _viewHalfWidth + 200f;

        var positions = BuildFormation(formation, count, spawnX, yCenter, spread);

        foreach (var pos in positions)
        {
            if (BasicEnemyScene is null)
            {
                GD.PushWarning("LevelDirector: BasicEnemyScene not assigned in Inspector.");
                return;
            }

            var enemy = BasicEnemyScene.Instantiate<Node2D>();
            _enemyContainer.AddChild(enemy);
            enemy.GlobalPosition = pos;
        }
    }

    // ── Boss spawning ─────────────────────────────────────────────────────────

    private void SpawnBoss()
    {
        if (BossScene is null)
        {
            GD.PushWarning("LevelDirector: BossScene not assigned in Inspector.");
            return;
        }

        // Instantiate as the concrete Boss type so nameof resolves against
        // the class, not the Raptor.Boss namespace (which shares the name).
        var boss = BossScene.Instantiate<Raptor.Boss.Boss>();
        _enemyContainer.AddChild(boss);

        // Place the boss in the right-centre of the current camera view.
        // Camera Y stays constant (ScrollCamera only moves X), so
        // _camera.GlobalPosition.Y is always the screen vertical midpoint.
        boss.GlobalPosition = new Vector2(
            _camera.GlobalPosition.X + _viewHalfWidth * 0.6f,
            _camera.GlobalPosition.Y);

        // Defer StartBoss() by one frame so Boss._Ready() has run and
        // Boss.Instance is valid before the phase activation begins.
        boss.CallDeferred(nameof(boss.StartBoss));

        GD.Print($"LevelDirector: Boss spawned at {boss.GlobalPosition}.");
    }

    // ── Formation builders ────────────────────────────────────────────────────

    /// <summary>
    /// Returns world positions for <paramref name="count"/> enemies.
    ///
    /// "line" — all at the same X, evenly distributed vertically around yCenter.
    ///
    /// "V"    — front enemy at X; each subsequent pair is one step further right
    ///          and spread one position wider, forming a V pointing at the player.
    /// </summary>
    private static List<Vector2> BuildFormation(
        string formation, int count, float x, float yCenter, float spread)
    {
        var positions = new List<Vector2>(count);

        if (count <= 1)
        {
            positions.Add(new Vector2(x, yCenter));
            return positions;
        }

        switch (formation)
        {
            case "V":
            {
                // Row 0 (front): 1 enemy at (x, yCenter).
                // Row r: 2 enemies at x + r*80, yCenter ± r * (spread / (count/2)).
                positions.Add(new Vector2(x, yCenter));
                float rowStep = spread / Mathf.Max(1, count / 2);
                for (int r = 1; positions.Count < count; r++)
                {
                    float rowX = x + r * 90f;
                    float rowY = r * rowStep;
                    if (positions.Count < count)
                        positions.Add(new Vector2(rowX, yCenter - rowY));
                    if (positions.Count < count)
                        positions.Add(new Vector2(rowX, yCenter + rowY));
                }
                break;
            }

            default: // "line"
            {
                float step = spread / (count - 1);
                float startY = yCenter - spread / 2f;
                for (int i = 0; i < count; i++)
                    positions.Add(new Vector2(x, startY + i * step));
                break;
            }
        }

        return positions;
    }

    // ── JSON param helpers ────────────────────────────────────────────────────
    // LevelDirectorTimeline.ConvertNumber produces int for whole numbers and
    // double for fractional ones.  These helpers abstract away the ambiguity.

    private static float GetFloat(IReadOnlyDictionary<string, object> p, string key, float def)
    {
        if (!p.TryGetValue(key, out var v)) return def;
        return v switch { int i => (float)i, double d => (float)d, _ => def };
    }

    private static int GetInt(IReadOnlyDictionary<string, object> p, string key, int def)
    {
        if (!p.TryGetValue(key, out var v)) return def;
        return v switch { int i => i, double d => (int)d, _ => def };
    }

    private static string GetStr(IReadOnlyDictionary<string, object> p, string key, string def)
    {
        if (!p.TryGetValue(key, out var v)) return def;
        return v is string s ? s : def;
    }
}
