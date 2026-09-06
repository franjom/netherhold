// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Payday: what a creature is owed, when it falls due, and where it goes to
/// collect.
///
/// <para><b>Read from the original</b> — the clock, the wage and the three
/// creature states are all decompiled, with addresses and evidence in
/// <c>docs/research/payday.md</c>. Decisions: ADR-0078.</para>
///
/// <para>The shape worth holding on to is that <b>payday is a journey, not a
/// transaction.</b> DK2 credits the creature and then makes it walk to a
/// treasury and lift the gold off the floor itself, which means payday can
/// <i>fail</i> — and failing is the interesting half, because it is what a
/// keeper who has overspent actually experiences.</para>
/// </summary>
public static class PaydayRules
{
    /// <summary>
    /// How often payday falls: <c>PayDayFrequencySeconds</c>, which ships at
    /// <b>600</b> — ten minutes, which is what the owner said before the
    /// variable was looked at (Q9).
    ///
    /// <para><b>The variable is live</b>, and that was checked rather than
    /// assumed: its global <c>0x006F25D1</c> has two references, the setter stub
    /// and one read inside the payday clock itself. Four shipped balance
    /// variables in this project have turned out to be read by nothing at all,
    /// so the scan is the price of building on a name.</para>
    /// </summary>
    public static int IntervalTicks(GameDatabase database) =>
        database[VariableTypeId.PayDayFrequencySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// When the <i>first</i> payday falls: <c>TimeBeforeFirstPayDaySeconds</c>,
    /// also 600. So a level does not open with everybody owed money — the first
    /// wage bill arrives ten minutes in, and a keeper has that long to dig out a
    /// treasury before it matters.
    ///
    /// <para>Read in the original's payday initialiser, which is the only reader
    /// of the variable's global.</para>
    /// </summary>
    public static int FirstPaydayTicks(GameDatabase database) =>
        database[VariableTypeId.TimeBeforeFirstPayDaySeconds] * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// How many times a creature will walk to a treasury and find nothing
    /// before it stops trying until the next payday: <b>two</b>.
    ///
    /// <para>Hardcoded in the original as <c>creature[0x474] &lt; 2</c>, tested
    /// in the decision that sends a creature to collect, incremented by the
    /// collecting state when the tile it reached had no gold, and reset to zero
    /// for every creature by the clock. So the budget is two attempts <b>per
    /// payday</b>, not two in a creature's life.</para>
    /// </summary>
    public const int MaxFailedAttempts = 2;

    /// <summary>
    /// What this creature is owed for one payday: its record's <c>Pay</c>,
    /// scaled by its level.
    ///
    /// <para><b>The scale is the shipped level table</b>, row
    /// <see cref="CreatureStatId.Pay"/>, which runs to <b>250%</b> at level 10 —
    /// so a veteran costs two and a half times what it did when it walked out of
    /// the portal. That row has been parsed since variables were first read and
    /// nothing has ever looked at it (Q119); this is its first consumer.</para>
    ///
    /// <para><b>An imp is unpaid, and that falls out of the data.</b> The Imp's
    /// record carries <c>pay=0</c>, so its wage is zero, so it is never owed
    /// anything and never goes to collect. No rule here names the imp — which is
    /// how it should be, and is the same shape as the Casino's fifteen states
    /// falling out of a flag byte rather than out of a list.</para>
    /// </summary>
    public static int Wage(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            creature.Pay, CreatureLevelRules.Percent(database, CreatureStatId.Pay, level));

    /// <summary>
    /// Whether this room is one a creature can be paid out of: a
    /// <see cref="RoomTypeId.Treasury"/> or a
    /// <see cref="RoomTypeId.DungeonHeart"/>, owned by the creature's own
    /// keeper.
    ///
    /// <para><b>Both types are the original's, read from its search.</b> It
    /// walks the player's room chain and keeps room type 1 or room type 5, and
    /// the heart carries an extra condition on a flag byte this project has not
    /// decoded — so the heart is admitted unconditionally here, which is the
    /// wider reading and is stated as such in ADR-0078.</para>
    ///
    /// <para>The heart mattering at all is not decoration: a keeper's first
    /// treasury is paid for out of the dungeon heart (ADR-0015), so a keeper who
    /// has not built one yet still has somewhere for wages to come from.</para>
    /// </summary>
    public static bool IsPayingRoomOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId
        && room.Definition.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart;

    /// <summary>
    /// Whether this creature is owed wages it has not collected — the test DK2
    /// charges <c>AngerNoPay</c> on.
    ///
    /// <para><b>Being owed is the whole trigger.</b> The original's need-anger
    /// routine tests the outstanding wage against zero and the creature's own
    /// level-scaled <c>Pay</c> against zero, and that is all: <b>there is no
    /// treasury test and no "did it fail to collect" test.</b> A keeper with no
    /// vault at all and a keeper whose creature simply has not walked over yet
    /// are charged identically, from the moment the clock runs until the money
    /// is in hand.</para>
    ///
    /// <para>Which is why the wage accumulating matters twice over: the debt is
    /// the anger source, and it persists.</para>
    ///
    /// <para>The second test excludes an Imp even if something contrived put a
    /// wage on one — the same <c>pay=0</c> that keeps it off the payroll keeps
    /// it from resenting the payroll.</para>
    /// </summary>
    public static bool IsOwedWages(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.WageOwed > 0 && Wage(database, creature, state.Level) > 0;

    /// <summary>
    /// How much gold this room is holding across all its tiles.
    ///
    /// <para><b>The original will not walk to an empty one.</b> Its search tests
    /// a per-room field beside the type test and skips the room when it is zero,
    /// which is why a keeper with three empty treasuries and one full one sends
    /// everybody to the full one rather than to the nearest.</para>
    /// </summary>
    public static int GoldIn(WorldState world, RoomInstance room)
    {
        int total = 0;
        foreach (int index in room.TileIndices) total += world.TileAtIndex(index).GoldHeld;
        return total;
    }

    /// <summary>
    /// Takes up to <paramref name="wanted"/> gold off one tile and says how much
    /// came off it.
    ///
    /// <para><b>One tile, the one underfoot</b>, and that is the original's
    /// rule rather than a simplification of ours: its collecting state passes
    /// the creature's own tile coordinates to the withdrawal, so a creature
    /// standing on an empty tile of a full treasury <b>gets nothing</b> and
    /// spends one of its two attempts. This is also the strongest independent
    /// confirmation this project has of ADR-0015 — payday is the one moment
    /// where a global gold counter would be most convenient, and DK2 still does
    /// it per tile.</para>
    /// </summary>
    public static int TakeFromTile(WorldState world, int tileIndex, int wanted)
    {
        if (wanted <= 0) return 0;

        ref TileState tile = ref world.TileAtIndex(tileIndex);
        int taken = Math.Min(tile.GoldHeld, wanted);
        tile.GoldHeld -= taken;
        return taken;
    }
}
