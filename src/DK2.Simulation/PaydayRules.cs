// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Payday: what a creature is owed, when it falls due, and where it collects. DK2
/// credits the creature, then makes it walk to a treasury and lift the gold off the floor,
/// so payday can fail (docs/research/payday.md, ADR-0078).</summary>
public static class PaydayRules
{
    /// <summary>Payday interval: <c>PayDayFrequencySeconds</c>, 600 s — the owner's ten minutes,
    /// then the variable (Q9). Its global <c>0x006F25D1</c> is read in the clock (payday.md §2.1).</summary>
    public static int IntervalTicks(GameDatabase database) =>
        database[VariableTypeId.PayDayFrequencySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>First payday: <c>TimeBeforeFirstPayDaySeconds</c>, also 600 s, so a keeper has ten
    /// minutes to dig a treasury. The payday initialiser is its global's only reader.</summary>
    public static int FirstPaydayTicks(GameDatabase database) =>
        database[VariableTypeId.TimeBeforeFirstPayDaySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>Times a creature walks to a treasury, finds nothing and retries before giving up:
    /// two. Hardcoded as <c>creature[0x474] &lt; 2</c>, reset by the payday clock — per payday, not per life.</summary>
    public const int MaxFailedAttempts = 2;

    /// <summary>What this creature is owed for one payday: its record's <c>Pay</c> scaled by the
    /// level table's <see cref="CreatureStatId.Pay"/> row, 105 % at L2 rising to 250 % at L10
    /// (Q119). An imp is unpaid by its own <c>pay=0</c>, not by a rule.</summary>
    public static int Wage(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            creature.Pay, CreatureLevelRules.Percent(database, CreatureStatId.Pay, level));

    /// <summary>Whether this room can pay a creature: a <see cref="RoomTypeId.Treasury"/> or
    /// <see cref="RoomTypeId.DungeonHeart"/> of its own keeper (the original searches types 1 and
    /// 5). The heart's further condition is on an undecoded flag byte, so it is admitted
    /// unconditionally (ADR-0078); it also pays for the first treasury (ADR-0015).</summary>
    public static bool IsPayingRoomOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId
        && room.Definition.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart;

    /// <summary>Whether this creature is owed wages it has not collected — the test DK2 charges
    /// <c>AngerNoPay</c> on. The need-anger routine tests the outstanding wage and the level-scaled
    /// <c>Pay</c> against zero and nothing else: no treasury test, no "failed to collect" test (payday.md §6.1, ADR-0079).</summary>
    public static bool IsOwedWages(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.WageOwed > 0 && Wage(database, creature, state.Level) > 0;

    /// <summary>How much gold this room holds across all its tiles. The original skips any room
    /// whose total is zero, so three empty treasuries and one full one send everybody to the full one.</summary>
    public static int GoldIn(WorldState world, RoomInstance room)
    {
        int total = 0;
        foreach (int index in room.TileIndices) total += world.TileAtIndex(index).GoldHeld;
        return total;
    }

    /// <summary>Takes up to <paramref name="wanted"/> gold off one tile and says how much came
    /// off. One tile, the one underfoot, is the original's rule: a creature on an empty tile of a
    /// full treasury gets nothing and spends an attempt (ADR-0015 per-tile gold).</summary>
    public static int TakeFromTile(WorldState world, int tileIndex, int wanted)
    {
        if (wanted <= 0) return 0;

        ref TileState tile = ref world.TileAtIndex(tileIndex);
        int taken = Math.Min(tile.GoldHeld, wanted);
        tile.GoldHeld -= taken;
        return taken;
    }
}
