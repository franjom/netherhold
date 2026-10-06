// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Which of the admitted creature types a keeper portal produces: the weighted
/// draw of <c>0x004DBF90</c> and the noticing pass <c>0x004DBD90</c> it runs first.</summary>
/// <remarks>STRONGLY INFERRED from the binary (portal-creature-action.md, ADR-0272). A type's
/// weight is its first attraction room's contribution less one per creature of the type
/// held, or a flat 1.0 (3.0 for the Goblin's lair) when that room is the Lair. A type first
/// noticed after tick 1 starts with a boost of 10.0, which multiplies by 200; after every
/// pick every boost and sack block drops by 1.0. The two variables named for this in the
/// balance table are read by nothing (Q90); the rule is hardcoded.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portal-creature-action.md")]
public static class PortalChoiceRules
{
    private const int One = 0x1000;

    /// <summary>The value a fresh boost or block starts at, and the one that is special.</summary>
    public const int Fresh = 10 * One;

    /// <summary>The lair object byte (<c>+0x771</c>) that makes a Lair-attracted type weigh 3.0.</summary>
    private const byte GoblinLairObjectId = 0x16;

    /// <summary><c>0x004DBD90</c>: gives each admitted type without a row one, boosted unless
    /// it is Lair-attracted or the game has not passed tick 1 (the level-start pass).</summary>
    public static void Notice(WorldState world, PlayerId playerId, IReadOnlyList<CreatureDef> admitted)
    {
        ref PlayerState player = ref world.Player(playerId);
        foreach (CreatureDef creature in admitted)
        {
            int at = IndexOf(player.EntranceInfo, creature.CreatureId);
            if (at >= 0) continue;

            int boost = world.CurrentTick.Value > 1 && !IsLairAttracted(creature) ? Fresh : 0;
            int insert = ~at;
            player.EntranceInfo.Insert(insert, new EntranceInfoState(creature.CreatureId, boost, 0));
        }
    }

    /// <summary>A type's weight in 1/4096 before the modifiers, and whether it counts as a
    /// candidate at all (a zero weight here drops it).</summary>
    public static int BaseWeight(
        WorldState world, CreatureDef creature, IReadOnlyList<RoomInstance> keeperRooms, PlayerId playerId)
    {
        if (IsLairAttracted(creature)) return creature.LairObjectId == GoblinLairObjectId ? 3 * One : One;

        int roomId = creature.Attractions.Count == 0 ? 0 : creature.Attractions[0].RoomId;
        int weight = 0;
        foreach (RoomInstance room in keeperRooms)
            if (room.Definition.RoomId == roomId) weight += AttractionBudget.Contribution(world, room) * One;

        int held = PortalRules.CreatureCountOfType(world, playerId, creature.CreatureId);
        return Math.Max(0, weight - (held * One));
    }

    /// <summary>The weight after the row's boost and block.</summary>
    public static int Weighted(int baseWeight, EntranceInfoState row)
    {
        long w = baseWeight;
        w = row.Boost == Fresh ? w * 200 : (w * Math.Max(row.Boost, One)) >> 12;
        w = row.Block == Fresh ? 0 : (w << 12) / Math.Max(row.Block, One);
        return (int)w;
    }

    /// <summary>Draws one of <paramref name="admitted"/> as <c>0x004DBF90</c> does, or null when
    /// none carries weight, then takes 1.0 off every boost and block. One draw per call that
    /// found a candidate.</summary>
    public static CreatureDef? Pick(
        WorldState world, SimRandom rng, PlayerId playerId, IReadOnlyList<CreatureDef> admitted,
        IReadOnlyList<RoomInstance> keeperRooms)
    {
        Notice(world, playerId, admitted);
        ref PlayerState player = ref world.Player(playerId);

        var entries = new List<(CreatureDef Creature, long Weight)>();
        foreach (CreatureDef creature in admitted)
        {
            int weight = BaseWeight(world, creature, keeperRooms, playerId);
            if (weight <= 0) continue;
            EntranceInfoState row = player.EntranceInfo[IndexOf(player.EntranceInfo, creature.CreatureId)];
            entries.Add((creature, Weighted(weight, row)));
        }
        if (entries.Count == 0) return null;

        // Each weight as a share of 100.0, then a running total, and the first entry whose
        // running total reaches the draw.
        long sum = 0;
        foreach ((_, long w) in entries) sum += w;
        CreatureDef? chosen = null;
        if (sum > 0)
        {
            var running = new long[entries.Count];
            long total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Weight > 0) total += (entries[i].Weight * 100 * One) / sum;
                running[i] = total;
            }
            if (total > 0)
            {
                long draw = rng.NextInt((int)total);
                for (int i = 0; i < entries.Count && chosen is null; i++)
                    if (draw <= running[i]) chosen = entries[i].Creature;
            }
        }

        for (int i = 0; i < player.EntranceInfo.Count; i++)
        {
            EntranceInfoState row = player.EntranceInfo[i];
            player.EntranceInfo[i] = row with
            {
                Boost = row.Boost != 0 ? row.Boost - One : 0,
                Block = row.Block != 0 ? row.Block - One : 0,
            };
        }
        return chosen;
    }

    private static bool IsLairAttracted(CreatureDef creature) =>
        creature.Attractions.Count > 0 && creature.Attractions[0].RoomId == (byte)RoomTypeId.Lair;

    /// <summary>Binary search by type id: the row's index, or the complement of where it goes.</summary>
    private static int IndexOf(List<EntranceInfoState> rows, byte creatureTypeId)
    {
        int lo = 0, hi = rows.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = rows[mid].CreatureTypeId.CompareTo(creatureTypeId);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        return ~lo;
    }
}
