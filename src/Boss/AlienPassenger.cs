// src/Boss/AlienPassenger.cs
// ─────────────────────────────────────────────────────────────────────────────
// STUB — compile placeholder only.  Full implementation: TICKET-606.
//
// The alien passenger is always visible on the boss body and subtly animates
// (eye blinks, tentacle twitch).  After Phase 3 dies, Boss.cs calls Detach()
// to start the flee sequence: the alien reparents itself to the level root,
// moves right across the screen for up to 5 seconds, and can be shot by the
// player for the good ending.
//
// Node placement (Boss.tscn):
//   Boss (Node2D)
//   └── AlienPassenger (Node2D)  ← this script
//       ├── AlienSprite (AnimatedSprite2D)  ← idle / flee animations (Slice 11)
//       └── HitArea (Area2D)               ← layer 9 (AlienFlee), mask 16 (PlayerProjectile)
//           └── CollisionShape2D           ← enabled only during flee
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace Raptor.Boss;

/// <summary>
/// The alien creature controlling The Demagogue.  Always visible on the boss
/// body.  After Phase 3 is destroyed, <see cref="Detach"/> kicks off the flee
/// sequence — the alien escapes across the screen and can be shot for the
/// good ending.
/// <para>
/// This is a compile stub.  TICKET-606 replaces this file with the full
/// flee sequence, 5-second timer, and good/bad ending EventBus emissions.
/// </para>
/// </summary>
public partial class AlienPassenger : Node2D
{
    /// <summary>
    /// Called by <see cref="Boss.OnBossDefeated"/> when Phase 3 HP reaches 0.
    /// Full implementation in TICKET-606.
    /// </summary>
    public void Detach()
    {
        GD.Print("[AlienPassenger stub] Detach() called — implement in TICKET-606.");
    }
}
