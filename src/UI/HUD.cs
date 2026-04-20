// src/UI/HUD.cs
// ─────────────────────────────────────────────────────────────────────────────
// Full HUD for Slice 7 — replaces the old inline ScoreLabel + BossHealthBar
// nodes that used to live directly in Level01.tscn.
//
// Node placement (HUD.tscn — instanced inside Level01.tscn):
//   HUD (CanvasLayer)          ← this script
//   ├── ScoreLabel  (Label)               top-right
//   ├── ShieldIcon  (ColorRect 40×40)     top-left
//   ├── LifeIcon1/2/3  (ColorRect 30×22)  top-left, right of shield
//   ├── MissileIcon1…6 (ColorRect 20×28)  below life icons (stub until Slice 9)
//   └── BossHealthBar (Control)           bottom of screen, hidden until boss
//       ├── Seg1 (ProgressBar)
//       ├── Seg2 (ProgressBar)
//       └── Seg3 (ProgressBar)
//
// Subscribed signals:
//   ScoreChanged       → ScoreLabel text
//   ShieldStateChanged → ShieldIcon colour + optional alpha pulse
//   LivesChanged       → LifeIcon1/2/3 colour (blue=alive, grey=lost)
//   BossSpawned        → BossHealthBar.Visible = true; reset segments
//   BossHpChanged      → active segment MaxValue / Value
//   BossPhaseChanged   → grey old segment, white new segment
//   BossDefeated       → BossHealthBar.Visible = false
//   MissileFired       → stub (Slice 9)
//   AmmoGained         → stub (Slice 9)
//
// Shield pulse:
//   GracePeriod and Recharging both set ShieldIcon to red and start a looping
//   Tween that oscillates color:a between 0.3 and 1.0 at ~3 Hz (0.15 s per leg).
//   The tween is killed and nulled before every state change, so colour
//   assignments in other states are never clobbered by a stale tween.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using Raptor.Core;

namespace Raptor.UI;

/// <summary>
/// Single CanvasLayer that owns all in-game HUD elements for Slice 7:
/// score, shield state, lives, boss health bar, and missile ammo stubs.
/// </summary>
public partial class HUD : CanvasLayer
{
    // ── Cached node references ────────────────────────────────────────────────

    private Label       _scoreLabel    = null!;
    private ColorRect   _shieldIcon    = null!;
    private ColorRect[] _lifeIcons     = null!;
    private ColorRect[] _missileIcons  = null!;
    private Control     _bossBar       = null!;
    private ProgressBar _seg1          = null!;
    private ProgressBar _seg2          = null!;
    private ProgressBar _seg3          = null!;

    // ── Runtime state ─────────────────────────────────────────────────────────

    /// <summary>
    /// Active looping tween that pulses <c>ShieldIcon.color:a</c>.
    /// Null when the shield is not in a warning state.
    /// </summary>
    private Tween? _shieldPulse;

    /// <summary>Which boss phase segment (1–3) is currently live.</summary>
    private int _currentPhase = 1;

    // ── Colours ───────────────────────────────────────────────────────────────

    /// <summary>Shield icon colour when active and blocking.</summary>
    private static readonly Color ShieldActive  = new(0.3f, 0.7f, 1.0f, 1.0f);

    /// <summary>Shield icon colour during GracePeriod and Recharging (pulsing red).</summary>
    private static readonly Color ShieldWarning = new(1.0f, 0.3f, 0.3f, 1.0f);

    /// <summary>Shield icon colour when the shield is fully broken.</summary>
    private static readonly Color ShieldBroken  = new(0.4f, 0.4f, 0.4f, 1.0f);

    /// <summary>Life icon colour while that life is still available.</summary>
    private static readonly Color LifeOn        = new(0.2f, 0.7f, 1.0f, 1.0f);

    /// <summary>Life icon colour after the life has been spent.</summary>
    private static readonly Color LifeOff       = new(0.3f, 0.3f, 0.3f, 1.0f);

    /// <summary>Missile icon placeholder colour (all-ready stub for Slice 9).</summary>
    private static readonly Color MissileReady  = new(1.0f, 0.9f, 0.2f, 1.0f);

    /// <summary>Boss health segment tint when it is the active phase.</summary>
    private static readonly Color SegActive     = Colors.White;

    /// <summary>Boss health segment tint when completed or not yet active.</summary>
    private static readonly Color SegGrey       = new(0.45f, 0.45f, 0.45f, 1.0f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // ── Resolve children ──────────────────────────────────────────────────
        _scoreLabel = GetNode<Label>("ScoreLabel");
        _shieldIcon = GetNode<ColorRect>("ShieldIcon");

        _lifeIcons = new[]
        {
            GetNode<ColorRect>("LifeIcon1"),
            GetNode<ColorRect>("LifeIcon2"),
            GetNode<ColorRect>("LifeIcon3"),
        };

        _missileIcons = new[]
        {
            GetNode<ColorRect>("MissileIcon1"),
            GetNode<ColorRect>("MissileIcon2"),
            GetNode<ColorRect>("MissileIcon3"),
            GetNode<ColorRect>("MissileIcon4"),
            GetNode<ColorRect>("MissileIcon5"),
            GetNode<ColorRect>("MissileIcon6"),
        };

        _bossBar = GetNode<Control>("BossHealthBar");
        _seg1    = GetNode<ProgressBar>("BossHealthBar/Seg1");
        _seg2    = GetNode<ProgressBar>("BossHealthBar/Seg2");
        _seg3    = GetNode<ProgressBar>("BossHealthBar/Seg3");

        // ── Initial visual state ──────────────────────────────────────────────
        SetScore(0, 1);
        SetShield("Active");
        SetLives(3);
        _bossBar.Visible = false;

        // Missile icons show yellow as an "all loaded" placeholder until
        // Slice 9 wires up the real ammo system (TICKET-903).
        foreach (var icon in _missileIcons)
            icon.Color = MissileReady;

        // ── Subscribe ─────────────────────────────────────────────────────────
        EventBus.Instance.ScoreChanged       += OnScoreChanged;
        EventBus.Instance.ShieldStateChanged += OnShieldStateChanged;
        EventBus.Instance.LivesChanged       += OnLivesChanged;
        EventBus.Instance.BossSpawned        += OnBossSpawned;
        EventBus.Instance.BossHpChanged      += OnBossHpChanged;
        EventBus.Instance.BossPhaseChanged   += OnBossPhaseChanged;
        EventBus.Instance.BossDefeated       += OnBossDefeated;
        EventBus.Instance.MissileFired       += OnMissileFired;
        EventBus.Instance.AmmoGained         += OnAmmoGained;
    }

    public override void _ExitTree()
    {
        // Disconnect before freeing to prevent callbacks into a freed node.
        EventBus.Instance.ScoreChanged       -= OnScoreChanged;
        EventBus.Instance.ShieldStateChanged -= OnShieldStateChanged;
        EventBus.Instance.LivesChanged       -= OnLivesChanged;
        EventBus.Instance.BossSpawned        -= OnBossSpawned;
        EventBus.Instance.BossHpChanged      -= OnBossHpChanged;
        EventBus.Instance.BossPhaseChanged   -= OnBossPhaseChanged;
        EventBus.Instance.BossDefeated       -= OnBossDefeated;
        EventBus.Instance.MissileFired       -= OnMissileFired;
        EventBus.Instance.AmmoGained         -= OnAmmoGained;
    }

    // ── Score ─────────────────────────────────────────────────────────────────

    private void OnScoreChanged(int score, int multiplier) => SetScore(score, multiplier);

    private void SetScore(int score, int multiplier)
        => _scoreLabel.Text = $"SCORE: {score:000000}  ×{multiplier}";

    // ── Shield ────────────────────────────────────────────────────────────────

    private void OnShieldStateChanged(string state) => SetShield(state);

    /// <summary>
    /// Update the shield icon.  Kills any running pulse tween first so colours
    /// set for non-pulsing states are never overwritten by a stale animation.
    /// </summary>
    private void SetShield(string state)
    {
        _shieldPulse?.Kill();
        _shieldPulse = null;

        switch (state)
        {
            case "Active":
                _shieldIcon.Color = ShieldActive;
                break;

            case "GracePeriod":
            case "Recharging":
                _shieldIcon.Color = ShieldWarning;
                // Loop: fade out 0.15 s then fade in 0.15 s ≈ 3 pulses per second.
                _shieldPulse = CreateTween().SetLoops();
                _shieldPulse.TweenProperty(_shieldIcon, "color:a", 0.3f, 0.15);
                _shieldPulse.TweenProperty(_shieldIcon, "color:a", 1.0f, 0.15);
                break;

            case "Broken":
                _shieldIcon.Color = ShieldBroken;
                break;
        }
    }

    // ── Lives ─────────────────────────────────────────────────────────────────

    private void OnLivesChanged(int remaining) => SetLives(remaining);

    /// <summary>
    /// Lights up the first <paramref name="remaining"/> life icons blue and
    /// greys out the rest.
    /// </summary>
    private void SetLives(int remaining)
    {
        for (int i = 0; i < _lifeIcons.Length; i++)
            _lifeIcons[i].Color = i < remaining ? LifeOn : LifeOff;
    }

    // ── Boss health bar ───────────────────────────────────────────────────────

    private void OnBossSpawned()
    {
        _currentPhase = 1;

        // All segments grey+full until BossHpChanged fires with real phase HP.
        foreach (var seg in new[] { _seg1, _seg2, _seg3 })
        {
            seg.Value    = seg.MaxValue;
            seg.Modulate = SegGrey;
        }
        _seg1.Modulate = SegActive;  // Phase 1 is the starting active segment.

        _bossBar.Visible = true;
    }

    private void OnBossHpChanged(int current, int max)
    {
        var seg = SegmentForPhase(_currentPhase);
        if (seg is null) return;
        seg.MaxValue = max;
        seg.Value    = current;
    }

    private void OnBossPhaseChanged(int phase)
    {
        // Zero and grey the just-completed segment.
        var oldSeg = SegmentForPhase(_currentPhase);
        if (oldSeg is not null)
        {
            oldSeg.Value    = 0;
            oldSeg.Modulate = SegGrey;
        }

        _currentPhase = phase;

        // Light up the incoming segment; BossHpChanged fires immediately after
        // to set the correct MaxValue/Value for the new phase.
        var newSeg = SegmentForPhase(_currentPhase);
        if (newSeg is not null)
            newSeg.Modulate = SegActive;
    }

    private void OnBossDefeated() => _bossBar.Visible = false;

    private ProgressBar? SegmentForPhase(int phase) => phase switch
    {
        1 => _seg1,
        2 => _seg2,
        3 => _seg3,
        _ => null,
    };

    // ── Missiles — stub (Slice 9 / TICKET-903) ────────────────────────────────

    /// <summary>Placeholder — wired up in TICKET-903 when missile system lands.</summary>
    private void OnMissileFired(int remaining) { /* TODO TICKET-903 */ }

    /// <summary>Placeholder — wired up in TICKET-903 when missile system lands.</summary>
    private void OnAmmoGained(int total) { /* TODO TICKET-903 */ }
}
