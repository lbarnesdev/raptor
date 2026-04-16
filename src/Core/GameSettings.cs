// src/Core/GameSettings.cs
// ─────────────────────────────────────────────────────────────────────────────
// Global settings store — Godot 4 autoload singleton.
//
// Registered in project.godot as:
//   [autoload]
//   GameSettings="*res://src/Core/GameSettings.cs"
//
// Autoload order in project.godot (top → bottom):
//   EventBus, GameSettings, AudioManager, GameManager
//
// GameSettings is listed BEFORE AudioManager so that AudioManager._Ready()
// can read GameSettings.Instance.MasterVolume when building the initial
// pool volume.  If the order were reversed, AudioManager would read a
// null Instance and fall back to its own default.
//
// Source-of-truth rule:
//   GameSettings owns ContentWarningEnabled and MasterVolume.
//   AudioManager.MasterVolume is a mirror that GameSettings keeps in sync
//   via the MasterVolume property setter.  Nothing else should write to
//   AudioManager.MasterVolume directly — always go through GameSettings.
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Core;

/// <summary>
/// Stores player-configurable settings that persist across scene changes.
/// Exposes <see cref="ContentWarningEnabled"/> and <see cref="MasterVolume"/>.
/// </summary>
public partial class GameSettings : Node
{
    // ── Singleton accessor ───────────────────────────────────────────────────

    /// <summary>
    /// The single autoloaded instance.  Safe to access from any node's
    /// <c>_Ready()</c> or later (provided GameSettings appears first in the
    /// autoload list).
    /// </summary>
    public static GameSettings Instance { get; private set; } = null!;

    // ── Backing fields ───────────────────────────────────────────────────────

    private bool  _contentWarningEnabled = true;
    private float _masterVolume          = 0.8f;

    // ── Settings properties ──────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, content-warning assets are shown (e.g. the original
    /// HateShuriken sprite).  When <c>false</c>, sanitised alternatives are
    /// substituted.  Driven by a toggle on the Settings screen.
    /// </summary>
    /// <remarks>
    /// <c>HateShuriken.cs</c> reads this property on every activation via
    /// <c>GameSettings.Instance.ContentWarningEnabled</c>.
    /// </remarks>
    [Export]
    public bool ContentWarningEnabled
    {
        get => _contentWarningEnabled;
        set => _contentWarningEnabled = value;
    }

    /// <summary>
    /// Linear master volume scalar in [0, 1].  Setting this property
    /// immediately syncs the value to <see cref="AudioManager.MasterVolume"/>
    /// so in-flight sounds pick up the change on the next play call.
    /// </summary>
    [Export]
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Mathf.Clamp(value, 0f, 1f);

            // Keep AudioManager in sync.  AudioManager may not be initialised
            // yet on first assignment during _Ready() — the null-conditional
            // guard handles that case; AudioManager reads from GameSettings
            // in its own _Ready() anyway.
            if (AudioManager.Instance is not null)
                AudioManager.Instance.MasterVolume = _masterVolume;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        // TODO (settings screen ticket): load persisted values from a
        // ConfigFile at "user://settings.cfg" here and apply them via the
        // property setters so AudioManager gets synced at startup.
    }
}
