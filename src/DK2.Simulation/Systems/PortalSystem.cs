// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Runs every keeper's portals: waits, checks the four gates, and puts a
/// creature on the hole in the middle.
///
/// <para><b>This is the first system that makes a creature that is not an
/// imp</b>, and the first consumer of <see cref="SimulationContext.Rng"/> —
/// the shared RNG has been carried, hashed and snapshotted since M1 with nothing
/// drawing from it. Which of several eligible creatures arrives is the first
/// genuinely random decision in the simulation.</para>
///
/// <para>The rules and their evidence are in <see cref="PortalRules"/>. What is
/// here is the two things that only a per-tick loop can decide: when the clock
/// is read, and what a tick sees.</para>
///
/// <para><b>The keeper's creature count is taken once per tick and not updated
/// mid-tick, and that is a reproduced quirk rather than an oversight</b>
/// (fidelity priority 4). Two portals whose clocks come ripe on the same tick
/// both produce, even though the first arrival would have pushed the required
/// interval up and could have taken the keeper to their cap. OpenKeeper calls
/// this out as confirmed original behaviour and replicates it deliberately, for
/// the same reason: the original counts creatures once a tick too. The visible
/// consequence is that a keeper with several portals can end a tick one or two
/// over their stated maximum.</para>
///
/// <para><b>Cost.</b> One <see cref="RoomIndex.Build"/> sweep per tick, because
/// finding a portal means finding rooms and testing attraction means knowing
/// every room a keeper owns. That is a whole-map pass, which
/// <see cref="ManaSystem"/> already does per tick, so it is a cost the tick
/// already carries rather than a new one.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portals.md")]
public sealed class PortalSystem : ISimulationSystem
{
    private const string SystemName = "Portal";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        List<RoomInstance> rooms = RoomIndex.Build(world, database);

        for (int i = 0; i < world.PlayerCount; i++)
        {
            byte playerId = world.PlayerAtIndex(i).PlayerId;

            // Only a keeper's portals run. An unclaimed portal belongs to the
            // neutral player (<see cref="PlayerIds.Neutral"/>) — that is what
            // 167 of the campaign's portals are — and it is not waiting for
            // anything, so its clock does not run and a keeper who claims one
            // waits a full interval for the first arrival (Q86).
            //
            // <b>This clause is only about the clock.</b> Without it nothing
            // would spawn anyway: no shipped level deals a creature pool or
            // creature availability to a player below 3, so the gates in
            // <see cref="Pick"/> would refuse every candidate. What it decides
            // is whether claiming a portal that has stood idle for ten minutes
            // hands the new owner a creature at once. It does not.
            if (!PlayerIds.IsKeeper(playerId)) continue;

            RunPortalsOf(context, rooms, playerId);
        }
    }

    private static void RunPortalsOf(
        SimulationContext context, List<RoomInstance> rooms, byte playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var owned = new List<RoomInstance>();
        var entrances = new List<RoomInstance>();
        int keeperPortals = 0;

        foreach (RoomInstance room in rooms)
        {
            if (room.OwnerPlayerId != playerId) continue;
            owned.Add(room);

            if (!PortalRules.IsEntrance(room.Definition)) continue;
            if (!PortalRules.CanProduce(world, room)) continue;

            entrances.Add(room);
            if (PortalRules.IsKeeperPortal(room.Definition)) keeperPortals++;
        }

        if (entrances.Count == 0) return;

        // Read once, used for every one of this keeper's portals this tick. See
        // the quirk in the type doc — this is the line that reproduces it.
        int held = PortalRules.CreatureCount(world, database, playerId);
        int capacity = PortalRules.CreatureCapacity(database, keeperPortals);

        // RoomIndex.Build returns rooms in ascending order of their lowest tile
        // index, so two ripe portals are always visited in the same order and
        // draw from the RNG in the same order.
        foreach (RoomInstance portal in entrances)
        {
            int entranceTile = PortalRules.EntranceTileIndex(world, portal);
            ref TileState centre = ref world.TileAtIndex(entranceTile);

            // The clock runs whether or not anything can come out, so a portal
            // held up by a missing lair or a full dungeon produces as soon as
            // the obstacle goes.
            centre.EntranceIdleTicks++;

            // Per portal, from this portal's own tally — not the keeper's
            // creature count, and not shared between a keeper's portals. That
            // is the observed behaviour: a portal claimed after another had
            // produced five creatures started again at 25 seconds and not 50
            // (Q86, and TileState.EntranceCreaturesGenerated for the numbers).
            // So the requirement is computed inside the loop now, and the
            // once-per-tick read above is only the capacity check.
            int required = PortalRules.SpawnIntervalTicks(
                database, centre.EntranceCreaturesGenerated);

            if (held >= capacity) continue;
            if (centre.EntranceIdleTicks < required) continue;

            CreatureDef? arriving = Pick(context, owned, playerId);
            if (arriving is null) continue;

            CreatureState spawned = world.SpawnCreature(
                database, arriving.CreatureId, playerId,
                new TilePos(entranceTile % world.Width, entranceTile / world.Width));

            centre.EntranceIdleTicks = 0;

            // Once per creature generated, and it only ever climbs — nothing
            // here decrements it, matching the original, where 129 references
            // to the field include no DEC and no SUB. A portal wears out.
            centre.EntranceCreaturesGenerated++;

            context.Emit(SystemName, "creature_entered",
                $"player={playerId} creature={spawned.Id} type={arriving.Name} " +
                $"at=({spawned.Tile.X},{spawned.Tile.Y}) portal_tile={entranceTile} " +
                $"held={held} capacity={capacity} interval={required} " +
                $"generated={centre.EntranceCreaturesGenerated}");
        }
    }

    /// <summary>
    /// Which creature walks out, or null when nothing qualifies.
    ///
    /// <para><b>Uniformly at random among the eligible types</b>, which is
    /// OpenKeeper's choice and is a placeholder for something the data says
    /// exists. <c>DecreaseEntrancePersentageOfLastSackedCreatureType</c> and
    /// <c>IncreaseEntrancePersentageOfNewCreatureTypes</c> are both 33, so DK2
    /// carries a <i>percentage per creature type</i> that moves as a keeper
    /// sacks and receives — a weighting this does not model, and cannot until
    /// sacking exists (there is no drop-creature command yet). Uniform is the
    /// honest stand-in: it is what the weighting starts as before anything has
    /// shifted it. Recorded as Q90.</para>
    ///
    /// <para>Candidates are collected in creature-id order and the draw is made
    /// even when only one qualifies, so the RNG advances by exactly one step per
    /// arrival regardless of how many types were in the running. A draw that
    /// depended on the candidate count would make the stream a function of the
    /// keeper's rooms, and two runs that differed only in a room built and sold
    /// would then diverge forever.</para>
    /// </summary>
    private static CreatureDef? Pick(
        SimulationContext context, List<RoomInstance> ownedRooms, byte playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<CreatureDef>();
        foreach (CreatureDef creature in database.Creatures.Values.OrderBy(c => c.CreatureId))
        {
            // Cheapest gate and the most fundamental: two creature flags, and
            // the only one of the four that does not consult the level at all.
            if (!PortalRules.CanComeFromPortal(creature)) continue;

            if (!PortalRules.IsUnlocked(database, playerId, creature.CreatureId)) continue;

            int limit = PortalRules.PoolLimit(database, playerId, creature.CreatureId);
            if (limit <= PortalRules.CreatureCountOfType(world, playerId, creature.CreatureId))
                continue;

            if (!PortalRules.IsAttracted(creature, ownedRooms)) continue;

            candidates.Add(creature);
        }

        return candidates.Count == 0
            ? null
            : candidates[context.Rng.NextInt(candidates.Count)];
    }
}
