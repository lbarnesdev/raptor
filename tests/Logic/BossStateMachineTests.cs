// tests/Logic/BossStateMachineTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for BossStateMachine.
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered (matching ticket spec):
//   1. Full sequence: Intro→Phase1→Transition→Phase2→Transition→Phase3→Transition→Defeated
//   2. OnPhaseHealthDepleted on wrong phase is a no-op
//   3. OnTransitionAnimationComplete when not in Transition is a no-op
//   4. CanTransition blocks Phase1→Phase3 direct jump
//   5. CurrentPhase returns correct int per state
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class BossStateMachineTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Advance the machine from Intro to Phase1.</summary>
    private static BossStateMachine InPhase1()
    {
        var sm = new BossStateMachine();
        sm.StartBoss();
        return sm;
    }

    /// <summary>Advance the machine from Intro through to Phase2.</summary>
    private static BossStateMachine InPhase2()
    {
        var sm = InPhase1();
        sm.OnPhaseHealthDepleted(1);
        sm.OnTransitionAnimationComplete();
        return sm;
    }

    /// <summary>Advance the machine from Intro through to Phase3.</summary>
    private static BossStateMachine InPhase3()
    {
        var sm = InPhase2();
        sm.OnPhaseHealthDepleted(2);
        sm.OnTransitionAnimationComplete();
        return sm;
    }

    // ── Construction sanity ──────────────────────────────────────────────────

    [Fact]
    public void NewInstance_StartsInIntro()
    {
        var sm = new BossStateMachine();
        Assert.Equal(BossState.Intro, sm.CurrentState);
    }

    [Fact]
    public void NewInstance_CurrentPhaseIsZero()
    {
        var sm = new BossStateMachine();
        Assert.Equal(0, sm.CurrentPhase);
    }

    // ── 1. Full fight sequence ────────────────────────────────────────────────

    [Fact]
    public void StartBoss_TransitionsToPhase1()
    {
        var sm = new BossStateMachine();

        sm.StartBoss();

        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    [Fact]
    public void Phase1_OnHealthDepleted_TransitionsToTransition()
    {
        var sm = InPhase1();

        sm.OnPhaseHealthDepleted(1);

        Assert.Equal(BossState.Transition, sm.CurrentState);
    }

    [Fact]
    public void TransitionAfterPhase1_OnAnimationComplete_TransitionsToPhase2()
    {
        var sm = InPhase1();
        sm.OnPhaseHealthDepleted(1);

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Phase2, sm.CurrentState);
    }

    [Fact]
    public void Phase2_OnHealthDepleted_TransitionsToTransition()
    {
        var sm = InPhase2();

        sm.OnPhaseHealthDepleted(2);

        Assert.Equal(BossState.Transition, sm.CurrentState);
    }

    [Fact]
    public void TransitionAfterPhase2_OnAnimationComplete_TransitionsToPhase3()
    {
        var sm = InPhase2();
        sm.OnPhaseHealthDepleted(2);

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Phase3, sm.CurrentState);
    }

    [Fact]
    public void Phase3_OnHealthDepleted_TransitionsToTransition()
    {
        var sm = InPhase3();

        sm.OnPhaseHealthDepleted(3);

        Assert.Equal(BossState.Transition, sm.CurrentState);
    }

    [Fact]
    public void TransitionAfterPhase3_OnAnimationComplete_TransitionsToDefeated()
    {
        var sm = InPhase3();
        sm.OnPhaseHealthDepleted(3);

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Defeated, sm.CurrentState);
    }

    [Fact]
    public void FullFightSequence_AllStatesVisitedInOrder()
    {
        // Trace the entire fight as one linear sequence and assert each state.
        var sm = new BossStateMachine();

        Assert.Equal(BossState.Intro,      sm.CurrentState);

        sm.StartBoss();
        Assert.Equal(BossState.Phase1,     sm.CurrentState);

        sm.OnPhaseHealthDepleted(1);
        Assert.Equal(BossState.Transition, sm.CurrentState);

        sm.OnTransitionAnimationComplete();
        Assert.Equal(BossState.Phase2,     sm.CurrentState);

        sm.OnPhaseHealthDepleted(2);
        Assert.Equal(BossState.Transition, sm.CurrentState);

        sm.OnTransitionAnimationComplete();
        Assert.Equal(BossState.Phase3,     sm.CurrentState);

        sm.OnPhaseHealthDepleted(3);
        Assert.Equal(BossState.Transition, sm.CurrentState);

        sm.OnTransitionAnimationComplete();
        Assert.Equal(BossState.Defeated,   sm.CurrentState);
    }

    // ── 2. OnPhaseHealthDepleted with wrong phase is a no-op ─────────────────

    [Fact]
    public void OnPhaseHealthDepleted_WrongPhaseNumber_IsNoOp()
    {
        // In Phase 1 (CurrentPhase=1), deplete phase 2 — should be ignored.
        var sm = InPhase1();

        sm.OnPhaseHealthDepleted(2);

        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    [Fact]
    public void OnPhaseHealthDepleted_WrongPhaseNumber_PreviousStateUnchanged()
    {
        var sm = InPhase2();

        sm.OnPhaseHealthDepleted(1); // stale call — phase 1 already done

        Assert.Equal(BossState.Phase2, sm.CurrentState);
    }

    [Fact]
    public void OnPhaseHealthDepleted_WhileInTransition_IsNoOp()
    {
        // OnPhaseHealthDepleted should be a no-op when the machine is already
        // in Transition (a stray hit delivered the same frame HP hit 0).
        var sm = InPhase1();
        sm.OnPhaseHealthDepleted(1); // now in Transition

        sm.OnPhaseHealthDepleted(1); // should do nothing

        Assert.Equal(BossState.Transition, sm.CurrentState);
    }

    [Fact]
    public void OnPhaseHealthDepleted_WhileInIntro_IsNoOp()
    {
        var sm = new BossStateMachine();

        sm.OnPhaseHealthDepleted(1);

        Assert.Equal(BossState.Intro, sm.CurrentState);
    }

    [Fact]
    public void OnPhaseHealthDepleted_WhileDefeated_IsNoOp()
    {
        var sm = InPhase3();
        sm.OnPhaseHealthDepleted(3);
        sm.OnTransitionAnimationComplete(); // now Defeated

        sm.OnPhaseHealthDepleted(3);

        Assert.Equal(BossState.Defeated, sm.CurrentState);
    }

    // ── 3. OnTransitionAnimationComplete when not in Transition is a no-op ───

    [Fact]
    public void OnTransitionAnimationComplete_WhileInPhase1_IsNoOp()
    {
        var sm = InPhase1();

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    [Fact]
    public void OnTransitionAnimationComplete_WhileInIntro_IsNoOp()
    {
        var sm = new BossStateMachine();

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Intro, sm.CurrentState);
    }

    [Fact]
    public void OnTransitionAnimationComplete_WhileInPhase2_IsNoOp()
    {
        var sm = InPhase2();

        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Phase2, sm.CurrentState);
    }

    [Fact]
    public void OnTransitionAnimationComplete_WhileDefeated_IsNoOp()
    {
        var sm = InPhase3();
        sm.OnPhaseHealthDepleted(3);
        sm.OnTransitionAnimationComplete(); // now Defeated

        sm.OnTransitionAnimationComplete(); // second call — no-op

        Assert.Equal(BossState.Defeated, sm.CurrentState);
    }

    // ── 4. CanTransition blocks illegal jumps ─────────────────────────────────

    [Fact]
    public void CanTransition_Phase1ToPhase3_IsBlocked()
    {
        var sm = InPhase1();

        bool result = sm.Transition(BossState.Phase3);

        Assert.False(result);
        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_Phase1ToDefeated_IsBlocked()
    {
        var sm = InPhase1();

        bool result = sm.Transition(BossState.Defeated);

        Assert.False(result);
        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_IntroToPhase2_IsBlocked()
    {
        var sm = new BossStateMachine();

        bool result = sm.Transition(BossState.Phase2);

        Assert.False(result);
        Assert.Equal(BossState.Intro, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_Phase2ToPhase1_IsBlocked()
    {
        var sm = InPhase2();

        bool result = sm.Transition(BossState.Phase1);

        Assert.False(result);
        Assert.Equal(BossState.Phase2, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_Phase1ToPhase1_IsBlocked()
    {
        // Self-transitions are not in the valid graph.
        var sm = InPhase1();

        bool result = sm.Transition(BossState.Phase1);

        Assert.False(result);
    }

    [Fact]
    public void CanTransition_IntroToPhase1_IsAllowed()
    {
        var sm = new BossStateMachine();

        bool result = sm.Transition(BossState.Phase1);

        Assert.True(result);
        Assert.Equal(BossState.Phase1, sm.CurrentState);
    }

    // ── 5. CurrentPhase returns correct int per state ─────────────────────────

    [Theory]
    [InlineData(BossState.Intro,      0)]
    [InlineData(BossState.Transition, 0)]
    [InlineData(BossState.Defeated,   0)]
    [InlineData(BossState.Phase1,     1)]
    [InlineData(BossState.Phase2,     2)]
    [InlineData(BossState.Phase3,     3)]
    public void CurrentPhase_ReturnsCorrectValueForEachState(
        BossState targetState, int expectedPhase)
    {
        // Navigate the machine to each target state using the appropriate helper.
        var sm = targetState switch
        {
            BossState.Intro       => new BossStateMachine(),
            BossState.Phase1      => InPhase1(),
            BossState.Transition  => AdvanceToTransition(),
            BossState.Phase2      => InPhase2(),
            BossState.Phase3      => InPhase3(),
            BossState.Defeated    => AdvanceToDefeated(),
            _                     => throw new ArgumentOutOfRangeException(),
        };

        Assert.Equal(expectedPhase, sm.CurrentPhase);
    }

    // ── PreviousState tracking ────────────────────────────────────────────────

    [Fact]
    public void AfterStartBoss_PreviousStateIsIntro()
    {
        var sm = new BossStateMachine();
        sm.StartBoss();

        Assert.Equal(BossState.Intro, sm.PreviousState);
    }

    [Fact]
    public void AfterTransitionToPhase2_PreviousStateIsTransition()
    {
        var sm = InPhase1();
        sm.OnPhaseHealthDepleted(1);
        sm.OnTransitionAnimationComplete();

        Assert.Equal(BossState.Transition, sm.PreviousState);
    }

    // ── Private helpers for the Theory ───────────────────────────────────────

    private static BossStateMachine AdvanceToTransition()
    {
        var sm = InPhase1();
        sm.OnPhaseHealthDepleted(1);
        return sm;
    }

    private static BossStateMachine AdvanceToDefeated()
    {
        var sm = InPhase3();
        sm.OnPhaseHealthDepleted(3);
        sm.OnTransitionAnimationComplete();
        return sm;
    }
}
