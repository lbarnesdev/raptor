// tests/Logic/EnemyStateMachineTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// xUnit tests for EnemyStateMachine (TICKET-1203).
// No Godot dependencies — runs with plain `dotnet test`.
//
// Scenarios covered:
//   1. Default state is FormationHold.
//   2. StartAttackRun() from FormationHold → AttackRun, returns true.
//   3. StartAttackRun() from AttackRun → no-op, returns false.
//   4. StartAttackRun() from Fleeing → no-op, returns false.
//   5. StartFlee() from AttackRun → Fleeing, returns true.
//   6. StartFlee() from FormationHold → no-op, returns false.
//   7. StartFlee() from Fleeing → no-op, returns false.
//   8. Kill() from FormationHold → Dead, returns true.
//   9. Kill() from AttackRun → Dead, returns true.
//  10. Kill() from Fleeing → Dead, returns true.
//  11. Kill() from Dead → no-op (terminal), returns false.
//  12. StartAttackRun() after Dead → no-op, returns false.
//  13. StartFlee() after Dead → no-op, returns false.
// ─────────────────────────────────────────────────────────────────────────────

using Raptor.Logic;
using Xunit;

namespace Raptor.Tests.Logic;

public class EnemyStateMachineTests
{
    // ── 1. Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsFormationHold()
    {
        var fsm = new EnemyStateMachine();

        Assert.Equal(EnemyState.FormationHold, fsm.CurrentState);
    }

    // ── 2–4. StartAttackRun ─────────────────────────────────────────────────

    [Fact]
    public void StartAttackRun_FromFormationHold_ReturnsTrue()
    {
        var fsm = new EnemyStateMachine();

        bool result = fsm.StartAttackRun();

        Assert.True(result);
    }

    [Fact]
    public void StartAttackRun_FromFormationHold_TransitionsToAttackRun()
    {
        var fsm = new EnemyStateMachine();

        fsm.StartAttackRun();

        Assert.Equal(EnemyState.AttackRun, fsm.CurrentState);
    }

    [Fact]
    public void StartAttackRun_FromAttackRun_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun(); // FormationHold → AttackRun

        bool result = fsm.StartAttackRun(); // illegal transition

        Assert.False(result);
    }

    [Fact]
    public void StartAttackRun_FromAttackRun_StaysInAttackRun()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        fsm.StartAttackRun();

        Assert.Equal(EnemyState.AttackRun, fsm.CurrentState);
    }

    [Fact]
    public void StartAttackRun_FromFleeing_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();
        fsm.StartFlee(); // → Fleeing

        bool result = fsm.StartAttackRun(); // illegal

        Assert.False(result);
    }

    // ── 5–7. StartFlee ────────────────────────────────────────────────────────

    [Fact]
    public void StartFlee_FromAttackRun_ReturnsTrue()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        bool result = fsm.StartFlee();

        Assert.True(result);
    }

    [Fact]
    public void StartFlee_FromAttackRun_TransitionsToFleeing()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        fsm.StartFlee();

        Assert.Equal(EnemyState.Fleeing, fsm.CurrentState);
    }

    [Fact]
    public void StartFlee_FromFormationHold_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine(); // FormationHold

        bool result = fsm.StartFlee(); // illegal — must be in AttackRun first

        Assert.False(result);
    }

    [Fact]
    public void StartFlee_FromFormationHold_StaysInFormationHold()
    {
        var fsm = new EnemyStateMachine();

        fsm.StartFlee();

        Assert.Equal(EnemyState.FormationHold, fsm.CurrentState);
    }

    [Fact]
    public void StartFlee_FromFleeing_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();
        fsm.StartFlee();

        bool result = fsm.StartFlee(); // already Fleeing

        Assert.False(result);
    }

    // ── 8–10. Kill from live states ──────────────────────────────────────────

    [Fact]
    public void Kill_FromFormationHold_ReturnsTrue()
    {
        var fsm = new EnemyStateMachine();

        bool result = fsm.Kill();

        Assert.True(result);
    }

    [Fact]
    public void Kill_FromFormationHold_TransitionsToDead()
    {
        var fsm = new EnemyStateMachine();

        fsm.Kill();

        Assert.Equal(EnemyState.Dead, fsm.CurrentState);
    }

    [Fact]
    public void Kill_FromAttackRun_ReturnsTrue()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        bool result = fsm.Kill();

        Assert.True(result);
    }

    [Fact]
    public void Kill_FromAttackRun_TransitionsToDead()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        fsm.Kill();

        Assert.Equal(EnemyState.Dead, fsm.CurrentState);
    }

    [Fact]
    public void Kill_FromFleeing_ReturnsTrue()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();
        fsm.StartFlee();

        bool result = fsm.Kill();

        Assert.True(result);
    }

    [Fact]
    public void Kill_FromFleeing_TransitionsToDead()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();
        fsm.StartFlee();

        fsm.Kill();

        Assert.Equal(EnemyState.Dead, fsm.CurrentState);
    }

    // ── 11–13. Dead is terminal ───────────────────────────────────────────────

    [Fact]
    public void Kill_FromDead_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.Kill(); // → Dead

        bool result = fsm.Kill(); // terminal — no-op

        Assert.False(result);
    }

    [Fact]
    public void Kill_FromDead_StaysInDead()
    {
        var fsm = new EnemyStateMachine();
        fsm.Kill();

        fsm.Kill();

        Assert.Equal(EnemyState.Dead, fsm.CurrentState);
    }

    [Fact]
    public void StartAttackRun_AfterDead_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.Kill();

        bool result = fsm.StartAttackRun();

        Assert.False(result);
    }

    [Fact]
    public void StartFlee_AfterDead_ReturnsFalse()
    {
        var fsm = new EnemyStateMachine();
        fsm.Kill();

        bool result = fsm.StartFlee();

        Assert.False(result);
    }

    // ── Update is a no-op ────────────────────────────────────────────────────

    [Fact]
    public void Update_DoesNotChangeState()
    {
        var fsm = new EnemyStateMachine();
        fsm.StartAttackRun();

        fsm.Update(99.9); // large delta — no timers → no state change

        Assert.Equal(EnemyState.AttackRun, fsm.CurrentState);
    }
}
