#!/usr/bin/env python3
"""
tools/gen_audio_stubs.py
────────────────────────────────────────────────────────────────────────────
Generates silent placeholder audio files under assets/audio/ so Godot can
import and compile the project without missing-file errors.

Run from the repository root:
    python3 tools/gen_audio_stubs.py

Outputs:
    assets/audio/sfx/<key>.wav    — 0.1 s, 44 100 Hz, 16-bit mono, silent
    assets/audio/music/<key>.ogg  — 2 s, 44 100 Hz, stereo, silent (via ffmpeg)

Existing files are NOT overwritten so real assets are safe.
"""

import os
import struct
import subprocess
import sys
import tempfile
import wave

# ── Paths ─────────────────────────────────────────────────────────────────────

REPO_ROOT  = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SFX_DIR    = os.path.join(REPO_ROOT, "assets", "audio", "sfx")
MUSIC_DIR  = os.path.join(REPO_ROOT, "assets", "audio", "music")

# ── Asset lists ───────────────────────────────────────────────────────────────

SFX_KEYS = [
    "plasma_fire",
    "missile_fire",
    "missile_lock",
    "ammo_gain",
    "shield_break",
    "shield_recharge",
    "player_death",
    "enemy_shoot",
    "enemy_explode",
    "turret_explode",
    "boss_hit",
    "boss_phase_end",
    "alien_death",
    "level_complete",
]

MUSIC_KEYS = [
    "music_act1",
    "music_act2",
    "music_boss",
]

# ── Helpers ───────────────────────────────────────────────────────────────────

def make_silent_wav(path: str, duration_s: float = 0.1,
                    sample_rate: int = 44_100, channels: int = 1) -> None:
    """Write a minimal silent WAV file.  Skips if the file already exists."""
    if os.path.exists(path):
        print(f"  skip  {os.path.relpath(path, REPO_ROOT)}  (already exists)")
        return

    n_frames = int(sample_rate * duration_s)
    with wave.open(path, "w") as wf:
        wf.setnchannels(channels)
        wf.setsampwidth(2)          # 16-bit
        wf.setframerate(sample_rate)
        wf.writeframes(b"\x00" * n_frames * channels * 2)

    print(f"  wrote {os.path.relpath(path, REPO_ROOT)}")


def write_silent_wav_raw(path: str, duration_s: float,
                         sample_rate: int, channels: int) -> None:
    """Write a silent WAV unconditionally (no skip guard — for temp files)."""
    n_frames = int(sample_rate * duration_s)
    with wave.open(path, "w") as wf:
        wf.setnchannels(channels)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(b"\x00" * n_frames * channels * 2)


def make_silent_ogg(path: str, duration_s: float = 2.0,
                    sample_rate: int = 44_100, channels: int = 2) -> None:
    """
    Write a silent OGG Vorbis file via ffmpeg.
    Uses a silent WAV temp file as input.  Skips if the file already exists.
    """
    if os.path.exists(path):
        print(f"  skip  {os.path.relpath(path, REPO_ROOT)}  (already exists)")
        return

    # Use delete=False + manual cleanup so we can write to it after creation.
    fd, tmp_wav = tempfile.mkstemp(suffix=".wav")
    os.close(fd)

    try:
        # Write silent PCM data into the temp WAV (bypasses the skip guard).
        write_silent_wav_raw(tmp_wav, duration_s=duration_s,
                             sample_rate=sample_rate, channels=channels)

        # Convert to OGG Vorbis using ffmpeg.
        result = subprocess.run(
            [
                "ffmpeg", "-y",
                "-i", tmp_wav,
                "-c:a", "libvorbis",
                "-q:a", "0",    # lowest quality (0) = smallest file
                path,
            ],
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            print(f"  ERROR ffmpeg failed for {path}:\n{result.stderr}", file=sys.stderr)
        else:
            print(f"  wrote {os.path.relpath(path, REPO_ROOT)}")
    finally:
        os.unlink(tmp_wav)


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    os.makedirs(SFX_DIR,   exist_ok=True)
    os.makedirs(MUSIC_DIR, exist_ok=True)

    print("\n── SFX stubs (WAV) ──────────────────────────────────────────────────")
    for key in SFX_KEYS:
        make_silent_wav(os.path.join(SFX_DIR, f"{key}.wav"))

    print("\n── Music stubs (OGG via ffmpeg) ─────────────────────────────────────")
    for key in MUSIC_KEYS:
        make_silent_ogg(os.path.join(MUSIC_DIR, f"{key}.ogg"))

    print("\nDone.\n")


if __name__ == "__main__":
    main()
