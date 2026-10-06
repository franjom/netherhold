// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One trap: projected from a placed THING, whose authored bytes remain verbatim, or placed
/// in play as a blueprint. The runtime words are DK2's <c>CTrap</c> fields past <c>+0xFF</c>,
/// zeroed by the constructor <c>0x004A7940</c> and run by the update <c>0x004A84E0</c> (traps.md).
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
public sealed class TrapState
{
    /// <summary>The trap's id, given as <see cref="DoorState.Id"/> is.</summary>
    public int Id { get; internal set; }

    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public byte NumberOfShots { get; init; }
    public byte TrapId { get; init; }

    /// <summary>The mixed-class tile chain link (<see cref="ThingTileLink"/>), given once by
    /// <see cref="WorldState.AddTrap"/>: DK2 constructs a trap on layer 0 (<c>0x004A7998</c>).</summary>
    public ThingTileLink TileLink { get; internal set; } = ThingTileLink.None;
    public byte PlayerId { get; init; }
    public byte Unknown_000F { get; init; }
    public bool UnderConstruction { get; set; }

    /// <summary><c>+0x40</c>, seeded from the definition's <c>Health</c> (<c>+0xA2</c>).</summary>
    public int Health;

    /// <summary><c>+0xF4</c>, 2048 to a turn, zeroed by the class constructor <c>0x004B28D0</c>.</summary>
    public int Heading;

    /// <summary><c>+0xFF</c> rechargeAtTime: no targeting before this turn.</summary>
    public long RechargeAt;

    /// <summary><c>+0x107</c> nextHealthGainTick: recovery applies once the turn is past it.</summary>
    public long RecoveryAt;

    /// <summary><c>+0x10B</c>: turns in a row a target was found, wrapping as a byte.</summary>
    public byte Charge;

    /// <summary><c>+0x10C</c>'s low nibble: completed own Trigger traps beside this one, which
    /// suppress the sight and tile selectors.</summary>
    public byte TriggerCount;

    /// <summary><c>+0x10C</c> bit 4, seeded from definition flag <c>0x20</c>; a firing with flag
    /// <c>0x2</c> clears it.</summary>
    public bool Hidden;

    /// <summary><c>+0x10E</c>, counted down each update and read by nothing else.</summary>
    public byte ThreatDuration;

    /// <summary><c>+0x10F</c>: the threat shared among nearby enemies every update once set; no
    /// trap code clears it after a firing sets it.</summary>
    public int Threat;

    /// <summary><c>+0x10D</c> triggeringTime: a Trigger's propagation countdown.</summary>
    public byte TriggerCountdown;

    /// <summary><c>+0x117</c> triggeringCreature: the creature a Trigger fired at.</summary>
    public int TriggerCreatureId;

    /// <summary><c>+0x119</c>/<c>+0x11D</c>: the sight finder's turn and answer, reused within a turn
    /// and offered to the next. The constructor zeroes both, so on turn 0 the finder answers nobody.</summary>
    public long SightTurn;
    public int SightTargetId;

    /// <summary><c>+0x113</c>, a Guard Post's room: the owner's Guard Room nearest the trap
    /// (<c>0x004C61C0(9)</c>), written by the constructor and refreshed by the update. Ours has no
    /// room ids, so it holds that room's first slab tile, or -1 for DK2's 0.</summary>
    public int GuardRoomTile = -1;

    /// <summary>The trap's position, the tile's centre as the loader <c>0x00520E50</c> puts it.</summary>
    public int PosX => (X << 12) + 0x800;
    public int PosY => (Y << 12) + 0x800;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Unknown_0008);
        hash.Add(NumberOfShots);
        hash.Add(TrapId);
        TileLink.HashInto(ref hash);
        hash.Add(PlayerId);
        hash.Add(Unknown_000F);
        hash.Add(UnderConstruction);
        hash.Add(Health);
        hash.Add(Heading);
        hash.Add(RechargeAt);
        hash.Add(RecoveryAt);
        hash.Add(Charge);
        hash.Add(TriggerCount);
        hash.Add(Hidden);
        hash.Add(ThreatDuration);
        hash.Add(Threat);
        hash.Add(TriggerCountdown);
        hash.Add(TriggerCreatureId);
        hash.Add(SightTurn);
        hash.Add(SightTargetId);
        hash.Add(GuardRoomTile);
    }
}
