// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>One tick of a DK2 full-state capture, as <c>tools/oracle/capture_state.py</c> writes
/// it: the fields <see cref="WorldResync"/> copies into our world. Positions are DK2's fixed
/// point (4096 a tile), the same as ours; owners are player ids, 0 where the tile names no
/// player. The JSON is read by <c>DK2.Tools</c> (<c>ResyncStateFile</c>); fields it carries beyond
/// these (rooms, doors, pointers) are ignored.</summary>
public sealed record ResyncState(
    int Tick, int Width, int Height, ResyncPlayer[] Players, int[] Tiles, int[]? TileHealth,
    ResyncCreature[] Creatures, ResyncObject[]? Objects, uint Seed, ResyncMark[]? Marks = null)
{
    public int Terrain(int index) => Tiles[3 * index];

    public int Owner(int index) => Tiles[(3 * index) + 1];
}

/// <summary><c>money</c> is player <c>+0x7E</c>, <c>mana</c> <c>+0x82</c> (player-object.md).</summary>
public sealed record ResyncPlayer(int Id, int Money, int Mana);

/// <summary>The decoder's thing and creature fields; <see cref="Job"/> only on a record long
/// enough to hold the state object.</summary>
public sealed record ResyncCreature(
    int Slot, int Owner, int X, int Y, int Type, int State, int Health, ResyncJob? Job);

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

/// <summary>An object thing: its type id, owner, point and, for gold, the amount.</summary>
public sealed record ResyncObject(int Slot, int Type, int Owner, int X, int Y, int Gold, int Flags);
