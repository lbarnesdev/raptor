# RAPTOR

Sci-fi horizontal shoot-em-up built with Godot 4 and C#. An F-22 pilot fights back against an alien-possessed presidential motorcade in a pulpy, satirical side-scroller.

**Engine:** Godot 4.4 · **Language:** C# (.NET 8) · **CI:** GitHub Actions (xUnit logic tests, no Godot install required)

---

## Gameplay

- Horizontal scrolling shooter. The camera auto-scrolls rightward at 120 px/s.
- Shoot down **WraithFighter** and **SpecterFighter** enemies and survive four **HarbingerTurrets** along the scroll path.
- Build a score **multiplier** by chaining kills without taking unshielded hits.
- Fire **homing missiles** (earned one per kill) when you need burst damage.
- Survive 160 seconds to reach the **Boss** — a three-phase fight against a demagogue's possessed presidential limo.

### Controls

| Action | Key / Axis |
|--------|-----------|
| Move | WASD or Arrow Keys |
| Fire plasma bolts | Space or Left Mouse |
| Fire missile salvo | F or Right Mouse |
| Pause | Escape |

---

## Repository Layout

```
raptor/
├── scenes/          # Godot .tscn scene files
│   ├── boss/        # Boss.tscn, WeakPoint.tscn
│   ├── enemies/     # BasicEnemy, WraithFighter, SpecterFighter, HarbingerTurret
│   ├── fx/          # ExplosionSmall.tscn, ExplosionLarge.tscn
│   ├── player/      # Player.tscn
│   ├── projectiles/ # PlasmaBolt, Missile, PlasmaBlob, CrystalSpine, OrganicSpore, …
│   ├── ui/          # HUD.tscn, MainMenu.tscn, GameOverScreen.tscn, WinScreen.tscn
│   └── world/       # Level01.tscn, SporeCloud.tscn
├── src/             # C# source (mirrors scenes/ structure)
│   ├── Boss/        # Boss.cs, Phase1Controller.cs … AlienPassenger.cs
│   ├── Core/        # GameManager.cs, EventBus.cs, AudioManager.cs, GameSettings.cs
│   ├── Enemies/     # BaseEnemy.cs, WraithFighter.cs, SpecterFighter.cs, HarbingerTurret.cs
│   ├── Logic/       # Pure-C# FSMs and systems (zero Godot deps — tested in CI)
│   ├── Player/      # Player.cs, ShieldController.cs, PlasmaWeapon.cs, MissileWeapon.cs
│   ├── Projectiles/ # All projectile scripts
│   ├── UI/          # HUD.cs, MainMenu.cs, …
│   └── World/       # ScrollCamera.cs, LevelDirector.cs, CheckpointManager.cs, …
├── assets/          # Art, audio, fonts (PNG tracked by Git LFS)
│   ├── audio/       # sfx/*.wav, music/*.ogg
│   └── sprites/     # boss/, enemies/, fx/, player/, projectiles/, terrain/, ui/
├── data/            # level_01_waves.json — timeline of spawn events
├── tests/           # xUnit test project (runs without Godot)
│   └── Logic/       # Tests for every src/Logic/ class
└── tools/           # gen_audio_stubs.py — generates silent audio placeholder files
```

---

## Development Setup

### Prerequisites

- [Godot 4.4](https://godotengine.org/download) with .NET support (the "Mono" or "C#" build)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Git LFS](https://git-lfs.com/) (required for binary assets — see below)

### Clone and open

```bash
git lfs install        # one-time, per machine
git clone https://github.com/your-org/raptor.git
cd raptor
# Open in Godot: File → Open Project → select this folder
```

### Run logic tests (no Godot needed)

The `src/Logic/` layer has zero Godot dependencies and can be tested standalone:

```bash
dotnet test tests/tests.csproj
```

This runs all xUnit tests in `tests/Logic/` covering the FSMs and pure-C# systems.

### Regenerate audio stubs

If you need silent placeholder WAV/OGG files (e.g. after a fresh checkout without LFS):

```bash
python3 tools/gen_audio_stubs.py
```

The script skips files that already exist, so running it on top of real audio is safe.

---

## Architecture Highlights

### Event Bus

All cross-system communication goes through `EventBus` (autoload singleton). Gameplay nodes emit signals; UI and managers subscribe. This keeps Godot nodes decoupled from each other.

Key signals: `ScoreChanged`, `LivesChanged`, `PlayerDied`, `BossSpawned`, `BossHpChanged`, `BossPhaseChanged`, `BossDefeated`, `ShieldStateChanged`, `MissileFired`, `AmmoGained`.

### Logic layer (testable without Godot)

Every system with meaningful branching logic lives in `src/Logic/` as a plain C# class:

| Class | Purpose |
|-------|---------|
| `ScoreSystem` | Kill scoring, multiplier tracking |
| `ShieldStateMachine` | Active → GracePeriod → Broken → Recharging cycle |
| `EnemyStateMachine` | FormationHold → AttackRun → Fleeing → Dead |
| `BossStateMachine` | Phase1 → Phase2 → Phase3 → Defeated |
| `BossPhaseHealth` | Per-phase hit-point tracking |
| `MissileSystem` | Ammo counting, fire-rate gating |
| `LevelDirectorTimeline` | JSON timeline parser and tick-driven event queue |
| `StateMachine<T>` | Generic base with `CanTransition` guard hook |

### Projectile pooling

`ProjectilePool` (scene-scoped, not an autoload) recycles all projectile instances to avoid runtime GC pressure. Indexed by `ProjectileType` enum.

### Audio

`AudioManager` (autoload) manages an 8-slot SFX player pool and two music players for seamless crossfading. Level music starts in `LevelDirector._Ready()` and crossfades at t=60s via the `CrossfadeMusic` timeline event.

---

## Content Warning Toggle

The boss's Phase 3 `HateShuriken` projectile has two sprite variants. Set `GameSettings.ContentWarningEnabled = false` to show the generic (non-offensive) sprite. The setting is accessed via `GameSettings.Instance` (autoload) and persists across sessions.

---

## Git LFS Setup

This repository uses [Git LFS](https://git-lfs.com/) to store binary assets (sprites, audio). You must install and initialise LFS **once per machine** before cloning or pushing.

**1 — Install Git LFS**

```bash
# Windows
winget install GitHub.GitLFS

# macOS
brew install git-lfs

# Ubuntu / Debian
sudo apt install git-lfs
```

**2 — Initialise (one-time per machine)**

```bash
git lfs install
```

**3 — Clone as normal**

```bash
git clone https://github.com/your-org/raptor.git
```

If assets appear as 1-line pointer stubs after cloning, run:

```bash
git lfs install
git lfs pull
```
