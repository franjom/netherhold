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

    /// <summary>The initialiser <c>0x004BE330</c>: a keeper's clock is armed once, when it gains
    /// its first non-worker creature, by construction (<c>0x0048795E</c>) or a change of owner
    /// (<c>0x0048D24E</c>). A keeper with only imps never has a payday (payday.md §2.1). Ours
    /// reads "armed" as a non-zero deadline where DK2 keeps <c>player+0x76</c> bit <c>0x200</c>.</summary>
    public static void ArmOnGain(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker)) return;
        if (!world.HasPlayer(creature.OwnerPlayerId)) return;
        ref PlayerState player = ref world.Player(creature.OwnerPlayerId);
        if (player.NextPaydayTick.Value != 0) return;
        // Data without the variable (a test's) has no payday to arm.
        if (!database.Variables.TryGet(VariableTypeId.TimeBeforeFirstPayDaySeconds, out int seconds)) return;
        player.NextPaydayTick = new GameTick(world.CurrentTick.Value + (seconds * SimulationTiming.TicksPerSecond));
    }

    /// <summary>The constructor's <c>GetPaid</c> (<c>0x0048750C</c>): a creature made with fewer
    /// than <c>PayDayCutOffTimeSeconds</c> turns left before its keeper's payday skips that one.
    /// The 120 is compared raw with turns, so the window is 30 s. The subtraction is unsigned, so a
    /// passed deadline never sets it, nor does an unarmed clock (zero) after turn 0. Ours leaves
    /// turn 0 out too: whether DK2 builds a level's creatures on turn 0 is unread (payday.md §2.1).</summary>
    public static bool TooCloseToPayday(WorldState world, GameDatabase database, PlayerId ownerPlayerId)
    {
        if (!world.HasPlayer(ownerPlayerId)) return false;
        long deadline = world.Player(ownerPlayerId).NextPaydayTick.Value;
        if (deadline == 0) return false;
        if (!database.Variables.TryGet(VariableTypeId.PayDayCutOffTimeSeconds, out int cutOff)) return false;
        long left = deadline - world.CurrentTick.Value;
        return left >= 0 && left < cutOff * SimulationTiming.OriginalTickPeriodTicks;
    }

    /// <summary>Times a creature walks to a treasury, finds nothing and retries before giving up:
    /// two. Hardcoded as <c>creature[0x474] &lt; 2</c>, reset by the payday clock — per payday, not per life.</summary>
    public const int MaxFailedAttempts = 2;

    /// <summary>State 187: finds a treasury with gold and walks to it in state 6, resuming in
    /// <see cref="ArrivingState"/> (<c>payday.md</c> §4.2).</summary>
    public const int FindTheMoneyState = 187;

    /// <summary>State 188: checks the keeper still owns a treasury or heart, then moves to
    /// <see cref="TakeItState"/> (§4.3).</summary>
    public const int ArrivingState = 188;

    /// <summary>State 189: takes the wage from the room underfoot, then returns failure, which
    /// is idle, unless the Drink job takes over (§4.4, Q421).</summary>
    public const int TakeItState = 189;

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
    public static bool IsPayingRoomOf(RoomInstance room, PlayerId playerId) =>
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

    /// <summary>DK2's <c>0x004F4510</c>, shared by payday's 189 and the thief's 165: from
    /// <paramref name="start"/> round the room's tiles, one pile a tile, until
    /// <paramref name="wanted"/> is met or the ring is back at the start (Q435). So a creature on a
    /// bare tile of a full treasury is still paid; only an empty room pays nothing. Ours walks
    /// <see cref="RoomInstance.TileIndices"/> in index order, where DK2 follows each cell's link.</summary>
    public static int TakeFromRoom(WorldState world, RoomInstance room, int start, int wanted)
    {
        if (wanted <= 0) return 0;
        IReadOnlyList<int> tiles = room.TileIndices;
        int first = 0;
        while (first < tiles.Count && tiles[first] != start) first++;
        if (first == tiles.Count) first = 0;

        int taken = 0;
        for (int k = 0; k < tiles.Count && taken < wanted; k++)
        {
            ref TileState tile = ref world.TileAtIndex(tiles[(first + k) % tiles.Count]);
            int take = Math.Min(tile.GoldHeld, wanted - taken);
            tile.GoldHeld -= take;
            taken += take;
        }
        return taken;
    }
}
