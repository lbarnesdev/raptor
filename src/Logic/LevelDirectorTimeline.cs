// src/Logic/LevelDirectorTimeline.cs
// ─────────────────────────────────────────────────────────────────────────────
// Time-indexed event schedule consumed by LevelDirector.cs.
//
// ZERO Godot dependencies — compiles with plain .NET 8 and is fully
// testable via xUnit without the engine installed.
//
// Usage in LevelDirector._Process(delta):
//   foreach (var ev in _timeline.Update(delta))
//       Dispatch(ev);
//
// Caller contract:
//   Update() returns a lazy iterator.  It MUST be fully enumerated
//   (foreach / .ToList()) in the same frame it is called, before the
//   next Update() call.  This matches the foreach usage above.
//   Double-enumerating the same Update() result would apply delta twice.
//
// FromJson accepts a raw JSON string (file I/O is the caller's concern —
// LevelDirector.cs reads the file via Godot's FileAccess and passes the
// content string here).
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;

namespace Raptor.Logic;

// ── Public types ──────────────────────────────────────────────────────────────

/// <summary>All event types the timeline can carry.</summary>
public enum TimelineEventType
{
    SpawnWave,
    CrossfadeMusic,
    StopScroll,
    SpawnBoss,
    RegisterCheckpoint,
    DespawnAllEnemies,
}

/// <summary>
/// A single scheduled event.
/// <para>
/// <c>Params</c> carries all JSON fields except <c>time</c> and <c>type</c>.
/// Value types: <c>string</c>, <c>int</c>, <c>double</c>, or <c>bool</c>.
/// </para>
/// </summary>
public record TimelineEvent(
    float TriggerTime,
    TimelineEventType Type,
    IReadOnlyDictionary<string, object> Params);

// ── Timeline ──────────────────────────────────────────────────────────────────

/// <summary>
/// Plays back a sorted list of <see cref="TimelineEvent"/> items against a
/// running elapsed clock.  Call <see cref="Update"/> once per frame.
/// </summary>
public sealed class LevelDirectorTimeline
{
    // ── Private fields ──────────────────────────────────────────────────────

    private readonly List<TimelineEvent> _events;
    private float _elapsed;
    private int   _nextIndex;

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Build a timeline from any sequence of events.
    /// Events are sorted by <see cref="TimelineEvent.TriggerTime"/> ascending
    /// at construction time — the caller does not need to pre-sort.
    /// </summary>
    public LevelDirectorTimeline(IEnumerable<TimelineEvent> events)
    {
        _events = new List<TimelineEvent>(events);
        // Avoid LINQ OrderBy — List.Sort is in-place and allocation-free here.
        _events.Sort(static (a, b) => a.TriggerTime.CompareTo(b.TriggerTime));
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Advance the clock by <paramref name="delta"/> seconds and yield all
    /// events whose <see cref="TimelineEvent.TriggerTime"/> has now elapsed.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="IEnumerable{T}"/> is a lazy iterator.
    /// Always consume it immediately with <c>foreach</c> or <c>.ToList()</c>.
    /// </remarks>
    public IEnumerable<TimelineEvent> Update(double delta)
    {
        _elapsed += (float)delta;
        while (_nextIndex < _events.Count &&
               _events[_nextIndex].TriggerTime <= _elapsed)
        {
            yield return _events[_nextIndex++];
        }
    }

    /// <summary>
    /// Reset the clock to zero and rewind to the first event.
    /// All events will fire again on subsequent <see cref="Update"/> calls.
    /// </summary>
    public void Reset()
    {
        _elapsed   = 0f;
        _nextIndex = 0;
    }

    // ── Factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a JSON string (the content of <c>level_01_waves.json</c>) into a
    /// <see cref="LevelDirectorTimeline"/>.
    /// </summary>
    /// <param name="json">
    /// Raw JSON content.  File I/O is the caller's responsibility —
    /// LevelDirector.cs loads the file via Godot's FileAccess and passes
    /// the string here.
    /// </param>
    /// <example>
    /// Expected JSON shape:
    /// <code>
    /// {
    ///   "scroll_speed": 120,
    ///   "events": [
    ///     { "time": 5.0, "type": "SpawnWave", "enemy": "WraithFighter",
    ///       "count": 3, "formation": "V" },
    ///     { "time": 60.0, "type": "CrossfadeMusic", "track": "music_act2",
    ///       "duration": 2.0 },
    ///     { "time": 160.0, "type": "StopScroll" }
    ///   ]
    /// }
    /// </code>
    /// </example>
    public static LevelDirectorTimeline FromJson(string json)
    {
        using var doc    = JsonDocument.Parse(json);
        var eventsList   = new List<TimelineEvent>();

        foreach (var evObj in doc.RootElement.GetProperty("events").EnumerateArray())
        {
            float             triggerTime = evObj.GetProperty("time").GetSingle();
            string            typeStr     = evObj.GetProperty("type").GetString()!;
            TimelineEventType type        = Enum.Parse<TimelineEventType>(typeStr);

            var parameters = new Dictionary<string, object>();
            foreach (var prop in evObj.EnumerateObject())
            {
                if (prop.Name is "time" or "type")
                    continue;

                parameters[prop.Name] = ConvertJsonValue(prop.Value);
            }

            eventsList.Add(new TimelineEvent(triggerTime, type, parameters));
        }

        return new LevelDirectorTimeline(eventsList);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Convert a <see cref="JsonElement"/> to a plain .NET value.
    /// Numbers: integer if possible, otherwise double.
    /// </summary>
    private static object ConvertJsonValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number => ConvertNumber(el),
        JsonValueKind.True   => (object)true,
        JsonValueKind.False  => (object)false,
        _                    => el.GetRawText(),
    };

    private static object ConvertNumber(JsonElement el) =>
        el.TryGetInt32(out int i) ? (object)i : el.GetDouble();
}
