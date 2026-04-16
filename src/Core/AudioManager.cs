// src/Core/AudioManager.cs
// ─────────────────────────────────────────────────────────────────────────────
// Global audio manager — Godot 4 autoload singleton.
//
// Registered in project.godot as:
//   [autoload]
//   AudioManager="*res://src/Core/AudioManager.cs"
//
// Responsibilities:
//   • SFX playback via an 8-slot AudioStreamPlayer pool.  If all 8 slots are
//     busy the sound is silently dropped rather than hitching the frame.
//   • Music playback and smooth crossfading via two alternating
//     AudioStreamPlayer nodes (current + next).
//
// Audio bus setup (required in Godot's Audio bus layout editor):
//   Master → BGM (bus) → SFX (bus)
//   BGM and SFX buses let you apply independent compression / EQ and allow
//   volume sliders per-category in a settings screen.
//
// Music looping:
//   Loop must be enabled in the .import settings for each .ogg music file
//   (Godot Editor → select the file → Import tab → "Loop" checkbox).
//   AudioManager does not set looping programmatically because the property
//   lives on the concrete stream type (AudioStreamOggVorbis, AudioStreamMP3),
//   not on the abstract AudioStream base.
//
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Core;

/// <summary>
/// Provides fire-and-forget SFX playback and smooth music crossfading.
/// All callers use the string key API — no direct AudioStream references leak
/// outside this class.
/// </summary>
public partial class AudioManager : Node
{
    // ── Singleton accessor ───────────────────────────────────────────────────

    /// <summary>
    /// The single autoloaded instance.  Safe to access from any node's
    /// <c>_Ready()</c> or later (AudioManager is listed before other autoloads
    /// that depend on it in project.godot).
    /// </summary>
    public static AudioManager Instance { get; private set; } = null!;

    // ── Volume ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Linear master volume scalar (0.0 – 1.0).  Applied at play time to both
    /// SFX and music.  Persisted by <c>GameSettings.cs</c> — set this property
    /// from the settings screen, not from gameplay code.
    /// </summary>
    [Export] public float MasterVolume { get; set; } = 0.8f;

    // ── SFX pool ─────────────────────────────────────────────────────────────

    private const int SfxPoolSize = 8;

    /// <summary>
    /// Round-robin pool of <see cref="AudioStreamPlayer"/> nodes.
    /// Players return themselves to the queue via their Finished signal.
    /// </summary>
    private readonly Queue<AudioStreamPlayer> _sfxPool = new(SfxPoolSize);

    /// <summary>Loaded SFX streams keyed by the string constants below.</summary>
    private readonly Dictionary<string, AudioStream> _sfx = new();

    // ── Music players ────────────────────────────────────────────────────────

    /// <summary>Currently playing music track.</summary>
    private AudioStreamPlayer _currentMusic = null!;

    /// <summary>
    /// Inactive player used as the fade-in target during a crossfade.
    /// Swapped with <see cref="_currentMusic"/> once the fade completes.
    /// </summary>
    private AudioStreamPlayer _nextMusic = null!;

    /// <summary>Loaded music streams keyed by the string constants below.</summary>
    private readonly Dictionary<string, AudioStream> _music = new();

    /// <summary>
    /// Reference to the active crossfade tween so it can be killed if a
    /// second <see cref="CrossfadeTo"/> call arrives before the first finishes.
    /// </summary>
    private Tween? _crossfadeTween;

    // ── SFX key constants ────────────────────────────────────────────────────
    // These are the only strings that should appear in call-sites:
    //   AudioManager.Instance.PlaySfx(AudioManager.Sfx.PlasmaFire);

    public static class Sfx
    {
        public const string PlasmaFire      = "plasma_fire";
        public const string MissileFire     = "missile_fire";
        public const string MissileLock     = "missile_lock";
        public const string AmmoGain        = "ammo_gain";
        public const string ShieldBreak     = "shield_break";
        public const string ShieldRecharge  = "shield_recharge";
        public const string PlayerDeath     = "player_death";
        public const string EnemyExplode    = "enemy_explode";
        public const string TurretExplode   = "turret_explode";
        public const string BossHit         = "boss_hit";
        public const string BossPhaseEnd    = "boss_phase_end";
        public const string AlienDeath      = "alien_death";
        public const string LevelComplete   = "level_complete";
    }

    public static class Music
    {
        public const string Act1 = "music_act1";
        public const string Act2 = "music_act2";
        public const string Boss = "music_boss";
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        BuildSfxPool();
        LoadAllSfx();
        BuildMusicPlayers();
        LoadAllMusic();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Play a one-shot sound effect by key.  Silently drops the sound if all
    /// pool slots are busy — never stalls the frame.
    /// </summary>
    /// <param name="key">One of the <see cref="Sfx"/> constants.</param>
    public void PlaySfx(string key)
    {
        if (!_sfx.TryGetValue(key, out var stream))
        {
            GD.PushWarning($"AudioManager: Unknown SFX key '{key}'");
            return;
        }

        if (!_sfxPool.TryDequeue(out var player))
            return; // all 8 slots busy — drop rather than hitch

        player.Stream   = stream;
        player.VolumeDb = Mathf.LinearToDb(MasterVolume);
        player.Play();
        // player auto-returns to pool via the Finished signal wired in BuildSfxPool()
    }

    /// <summary>
    /// Begin playing a music track immediately (no fade).  Use this for the
    /// first track at level start.  For mid-level transitions, prefer
    /// <see cref="CrossfadeTo"/>.
    /// </summary>
    /// <param name="trackKey">One of the <see cref="Music"/> constants.</param>
    public void PlayMusic(string trackKey)
    {
        if (!_music.TryGetValue(trackKey, out var stream))
        {
            GD.PushWarning($"AudioManager: Unknown music key '{trackKey}'");
            return;
        }

        // Kill any crossfade already in progress.
        _crossfadeTween?.Kill();
        _crossfadeTween = null;

        _currentMusic.Stream   = stream;
        _currentMusic.VolumeDb = Mathf.LinearToDb(MasterVolume);
        _currentMusic.Play();
    }

    /// <summary>
    /// Smoothly crossfade from the currently playing music track to a new one.
    /// </summary>
    /// <param name="trackKey">One of the <see cref="Music"/> constants.</param>
    /// <param name="duration">Crossfade duration in seconds (e.g. 2.0f).</param>
    /// <remarks>
    /// Godot 4 C# tween syntax used here:
    /// <code>
    /// var tween = CreateTween();
    /// tween.TweenProperty(node, "volume_db", targetDb, duration);
    /// tween.Parallel().TweenProperty(otherNode, "volume_db", 0f, duration);
    /// await ToSignal(tween, Tween.SignalName.Finished);
    /// </code>
    /// <c>Parallel()</c> makes the second TweenProperty run simultaneously with
    /// the first rather than after it.  The default (without Parallel) is
    /// sequential.
    /// </remarks>
    public async void CrossfadeTo(string trackKey, float duration)
    {
        if (!_music.TryGetValue(trackKey, out var stream))
        {
            GD.PushWarning($"AudioManager: Unknown music key '{trackKey}'");
            return;
        }

        // Kill any crossfade that is already running, then start fresh.
        _crossfadeTween?.Kill();

        float targetDb = Mathf.LinearToDb(MasterVolume);

        // Prime the incoming track at silence.
        _nextMusic.Stream   = stream;
        _nextMusic.VolumeDb = -80f;
        _nextMusic.Play();

        // ── Crossfade tween ──────────────────────────────────────────────────
        // CreateTween() attaches the tween to this node's lifetime.
        // TweenProperty(target, propertyPath, finalValue, durationSecs)
        //   • "volume_db" is the Godot property name (snake_case string, not C# PascalCase).
        // Parallel() causes the next tweener to run at the same time as the previous one.
        _crossfadeTween = CreateTween();
        _crossfadeTween
            .TweenProperty(_currentMusic, "volume_db", -80f,     (double)duration);
        _crossfadeTween
            .Parallel()
            .TweenProperty(_nextMusic,    "volume_db", targetDb, (double)duration);

        // Await completion before swapping references.
        await ToSignal(_crossfadeTween, Tween.SignalName.Finished);

        // Swap current ↔ next so the next call to CrossfadeTo works correctly.
        _currentMusic.Stop();
        (_currentMusic, _nextMusic) = (_nextMusic, _currentMusic);

        // Reset the now-idle player to silence so it's ready for the next fade.
        _nextMusic.VolumeDb = -80f;
        _crossfadeTween     = null;
    }

    // ── Private setup helpers ─────────────────────────────────────────────────

    private void BuildSfxPool()
    {
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var player = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(player);

            // Wire return-to-pool ONCE per player.
            // `player` is captured per-iteration (C# for-loop body creates a
            // new scope each iteration), so each closure captures its own slot.
            player.Finished += () => _sfxPool.Enqueue(player);

            _sfxPool.Enqueue(player);
        }
    }

    private void BuildMusicPlayers()
    {
        _currentMusic = new AudioStreamPlayer { Bus = "BGM" };
        _nextMusic    = new AudioStreamPlayer { Bus = "BGM", VolumeDb = -80f };
        AddChild(_currentMusic);
        AddChild(_nextMusic);
    }

    private void LoadAllSfx()
    {
        TryLoadSfx(Sfx.PlasmaFire,     "res://assets/audio/sfx/plasma_fire.wav");
        TryLoadSfx(Sfx.MissileFire,    "res://assets/audio/sfx/missile_fire.wav");
        TryLoadSfx(Sfx.MissileLock,    "res://assets/audio/sfx/missile_lock.wav");
        TryLoadSfx(Sfx.AmmoGain,       "res://assets/audio/sfx/ammo_gain.wav");
        TryLoadSfx(Sfx.ShieldBreak,    "res://assets/audio/sfx/shield_break.wav");
        TryLoadSfx(Sfx.ShieldRecharge, "res://assets/audio/sfx/shield_recharge.wav");
        TryLoadSfx(Sfx.PlayerDeath,    "res://assets/audio/sfx/player_death.wav");
        TryLoadSfx(Sfx.EnemyExplode,   "res://assets/audio/sfx/enemy_explode.wav");
        TryLoadSfx(Sfx.TurretExplode,  "res://assets/audio/sfx/turret_explode.wav");
        TryLoadSfx(Sfx.BossHit,        "res://assets/audio/sfx/boss_hit.wav");
        TryLoadSfx(Sfx.BossPhaseEnd,   "res://assets/audio/sfx/boss_phase_end.wav");
        TryLoadSfx(Sfx.AlienDeath,     "res://assets/audio/sfx/alien_death.wav");
        TryLoadSfx(Sfx.LevelComplete,  "res://assets/audio/sfx/level_complete.wav");
    }

    private void LoadAllMusic()
    {
        TryLoadMusic(Music.Act1, "res://assets/audio/music/music_act1.ogg");
        TryLoadMusic(Music.Act2, "res://assets/audio/music/music_act2.ogg");
        TryLoadMusic(Music.Boss, "res://assets/audio/music/music_boss.ogg");
    }

    /// <summary>
    /// Loads a stream and stores it, or pushes a warning if the file is absent.
    /// Missing audio files should never crash the game — they just produce silence.
    /// </summary>
    private void TryLoadSfx(string key, string resPath)
    {
        var stream = GD.Load<AudioStream>(resPath);
        if (stream is null)
            GD.PushWarning($"AudioManager: SFX file not found — {resPath}");
        else
            _sfx[key] = stream;
    }

    private void TryLoadMusic(string key, string resPath)
    {
        var stream = GD.Load<AudioStream>(resPath);
        if (stream is null)
            GD.PushWarning($"AudioManager: Music file not found — {resPath}");
        else
            _music[key] = stream;
    }
}
