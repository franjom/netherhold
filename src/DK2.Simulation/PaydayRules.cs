// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Payday: what a creature is owed, when it falls due, and where it collects.
/// <b>A journey, not a transaction</b> — DK2 credits the creature, then makes it walk to
/// a treasury and lift the gold off the floor, so payday can <i>fail</i>. Read from the
/// original (docs/research/payday.md, ADR-0078).</summary>
public static class PaydayRules
{
    /// <summary>How often payday falls: <c>PayDayFrequencySeconds</c>, <b>600</b> — ten minutes,
    /// as the owner said before the variable was read (Q9). Live: its global <c>0x006F25D1</c> is read in the payday clock (payday.md §2.1).</summary>
    public static int IntervalTicks(GameDatabase database) =>
        database[VariableTypeId.PayDayFrequencySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>When the <i>first</i> payday falls: <c>TimeBeforeFirstPayDaySeconds</c>, also
    /// <b>600</b>, so a keeper has ten minutes to dig a treasury. Read in the payday initialiser, its global's only reader.</summary>
    public static int FirstPaydayTicks(GameDatabase database) =>
        database[VariableTypeId.TimeBeforeFirstPayDaySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>How many times a creature walks to a treasury and finds nothing before
    /// giving up until the next payday: <b>two</b>. Hardcoded as <c>creature[0x474] &lt; 2</c> and reset by the clock, so two <b>per payday</b>, not per life.</summary>
    public const int MaxFailedAttempts = 2;

    /// <summary>What this creature is owed for one payday: its record's <c>Pay</c> scaled by
    /// the level table's <see cref="CreatureStatId.Pay"/> row, <b>250%</b> at level 10 (Q119). An imp is unpaid by its own <c>pay=0</c>, not by a rule.</summary>
    public static int Wage(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            creature.Pay, CreatureLevelRules.Percent(database, CreatureStatId.Pay, level));

    /// <summary>Whether this room can pay a creature: a <see cref="RoomTypeId.Treasury"/> or
    /// <see cref="RoomTypeId.DungeonHeart"/> of its own keeper, both from the original's
    /// search (types 1 and 5). The heart has a further condition on an undecoded flag byte, so it is admitted unconditionally — the wider reading (ADR-0078) — and it matters
    /// because the first treasury is paid for out of the heart (ADR-0015).</summary>
    public static bool IsPayingRoomOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId
        && room.Definition.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart;

    /// <summary>Whether this creature is owed wages it has not collected — the test DK2
    /// charges <c>AngerNoPay</c> on. Being owed is the whole trigger: the need-anger routine
    /// tests the outstanding wage and the level-scaled <c>Pay</c> against zero and nothing else, so <b>no treasury test and no "failed to collect" test</b> (payday.md §6.1, ADR-0079).</summary>
    public static bool IsOwedWages(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.WageOwed > 0 && Wage(database, creature, state.Level) > 0;

    /// <summary>How much gold this room holds across all its tiles. The original will not
    /// walk to an empty one, skipping any room whose per-room total is zero, so three empty treasuries and one full one send everybody to the full one.</summary>
    public static int GoldIn(WorldState world, RoomInstance room)
    {
        int total = 0;
        foreach (int index in room.TileIndices) total += world.TileAtIndex(index).GoldHeld;
        return total;
    }

    /// <summary>Takes up to <paramref name="wanted"/> gold off one tile and says how much
    /// came off. <b>One tile, the one underfoot</b> — the original's rule, not a
    /// simplification: a creature on an empty tile of a full treasury gets nothing and spends an attempt. Independent confirmation of ADR-0015's per-tile gold.</summary>
    public static int TakeFromTile(WorldState world, int tileIndex, int wanted)
    {
        if (wanted <= 0) return 0;

        ref TileState tile = ref world.TileAtIndex(tileIndex);
        int taken = Math.Min(tile.GoldHeld, wanted);
        tile.GoldHeld -= taken;
        return taken;
    }
}
