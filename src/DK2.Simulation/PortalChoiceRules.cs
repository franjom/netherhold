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
/// pick every boost and sack block drops by 1.0 for each definition that maps to its type.
/// The two variables named for this in the balance table are read by nothing (Q90); the
/// rule is hardcoded.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portal-creature-action.md")]
public static class PortalChoiceRules
{
    private const int One = 0x1000;

    /// <summary>The value a fresh boost or block starts at, and the one that is special.</summary>
    public const int Fresh = 10 * One;

    /// <summary>The lair object byte (<c>+0x771</c>) that makes a Lair-attracted type weigh 3.0.</summary>
    private const byte GoblinLairObjectId = 0x16;

    /// <summary><c>0x004DBD90</c>: gives each admitted type without a row one, boosted unless
    /// it is Lair-attracted or the game has not passed tick 1 (the level-start pass). Only a
    /// type whose weight is positive is noticed (0x004DBF3D sets the flag after that test), so
    /// a type with no attracting room, or all of its room's contribution already held, waits
    /// for the pick in which it carries weight and is boosted then.</summary>
    public static void Notice(
        WorldState world, PlayerId playerId, IReadOnlyList<CreatureDef> admitted, IReadOnlyList<RoomInstance> keeperRooms)
    {
        ref PlayerState player = ref world.Player(playerId);
        foreach (CreatureDef creature in admitted)
        {
            int at = IndexOf(player.EntranceInfo, creature.CreatureId);
            if (at >= 0)
            {
                continue;
            }

            if (BaseWeight(world, creature, keeperRooms, playerId) <= 0)
            {
                continue;
            }

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
        if (IsLairAttracted(creature))
        {
            return creature.LairObjectId == GoblinLairObjectId ? 3 * One : One;
        }

        int roomId = creature.Attractions.Count == 0 ? 0 : creature.Attractions[0].RoomId;
        int weight = 0;
        foreach (RoomInstance room in keeperRooms)
        {
            if (room.Definition.RoomId == roomId)
            {
                weight += AttractionBudget.Contribution(world, room) * One;
            }
        }

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
    /// none carries weight, then decays the boosts and blocks (<see cref="Decay"/>). One draw per
    /// call that found a candidate.</summary>
    public static CreatureDef? Pick(
        WorldState world, GameDatabase database, SimRandom rng, PlayerId playerId,
        IReadOnlyList<CreatureDef> admitted, IReadOnlyList<RoomInstance> keeperRooms)
    {
        Notice(world, playerId, admitted, keeperRooms);
        ref PlayerState player = ref world.Player(playerId);

        var entries = new List<(CreatureDef Creature, long Weight)>();
        foreach (CreatureDef creature in admitted)
        {
            int weight = BaseWeight(world, creature, keeperRooms, playerId);
            if (weight <= 0)
            {
                continue;
            }

            EntranceInfoState row = player.EntranceInfo[IndexOf(player.EntranceInfo, creature.CreatureId)];
            entries.Add((creature, Weighted(weight, row)));
        }
        if (entries.Count == 0)
        {
            return null;
        }

        // Each weight as a share of 100.0, then a running total, and the first entry whose
        // running total reaches the draw.
        long sum = 0;
        foreach ((_, long w) in entries)
        {
            sum += w;
        }

        CreatureDef? chosen = null;
        if (sum > 0)
        {
            long[] running = new long[entries.Count];
            long total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Weight > 0)
                {
                    total += (entries[i].Weight * 100 * One) / sum;
                }

                running[i] = total;
            }
            if (total > 0)
            {
                long draw = rng.NextInt((int)total);
                for (int i = 0; i < entries.Count && chosen is null; i++)
                {
                    if (draw <= running[i])
                    {
                        chosen = entries[i].Creature;
                    }
                }
            }
        }

        Decay(ref player, database);
        return chosen;
    }

    /// <summary>The definition indices 0x004DBF90 walks, and the rows of the entrance table: one per
    /// creature type, 1 to 46.</summary>
    public const int RowCount = 46;

    /// <summary>The decay after a pick that had a candidate (0x004DC5F2 to 0x004DC72F): for each
    /// definition index 0 to 45 it takes 1.0 off the boost (all 46), then 1.0 off the block, of
    /// the first row whose type is the index's, each only while non-zero. A unique definition
    /// counts for its base type (0x00494360), so a type with a unique variant loses 2.0 a pick
    /// and one without 1.0. An index past the definition count has type 0, whose first row is
    /// the empty one past the definitions (0x004B8640 types rows by index), so it docks nothing.
    /// The definition table is in id order in the shipped data (index = id - 1).</summary>
    private static void Decay(ref PlayerState player, GameDatabase database)
    {
        int index = 0;
        for (int id = 0; id <= byte.MaxValue && index < RowCount; id++)
        {
            if (!database.Creatures.TryGetValue((byte)id, out CreatureDef? definition))
            {
                continue;
            }

            byte type = (definition.Flags & CreatureFlags.IsUnique) != 0 ? definition.UniqueBaseCreatureId : definition.CreatureId;
            DecayRowOf(ref player, type);
            index++;
        }
    }

    private static void DecayRowOf(ref PlayerState player, byte type)
    {
        // A type without a row here was never noticed, and its native row has nothing to take off.
        int at = IndexOf(player.EntranceInfo, type);
        if (at < 0)
        {
            return;
        }

        EntranceInfoState row = player.EntranceInfo[at];
        player.EntranceInfo[at] = row with
        {
            Boost = row.Boost != 0 ? row.Boost - One : 0,
            Block = row.Block != 0 ? row.Block - One : 0,
        };
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
            if (c == 0)
            {
                return mid;
            }

            if (c < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return ~lo;
    }
}
