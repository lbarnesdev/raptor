// tests/Logic/LevelDirectorTimelineTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for LevelDirectorTimeline.
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered (matching ticket spec):
//   1. Single event at t=5.0: not returned at t=4.9, returned at t=5.0
//   2. Two events at same t=10.0: both returned in same Update call
//   3. After all events fired, subsequent Update returns empty
//   4. Reset: rewinds to t=0 and re-fires events from the beginning
//   5. FromJson parses correctly (inline JSON string)
//
// Implementation note:
//   Update() returns a lazy iterator.  All tests call .ToList() to force
//   immediate evaluation — exactly as LevelDirector's foreach does in-engine.
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class LevelDirectorTimelineTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TimelineEvent E(float t, TimelineEventType type,
        Dictionary<string, object>? p = null) =>
        new(t, type, p ?? new Dictionary<string, object>());

    // ── Construction sanity ──────────────────────────────────────────────────

    [Fact]
    public void NewTimeline_UpdateReturnsEmpty_BeforeAnyTime()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        var result = tl.Update(0.0).ToList();

        Assert.Empty(result);
    }

    // ── 1. Single event timing ────────────────────────────────────────────────

    [Fact]
    public void SingleEvent_NotReturnedBeforeTriggerTime()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        var result = tl.Update(4.9).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void SingleEvent_ReturnedAtExactTriggerTime()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        tl.Update(4.9).ToList(); // advance to just before
        var result = tl.Update(0.1).ToList(); // _elapsed = 5.0

        Assert.Single(result);
        Assert.Equal(TimelineEventType.SpawnWave, result[0].Type);
    }

    [Fact]
    public void SingleEvent_ReturnedWhenDeltaOvershoots()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.StopScroll),
        });

        // One big update step that skips past the trigger time
        var result = tl.Update(10.0).ToList();

        Assert.Single(result);
        Assert.Equal(TimelineEventType.StopScroll, result[0].Type);
    }

    [Fact]
    public void SingleEvent_NotReturnedTwice()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnBoss),
        });

        tl.Update(5.0).ToList();          // fires the event
        var second = tl.Update(1.0).ToList(); // should not re-fire

        Assert.Empty(second);
    }

    // ── 2. Two events at same trigger time ────────────────────────────────────

    [Fact]
    public void TwoEventsAtSameTime_BothReturnedInSameUpdate()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(10f, TimelineEventType.StopScroll),
            E(10f, TimelineEventType.DespawnAllEnemies),
        });

        var result = tl.Update(10.0).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TwoEventsAtSameTime_CorrectTypesReturned()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(10f, TimelineEventType.StopScroll),
            E(10f, TimelineEventType.DespawnAllEnemies),
        });

        var result = tl.Update(10.0).ToList();
        var types  = result.Select(e => e.Type).ToHashSet();

        Assert.Contains(TimelineEventType.StopScroll,       types);
        Assert.Contains(TimelineEventType.DespawnAllEnemies, types);
    }

    [Fact]
    public void ThreeEventsAtSameTime_AllThreeReturnedInSameUpdate()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(10f, TimelineEventType.StopScroll),
            E(10f, TimelineEventType.DespawnAllEnemies),
            E(10f, TimelineEventType.SpawnBoss),
        });

        var result = tl.Update(10.0).ToList();

        Assert.Equal(3, result.Count);
    }

    // ── 3. After all events fired, subsequent Update returns empty ─────────────

    [Fact]
    public void AfterAllEventsFired_SubsequentUpdateReturnsEmpty()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f,  TimelineEventType.SpawnWave),
            E(10f, TimelineEventType.CrossfadeMusic),
        });

        tl.Update(10.0).ToList(); // fires both

        var result = tl.Update(100.0).ToList(); // nothing left

        Assert.Empty(result);
    }

    [Fact]
    public void UpdatesReturnEventsOnlyOnce_AcrossMultipleCalls()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        var first  = tl.Update(5.0).ToList();
        var second = tl.Update(5.0).ToList();
        var third  = tl.Update(5.0).ToList();

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Empty(third);
    }

    // ── 4. Reset rewinds to t=0 and re-fires events ───────────────────────────

    [Fact]
    public void Reset_AfterAllFired_AllowsEventsToFireAgain()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        tl.Update(5.0).ToList(); // fire once
        tl.Reset();

        var result = tl.Update(5.0).ToList(); // should fire again

        Assert.Single(result);
    }

    [Fact]
    public void Reset_ResetsElapsedToZero_EarlyEventNotFiredBeforeTime()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(5f, TimelineEventType.SpawnWave),
        });

        tl.Update(5.0).ToList();
        tl.Reset();

        // After reset, t=4.9 should not fire yet
        var result = tl.Update(4.9).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Reset_MultipleTimesWorksCorrectly()
    {
        var tl = new LevelDirectorTimeline(new[]
        {
            E(3f, TimelineEventType.RegisterCheckpoint),
        });

        for (int i = 0; i < 5; i++)
        {
            var fired = tl.Update(3.0).ToList();
            Assert.Single(fired);
            tl.Reset();
        }
    }

    // ── Sort on construction ──────────────────────────────────────────────────

    [Fact]
    public void EventsAreReturnedInTimeOrder_EvenIfAddedOutOfOrder()
    {
        // Provide events in reverse chronological order.
        var tl = new LevelDirectorTimeline(new[]
        {
            E(10f, TimelineEventType.CrossfadeMusic),
            E(3f,  TimelineEventType.SpawnWave),
            E(7f,  TimelineEventType.RegisterCheckpoint),
        });

        var at3  = tl.Update(3.0).ToList();
        var at7  = tl.Update(4.0).ToList(); // cumulative 7.0
        var at10 = tl.Update(3.0).ToList(); // cumulative 10.0

        Assert.Single(at3);
        Assert.Equal(TimelineEventType.SpawnWave, at3[0].Type);

        Assert.Single(at7);
        Assert.Equal(TimelineEventType.RegisterCheckpoint, at7[0].Type);

        Assert.Single(at10);
        Assert.Equal(TimelineEventType.CrossfadeMusic, at10[0].Type);
    }

    // ── 5. FromJson parsing ───────────────────────────────────────────────────

    private const string SampleJson = """
        {
            "scroll_speed": 120,
            "events": [
                { "time": 5.0,   "type": "SpawnWave",    "enemy": "WraithFighter",
                  "count": 3,    "formation": "V" },
                { "time": 60.0,  "type": "CrossfadeMusic","track": "music_act2",
                  "duration": 2.0 },
                { "time": 160.0, "type": "StopScroll" }
            ]
        }
        """;

    [Fact]
    public void FromJson_ParsesCorrectNumberOfEvents()
    {
        var tl = LevelDirectorTimeline.FromJson(SampleJson);

        // Fire all events by advancing far enough
        var all = tl.Update(200.0).ToList();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void FromJson_FirstEvent_HasCorrectTriggerTimeAndType()
    {
        var tl    = LevelDirectorTimeline.FromJson(SampleJson);
        var fired = tl.Update(5.0).ToList();

        Assert.Single(fired);
        Assert.Equal(5.0f,                          fired[0].TriggerTime);
        Assert.Equal(TimelineEventType.SpawnWave,   fired[0].Type);
    }

    [Fact]
    public void FromJson_FirstEvent_Params_ContainStringValues()
    {
        var tl  = LevelDirectorTimeline.FromJson(SampleJson);
        var ev  = tl.Update(5.0).ToList()[0];

        Assert.Equal("WraithFighter", ev.Params["enemy"]);
        Assert.Equal("V",             ev.Params["formation"]);
    }

    [Fact]
    public void FromJson_FirstEvent_Params_ContainIntegerCount()
    {
        var tl = LevelDirectorTimeline.FromJson(SampleJson);
        var ev = tl.Update(5.0).ToList()[0];

        // "count": 3 should parse as int, not double
        Assert.IsType<int>(ev.Params["count"]);
        Assert.Equal(3, (int)ev.Params["count"]);
    }

    [Fact]
    public void FromJson_SecondEvent_HasCorrectTypeAndTrack()
    {
        var tl = LevelDirectorTimeline.FromJson(SampleJson);
        tl.Update(5.0).ToList();               // skip first event
        var fired = tl.Update(55.0).ToList();  // cumulative 60.0

        Assert.Single(fired);
        Assert.Equal(TimelineEventType.CrossfadeMusic, fired[0].Type);
        Assert.Equal("music_act2",                     fired[0].Params["track"]);
    }

    [Fact]
    public void FromJson_SecondEvent_DurationIsDouble()
    {
        var tl = LevelDirectorTimeline.FromJson(SampleJson);
        tl.Update(5.0).ToList();
        var fired = tl.Update(55.0).ToList(); // cumulative 60.0
        var ev    = fired[0];

        // "duration": 2.0 has a decimal point → parses as double
        Assert.IsType<double>(ev.Params["duration"]);
        Assert.Equal(2.0, (double)ev.Params["duration"]);
    }

    [Fact]
    public void FromJson_ThirdEvent_StopScrollHasEmptyParams()
    {
        var tl = LevelDirectorTimeline.FromJson(SampleJson);
        tl.Update(160.0).ToList(); // fire all

        // Rebuild and just grab the third event
        var tl2   = LevelDirectorTimeline.FromJson(SampleJson);
        var all   = tl2.Update(200.0).ToList();
        var third = all[2];

        Assert.Equal(TimelineEventType.StopScroll, third.Type);
        Assert.Equal(160.0f, third.TriggerTime);
        Assert.Empty(third.Params);
    }

    [Fact]
    public void FromJson_ScrollSpeedFieldIsIgnored_NoExtraEvents()
    {
        // scroll_speed is a root-level field, not an event — ensure it doesn't
        // produce a stray event entry.
        var tl  = LevelDirectorTimeline.FromJson(SampleJson);
        var all = tl.Update(9999.0).ToList();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void FromJson_EventsAreSortedByTime_EvenIfJsonIsUnordered()
    {
        const string unsortedJson = """
            {
                "events": [
                    { "time": 30.0, "type": "SpawnBoss" },
                    { "time": 5.0,  "type": "SpawnWave" },
                    { "time": 15.0, "type": "CrossfadeMusic" }
                ]
            }
            """;

        var tl = LevelDirectorTimeline.FromJson(unsortedJson);

        var first  = tl.Update(5.0).ToList();
        var second = tl.Update(10.0).ToList();  // cumulative 15.0
        var third  = tl.Update(15.0).ToList();  // cumulative 30.0

        Assert.Equal(TimelineEventType.SpawnWave,       first[0].Type);
        Assert.Equal(TimelineEventType.CrossfadeMusic,  second[0].Type);
        Assert.Equal(TimelineEventType.SpawnBoss,       third[0].Type);
    }
}
