// tests/Logic/StateMachineTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for StateMachine<TState>.
// No Godot dependencies — runs with plain `dotnet test`.
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>Three-state enum used exclusively in these tests.</summary>
internal enum TestState { A, B, C }

/// <summary>
/// Concrete subclass that records every OnEnter / OnExit call
/// and exposes a delegate hook for CanTransition so individual tests
/// can inject custom guard logic.
/// </summary>
internal sealed class TrackingStateMachine : StateMachine<TestState>
{
    // Ordered log of (callback, state) pairs — lets tests assert
    // that OnExit fires before OnEnter on the same transition.
    public List<(string Callback, TestState State)> CallLog { get; } = new();

    // Inject a custom guard per test; defaults to allow-all.
    public Func<TestState, TestState, bool>? CanTransitionOverride { get; set; }

    public TrackingStateMachine(TestState initial) : base(initial) { }

    // Update is required by the abstract contract but unused in unit tests.
    public override void Update(double delta) { }

    protected override bool CanTransition(TestState from, TestState to)
        => CanTransitionOverride?.Invoke(from, to) ?? true;

    protected override void OnEnter(TestState state)
        => CallLog.Add(("OnEnter", state));

    protected override void OnExit(TestState state)
        => CallLog.Add(("OnExit", state));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class StateMachineTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsSetByConstructor()
    {
        var sm = new TrackingStateMachine(TestState.A);

        Assert.Equal(TestState.A, sm.CurrentState);
    }

    [Fact]
    public void InitialPreviousState_EqualsInitialCurrent()
    {
        // Before any transition, PreviousState == CurrentState == initial.
        var sm = new TrackingStateMachine(TestState.B);

        Assert.Equal(TestState.B, sm.PreviousState);
    }

    [Fact]
    public void Constructor_DoesNotFireOnEnterForInitialState()
    {
        // OnEnter should NOT be called during construction — only on transitions.
        var sm = new TrackingStateMachine(TestState.A);

        Assert.Empty(sm.CallLog);
    }

    // ── Successful transition ────────────────────────────────────────────────

    [Fact]
    public void Transition_ReturnsTrue_WhenAllowed()
    {
        var sm = new TrackingStateMachine(TestState.A);

        bool result = sm.Transition(TestState.B);

        Assert.True(result);
    }

    [Fact]
    public void Transition_UpdatesCurrentState()
    {
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);

        Assert.Equal(TestState.B, sm.CurrentState);
    }

    [Fact]
    public void Transition_UpdatesPreviousState()
    {
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);

        Assert.Equal(TestState.A, sm.PreviousState);
    }

    [Fact]
    public void Transition_CallsOnExitWithOldState()
    {
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);

        Assert.Contains(("OnExit", TestState.A), sm.CallLog);
    }

    [Fact]
    public void Transition_CallsOnEnterWithNewState()
    {
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);

        Assert.Contains(("OnEnter", TestState.B), sm.CallLog);
    }

    [Fact]
    public void Transition_OnExitFiresBeforeOnEnter()
    {
        // Critical ordering: old state must be notified it's leaving
        // before the new state is notified it's entered.
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);

        int exitIndex  = sm.CallLog.FindIndex(e => e == ("OnExit",  TestState.A));
        int enterIndex = sm.CallLog.FindIndex(e => e == ("OnEnter", TestState.B));

        Assert.True(exitIndex < enterIndex,
            $"Expected OnExit(A) at index {exitIndex} < OnEnter(B) at index {enterIndex}");
    }

    // ── Blocked transition ───────────────────────────────────────────────────

    [Fact]
    public void Transition_ReturnsFalse_WhenCanTransitionBlocks()
    {
        var sm = new TrackingStateMachine(TestState.A)
        {
            CanTransitionOverride = (from, to) => false   // block everything
        };

        bool result = sm.Transition(TestState.B);

        Assert.False(result);
    }

    [Fact]
    public void Transition_LeavesCurrentStateUnchanged_WhenBlocked()
    {
        var sm = new TrackingStateMachine(TestState.A)
        {
            CanTransitionOverride = (from, to) => false
        };

        sm.Transition(TestState.B);

        Assert.Equal(TestState.A, sm.CurrentState);
    }

    [Fact]
    public void Transition_LeavesPreviousStateUnchanged_WhenBlocked()
    {
        var sm = new TrackingStateMachine(TestState.A)
        {
            CanTransitionOverride = (from, to) => false
        };

        sm.Transition(TestState.B);

        // PreviousState was never updated.
        Assert.Equal(TestState.A, sm.PreviousState);
    }

    [Fact]
    public void Transition_DoesNotCallOnExitOrOnEnter_WhenBlocked()
    {
        var sm = new TrackingStateMachine(TestState.A)
        {
            CanTransitionOverride = (from, to) => false
        };

        sm.Transition(TestState.B);

        Assert.Empty(sm.CallLog);
    }

    [Fact]
    public void Transition_CanBlockSelectiveTransitions()
    {
        // Allow A→B but block A→C.
        var sm = new TrackingStateMachine(TestState.A)
        {
            CanTransitionOverride = (from, to) => to != TestState.C
        };

        bool allowedResult = sm.Transition(TestState.B);   // A→B: allowed
        bool blockedResult = sm.Transition(TestState.C);   // B→C: blocked

        Assert.True(allowedResult);
        Assert.False(blockedResult);
        Assert.Equal(TestState.B, sm.CurrentState);
    }

    // ── Consecutive transitions ──────────────────────────────────────────────

    [Fact]
    public void ConsecutiveTransitions_TrackStateCorrectly()
    {
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);
        sm.Transition(TestState.C);

        Assert.Equal(TestState.C, sm.CurrentState);
        Assert.Equal(TestState.B, sm.PreviousState);
    }

    [Fact]
    public void ConsecutiveTransitions_ProduceCorrectCallLog()
    {
        // A → B → C should log: OnExit(A), OnEnter(B), OnExit(B), OnEnter(C)
        var sm = new TrackingStateMachine(TestState.A);

        sm.Transition(TestState.B);
        sm.Transition(TestState.C);

        var expected = new[]
        {
            ("OnExit",  TestState.A),
            ("OnEnter", TestState.B),
            ("OnExit",  TestState.B),
            ("OnEnter", TestState.C),
        };

        Assert.Equal(expected, sm.CallLog);
    }

    [Fact]
    public void TransitionToSameState_IsAllowedAndFiresCallbacks()
    {
        // Self-transition: A → A. Some FSMs use this intentionally (e.g. "restart").
        // StateMachine<T> allows it unless CanTransition blocks it.
        var sm = new TrackingStateMachine(TestState.A);

        bool result = sm.Transition(TestState.A);

        Assert.True(result);
        Assert.Equal(TestState.A, sm.CurrentState);
        Assert.Equal(TestState.A, sm.PreviousState);
        Assert.Contains(("OnExit",  TestState.A), sm.CallLog);
        Assert.Contains(("OnEnter", TestState.A), sm.CallLog);
    }
}
