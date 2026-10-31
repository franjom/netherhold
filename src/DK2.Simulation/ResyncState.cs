// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>One tick of a DK2 full-state capture, as <c>tools/oracle/capture_state.py</c> writes
/// it: the fields <see cref="WorldResync"/> copies into our world. Positions are DK2's fixed
/// point (4096 a tile), the same as ours; owners are player ids, 0 where the tile names no
/// player. The JSON is read by <c>DK2.Tools</c> (<c>ResyncStateFile</c>); fields it carries beyond
/// these (pointers) are ignored. <see cref="Flags"/> and <see cref="Timers"/> are the trigger
/// banks (world <c>+0x3F70</c>, <c>+0x3FF0</c>; trigger-state.md), a timer the tick it started.</summary>
public sealed record ResyncState(
    int Tick, int Width, int Height, ResyncPlayer[] Players, int[] Tiles, int[]? TileHealth,
    ResyncCreature[] Creatures, ResyncObject[]? Objects, uint Seed, ResyncMark[]? Marks = null,
    ResyncDoor[]? Doors = null, ResyncTrap[]? Traps = null, int[]? Flags = null, int[]? Timers = null,
    ResyncRoom[]? Rooms = null)
{
    public int Terrain(int index) => Tiles[3 * index];

    public int Owner(int index) => Tiles[(3 * index) + 1];
}

/// <summary><c>money</c> is player <c>+0x7E</c>, <c>mana</c> <c>+0x82</c> (player-object.md).</summary>
public sealed record ResyncPlayer(int Id, int Money, int Mana, ResyncManufacture? Manufacture = null);

/// <summary>The manufacture list (the push <c>0x005066E0</c>): the points word <c>player+0x217</c>
/// and the ring at <c>+0x219</c> from its head, each entry's cost, kind (0 a door, 1 a trap), type
/// and the blueprint's point; the ring's tail <c>+0x802</c> and the cost word in slot 0, which the
/// adder pays against (Q618). -1 is a capture written before they were read.</summary>
public sealed record ResyncManufacture(int Points, ResyncOrder[] Orders, int Tail = -1, int SlotZeroCost = -1);

public sealed record ResyncOrder(int Kind, int Type, int Cost, int X, int Y);

/// <summary>A door thing: its tile, type <c>+0xF6</c>, under construction while <c>+0x109</c> is set,
/// and health <c>+0x40</c>. A type of 0 is a capture written before the type was read.</summary>
public sealed record ResyncDoor(int Slot, int X, int Y, int Owner, int Type = 0, bool Constructing = false, int Health = 0);

/// <summary>A trap thing: its tile, type <c>+0xF6</c>, under construction while <c>+0x10C</c> bit 5
/// is set, and health <c>+0x40</c>.</summary>
public sealed record ResyncTrap(int Slot, int X, int Y, int Owner, int Type, bool Constructing, int Health);

/// <summary>The decoder's thing and creature fields, and <see cref="Level"/> <c>+0x403</c>;
/// <see cref="Job"/>, <see cref="Fear"/> and <see cref="FightReturn"/> only on a record long enough to
/// hold them. A level of 0 is a capture written before it was read; a null fight return likewise.</summary>
public sealed record ResyncCreature(
    int Slot, int Owner, int X, int Y, int Type, int State, int Health, ResyncJob? Job, ResyncFear? Fear = null,
    int Level = 0, ResyncFightReturn? FightReturn = null);

/// <summary>Where a creature withdrew from a fight, and when (ADR-0225): <c>OldFightPos</c>, two words
/// at <c>+0x52A</c>/<c>+0x52C</c> that the withdraw state both writes with the x tile, and
/// <c>OldFightTick</c> <c>+0x52E</c> (<c>0x004617FB</c>-<c>0x00461820</c>). State 1 walks back to it.</summary>
public sealed record ResyncFightReturn(int X, int Y, long Tick);

/// <summary>Fear (combat-fear.md): <c>0x0045E4F0</c> hands the words over, <c>0x0045EC80</c> counts; <c>TempFear</c>
/// <c>+0x524</c>, <c>CurrentFear</c> <c>+0x526</c>, <c>OverThreatCounter</c> <c>+0x528</c>.</summary>
public sealed record ResyncFear(int Temp, int Current, int Counter);

/// <summary>The state object at creature <c>+0x24E</c> (creature-job-object.md): the resume state
/// <c>+0xDF</c>, the task tile <c>+0x8C</c>/<c>+0x90</c>, side <c>+0x94</c>, slot <c>+0x96</c>, the
/// last task kind <c>+0x98</c>, <c>JustDropped</c> <c>+0x99</c>, the aimed kind <c>+0x9D</c> that
/// state 10 retries first (functions/00463DF0.md), <c>DroppedToDoJobId</c> <c>+0x6E</c> (the dumper's
/// name; not the aim, kept for the record), and the gold in hand (<c>+0x3FB</c>).</summary>
public sealed record ResyncJob(
    int Resume, int TaskX, int TaskY, int TaskId, int TaskSide, int TaskSlot, int LastKind,
    int JustDropped, int DroppedTask, int Gold, int AimedKind = 0);

/// <summary>A dig mark DK2 held at the tick: a kind-1 or kind-2 entry of the keeper's task pool,
/// which the capture records as a region (player-task-pool.md). <see cref="Order"/> counts up
/// from the list's tail, the head (raised last) largest, for the newest-first tie-break
/// (<see cref="TileState.TagOrder"/>).</summary>
public sealed record ResyncMark(int X, int Y, int Player, int Order);

/// <summary>A room record: type <c>+0x18</c>, player <c>+0x1D</c>, and the words <c>capture_state.py</c>
/// reads at <c>+0x49</c>, <c>+0x4B</c>, <c>+0x4D</c> and <c>+0x4F</c> as bounds. They are a union: a
/// Guard Room's bounds, a Portal's dwords <c>room[0x49]</c>, the turn of its last due roll, and
/// <c>room[0x4D]</c>, its live arrivals (portals-interval.md). <see cref="Active"/> is the slot's byte.</summary>
public sealed record ResyncRoom(int? Slot, bool Active, int Type, int Owner, int MinX, int MaxX, int MinY, int MaxY)
{
    public int PortalStamp => MinX | (MaxX << 16);

    public int PortalTally => MinY | (MaxY << 16);
}

/// <summary>An object thing: its type id, owner, point, flags <c>+0x146</c> and the dword at
/// <c>+0x14A</c>: gold's amount, a crate's kind.</summary>
public sealed record ResyncObject(int Slot, int Type, int Owner, int X, int Y, int Gold, int Flags);

/// <summary>The parts of a capture <see cref="WorldResync.Apply"/> copies. A creature part needs
/// <see cref="Creatures"/>, the pairing itself; without <see cref="Jobs"/> a worker is placed as any
/// other creature is.</summary>
[Flags]
public enum ResyncParts
{
    None = 0,
    /// <summary>Terrain, owner and health of every tile.</summary>
    Tiles = 1 << 0,
    /// <summary>The keepers' dig marks, in DK2's order.</summary>
    Marks = 1 << 1,
    /// <summary>Doors and traps.</summary>
    Doors = 1 << 2,
    /// <summary>Loose gold piles.</summary>
    Gold = 1 << 3,
    /// <summary>Crates.</summary>
    Crates = 1 << 4,
    /// <summary>Pairing every creature with ours, making DK2's extra ones and removing ours'.</summary>
    Creatures = 1 << 5,
    /// <summary>A paired creature's point, health and state.</summary>
    Pose = 1 << 6,
    /// <summary>A paired creature's level, raised to DK2's.</summary>
    Levels = 1 << 7,
    /// <summary>A paired creature's fear words and fight-return stamp.</summary>
    Fear = 1 << 8,
    /// <summary>A worker's job, gold in hand and last task.</summary>
    Jobs = 1 << 9,
    /// <summary>Each keeper's mana and manufacture list.</summary>
    Players = 1 << 10,
    /// <summary>The trigger flag and timer banks.</summary>
    Triggers = 1 << 11,
    /// <summary>DK2's generator seed, under <c>--rng dk2</c>.</summary>
    Seed = 1 << 12,
    /// <summary>Each Portal's clock and arrival tally.</summary>
    Portals = 1 << 13,
    All = (1 << 14) - 1,
}
