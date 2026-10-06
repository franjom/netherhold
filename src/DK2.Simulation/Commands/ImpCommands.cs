// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>
/// Casts Create Imp: buy one imp at the dungeon heart for mana.
/// <para>CONFIRMED (owner, 2026-08-30) that the spell exists, costs 1500 for the
/// first and rises by 1500 each time, and that 1500 is the number on DK2's own
/// <c>Create Imp</c> record. HYPOTHESIS: the exact form of the rise — see
/// <see cref="ImpSupplyRules.CreateImpCost"/> and Q54.</para>
/// <para>The original casts onto a tile (packet action 54) and the imp appears there.
/// <paramref name="At"/> is that tile; without one the imp appears at the dungeon heart.
/// The position permission (<c>0x004BB8A0</c>, keeper-spell-casting.md §3) applies as the
/// data selects it for spell 4: the row AVAILABLE, location 1 and target 4 at the tile, so a
/// cast onto land the keeper does not own makes nothing (ADR-0312).</para>
/// </summary>
public sealed record CreateImpCommand(PlayerId PlayerId, TilePos? At = null) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(At is { } at ? at.Y * 65536 + at.X : -1);
    }
}
