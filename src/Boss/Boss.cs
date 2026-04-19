// src/Boss/Boss.cs
// ─────────────────────────────────────────────────────────────────────────────
// Root Node2D script for The Demagogue boss scene.
//
// Node placement (Boss.tscn — arch doc Section 3.3):
//   Boss (Node2D)  ← this script
//   ├── BodySprite (AnimatedSprite2D)
//   ├── TinyHandNode (Node2D)
//   │   └── WeakPoint_P1 (Area2D + WeakPoint.cs)
//   ├── ToupeeNode (Node2D)
//   │   └── WeakPoint_P2 (Area2D + WeakPoint.cs)
//   ├── StatueNode (Node2D)          — hidden until Phase 3
//   │   └── WeakPoint_P3 (Area2D + WeakPoint.cs)
//   ├── AlienPassenger (Node2D + AlienPassenger.cs)
//   └── PhaseControllers (Node2D)
//       ├── Phase1Controller (Node)
//       ├── Phase2Controller (Node)
//       └── Phase3Controller (Node)
//
// Ownership:
//   Boss owns one BossStateMachine and one BossPhaseHealth instance.
//   WeakPoint.cs calls Boss.Instance.OnWeakPointHit() on every valid hit.
//   Boss routes those hits through BossPhaseHealth and drives state transitions
//   in BossStateMachine.  All EventBus boss signals are emitted from here.
//
// Phase transition flow:
//   1. WeakPoint hit → OnWeakPointHit() → ApplyHit() → HP reaches 0
//   2. SetTransitionLock(true) + OnPhaseHealthDepleted() → FSM enters Transition
//   3. StartTransition() disables all weak points, waits 2 s, calls CompleteTransition()
//   4. CompleteTransition() advances health phase, advances FSM, enables new weak point
//   5. If FSM reaches Defeated → OnBossDefeated() (AlienPassenger flee in Slice 6)
//
// Phase controller activation:
//   Each PhaseController is a Node child.  Boss calls SetActive(bool) on the
//   correct controller when entering or leaving a phase.  Controllers are
//   implemented in Slice 6 (TICKET-601/602/603); the GetNodeOrNull calls below
//   guard against missing nodes so this script works in Slice 5 without them.
//
// LevelDirector wiring:
//   LevelDirector calls Boss.Instance.StartBoss() via CallDeferred after
//   instantiating and adding the scene — see TICKET-504.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;
using Raptor.Logic;

namespace Raptor.Boss;

/// <summary>
/// Orchestrates the three-phase boss fight.  Owns <see cref="BossStateMachine"/>
/// and <see cref="BossPhaseHealth"/>; routes weak-point hits, drives phase
/// transitions, and emits all boss-related <see cref="EventBus"/> signals.
/// </summary>
public partial class Boss : Node2D
{
    // ── Singleton ────────────────────────────────────────────────────────────

    /// <summary>
    /// The live boss instance.  <c>null</c> when no boss is present.
    /// Set in <c>_Ready()</c>, cleared in <c>_ExitTree()</c>.
    /// </summary>
    public static Boss? Instance { get; private set; }

    // ── Exported scene references ─────────────────────────────────────────────

    /// <summary>
    /// The <c>AlienPassenger.tscn</c> scene, used by the defeat sequence in
    /// Slice 6 (TICKET-606).  Assign in the Inspector.
    /// </summary>
    [Export] public PackedScene? AlienPassengerScene { get; set; }

    // ── Pure-C# logic objects ─────────────────────────────────────────────────

    private readonly BossStateMachine _stateMachine = new();

    // Phase HP values (PRD F-411 / F-421 / F-431):
    //   Phase 1 — 20 plasma hits  (or 5 missile hits at 3× damage each = 15 effective)
    //   Phase 2 — 25 plasma hits
    //   Phase 3 — 30 plasma hits
    // MissileMultiplier = 3 means each missile subtracts 3 HP (default in PhaseConfig).
    private readonly BossPhaseHealth _health = new(new PhaseConfig[]
    {
        new(MaxHp: 20),
        new(MaxHp: 25),
        new(MaxHp: 30),
    });

    // ── Cached node references ────────────────────────────────────────────────

    // Indexed 0–2, matching PhaseIndex 1–3. Populated in _Ready().
    private WeakPoint[] _weakPoints = null!;

    // Phase controllers — may be null in Slice 5 before they are implemented.
    private Node? _phase1Controller;
    private Node? _phase2Controller;
    private Node? _phase3Controller;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        // Cache weak point nodes.
        _weakPoints = new WeakPoint[]
        {
            GetNode<WeakPoint>("TinyHandNode/WeakPoint_P1"),
            GetNode<WeakPoint>("ToupeeNode/WeakPoint_P2"),
            GetNode<WeakPoint>("StatueNode/WeakPoint_P3"),
        };

        // All weak points start disabled (WeakPoint._Ready() also does this,
        // but being explicit here documents the intended initial state).
        foreach (var wp in _weakPoints)
            wp.SetActive(false);

        // Phase controllers are optional until Slice 6.
        _phase1Controller = GetNodeOrNull<Node>("PhaseControllers/Phase1Controller");
        _phase2Controller = GetNodeOrNull<Node>("PhaseControllers/Phase2Controller");
        _phase3Controller = GetNodeOrNull<Node>("PhaseControllers/Phase3Controller");

        // StatueNode is hidden until Phase 3.
        GetNode<Node2D>("StatueNode").Visible = false;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Public API — called externally ───────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Raptor.World.LevelDirector"/> (via
    /// <c>CallDeferred</c>) after the boss scene is added to the tree.
    /// Transitions the FSM from <see cref="BossState.Intro"/> to
    /// <see cref="BossState.Phase1"/> and enables the first weak point.
    /// </summary>
    public void StartBoss()
    {
        _stateMachine.StartBoss();            // Intro → Phase1

        ActivatePhase(1);

        EventBus.Instance.EmitSignal(EventBus.SignalName.BossSpawned);
        EmitHpChanged();                      // initialise HUD bar to full Phase 1 HP

        GD.Print("Boss: StartBoss() — Phase 1 active.");
    }

    /// <summary>
    /// Called by <see cref="WeakPoint.OnAreaEntered"/> when a player projectile
    /// strikes the active weak point.
    /// </summary>
    /// <param name="phaseIndex">1-indexed phase of the struck weak point.</param>
    /// <param name="isMissile"><c>true</c> if the projectile was a <see cref="Raptor.Projectiles.Missile"/>.</param>
    public void OnWeakPointHit(int phaseIndex, bool isMissile)
    {
        // Guard 1: only accept hits during an active combat phase.
        if (_stateMachine.CurrentPhase == 0)
            return;

        // Guard 2: the hit must be on the currently active phase's weak point.
        // Stale physics events can occasionally fire on a just-disabled Area2D.
        if (_stateMachine.CurrentPhase != phaseIndex)
            return;

        // Damage amount: missiles deal 3, plasma bolts deal 1.
        // ApplyHit also applies the phase's MissileMultiplier internally and
        // returns false if the hit is rejected (transition lock, HP already 0).
        int rawDamage = isMissile ? 3 : 1;
        bool applied  = _health.ApplyHit(rawDamage, isMissile);
        if (!applied)
            return;

        // SFX.
        AudioManager.Instance.PlaySfx(AudioManager.Sfx.BossHit);

        // Update the HUD health bar.
        EmitHpChanged();

        // Check if this hit killed the current phase.
        if (_health.IsCurrentPhaseDead)
            BeginPhaseTransition(phaseIndex);
    }

    // ── Phase transition pipeline ─────────────────────────────────────────────

    /// <summary>
    /// Starts the between-phase transition: locks hits, shuts down the current
    /// phase's controller and weak point, then waits 2 s before advancing.
    /// </summary>
    private async void BeginPhaseTransition(int completedPhase)
    {
        // Lock the health system so stray in-flight projectiles can't double-trigger.
        _health.SetTransitionLock(true);

        // Notify the FSM — moves to Transition state.
        _stateMachine.OnPhaseHealthDepleted(completedPhase);

        // Disable all weak points and the active phase controller.
        DeactivateAll();

        AudioManager.Instance.PlaySfx(AudioManager.Sfx.BossPhaseEnd);

        GD.Print($"Boss: Phase {completedPhase} defeated — transitioning (2 s).");

        // 2-second pause (Slice 11 will replace this with an animation signal).
        await ToSignal(
            GetTree().CreateTimer(2.0),
            SceneTreeTimer.SignalName.Timeout);

        // Safety: the boss may have been freed during the wait (e.g. scene unload).
        if (!IsInstanceValid(this))
            return;

        CompleteTransition();
    }

    /// <summary>
    /// Called after the transition pause finishes.  Advances the health phase,
    /// advances the FSM, then activates the next phase (or triggers defeat).
    /// </summary>
    private void CompleteTransition()
    {
        // Advance the pure-C# health tracker to the next phase and clear the lock.
        _health.AdvancePhase();

        // Advance the FSM: Transition → Phase2 / Phase3 / Defeated.
        _stateMachine.OnTransitionAnimationComplete();

        int newPhase = _stateMachine.CurrentPhase;

        if (_stateMachine.CurrentState == BossState.Defeated)
        {
            OnBossDefeated();
            return;
        }

        // Activate the new phase.
        ActivatePhase(newPhase);

        EventBus.Instance.EmitSignal(EventBus.SignalName.BossPhaseChanged, newPhase);
        EmitHpChanged();

        GD.Print($"Boss: Phase {newPhase} now active.");
    }

    // ── Phase activation helpers ──────────────────────────────────────────────

    /// <summary>
    /// Enables the weak point and phase controller for <paramref name="phase"/>
    /// and ensures all others are disabled.
    /// </summary>
    private void ActivatePhase(int phase)
    {
        // Disable everything first.
        DeactivateAll();

        // Enable the correct weak point (0-indexed array, 1-indexed phase).
        if (phase >= 1 && phase <= _weakPoints.Length)
            _weakPoints[phase - 1].SetActive(true);

        // Enable the matching phase controller (optional until Slice 6).
        var controller = phase switch
        {
            1 => _phase1Controller,
            2 => _phase2Controller,
            3 => _phase3Controller,
            _ => null,
        };
        SetControllerActive(controller, true);

        // Phase 3 also reveals the StatueNode.
        if (phase == 3)
            GetNode<Node2D>("StatueNode").Visible = true;
    }

    /// <summary>
    /// Disables all weak points and all phase controllers.
    /// Called at the start of every transition and before activating a new phase.
    /// </summary>
    private void DeactivateAll()
    {
        foreach (var wp in _weakPoints)
            wp.SetActive(false);

        SetControllerActive(_phase1Controller, false);
        SetControllerActive(_phase2Controller, false);
        SetControllerActive(_phase3Controller, false);
    }

    /// <summary>
    /// Calls <c>SetActive(bool)</c> on a phase controller node if it exists and
    /// exposes that method.  Guards against null (controller not yet implemented).
    /// </summary>
    private static void SetControllerActive(Node? controller, bool active)
    {
        if (controller is null) return;
        if (controller.HasMethod("SetActive"))
            controller.Call("SetActive", active);
    }

    // ── Defeat sequence ───────────────────────────────────────────────────────

    /// <summary>
    /// Triggered when <see cref="BossState.Defeated"/> is entered.
    /// Emits <see cref="EventBus.BossDefeated"/> and (in Slice 6) kicks off
    /// the alien passenger flee sequence.
    /// </summary>
    private void OnBossDefeated()
    {
        GD.Print("Boss: DEFEATED — starting defeat sequence.");

        EventBus.Instance.EmitSignal(EventBus.SignalName.BossDefeated);

        // Slice 6 (TICKET-606): instantiate AlienPassengerScene, call Detach(),
        // wire flee timer for good/bad ending.
        // Stub: just log for now.
        if (AlienPassengerScene is not null)
        {
            GD.Print("Boss: AlienPassengerScene assigned — implement flee in TICKET-606.");
        }
        else
        {
            // No alien scene assigned yet — emit a good ending immediately
            // so the win screen fires during Slice 5 testing.
            EventBus.Instance.EmitSignal(EventBus.SignalName.LevelComplete, true);
        }

        // Start the Demagogue's fall animation (Slice 11).
        // For now, free the boss node after a short delay.
        GetTree().CreateTimer(1.0).Timeout += QueueFree;
    }

    // ── HUD helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits <see cref="EventBus.BossHpChanged"/> with the current phase's HP
    /// values so the HUD health bar stays in sync.
    /// </summary>
    private void EmitHpChanged()
    {
        EventBus.Instance.EmitSignal(
            EventBus.SignalName.BossHpChanged,
            _health.CurrentHp,
            _health.MaxHp);
    }
}
