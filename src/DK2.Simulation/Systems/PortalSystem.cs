// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Runs each keeper's portals: advances the idle clock, checks the four
/// gates in <see cref="Pick"/>, and spawns a creature on the portal's centre
/// tile. Rules and evidence: <see cref="PortalRules"/>.</summary>
/// <remarks>
/// <para>Reproduced quirk, not an oversight (fidelity priority 4): the keeper's
/// creature count is read once per tick, so two portals ripe on the same tick
/// both produce and a keeper can end the tick one or two over their stated
/// maximum. STRONGLY INFERRED — OpenKeeper replicates the same once-a-tick
/// count as original behaviour.</para>
/// <para>Cost: one <see cref="RoomIndex.Build"/> sweep per tick, which
/// <see cref="ManaSystem"/> already pays.</para>
/// </remarks>
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
            // neutral player (PlayerIds.Neutral) — 167 of the campaign's are —
            // and its clock does not run, so a keeper who claims one waits a
            // full interval for the first arrival (Q86). This clause decides
            // only the clock: no shipped level grants a creature pool or
            // availability below player 3, so Pick would refuse every candidate.
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
        int portalsHeld = 0;

        foreach (RoomInstance room in rooms)
        {
            if (room.OwnerPlayerId != playerId) continue;
            owned.Add(room);

            if (!PortalRules.IsEntrance(room.Definition)) continue;

            // Counted before the functional test, deliberately: DK2's capacity
            // routine walks the owner's room list and tests the room type only —
            // not size, not whether the room still has its centre. A broken
            // portal, or a Hero Portal a keeper has claimed and cannot use,
            // still raises the cap, and both portal types count. See
            // PortalRules.CreatureCapacity for what the routine reads.
            portalsHeld++;

            if (!PortalRules.CanProduce(world, room)) continue;

            entrances.Add(room);
        }

        if (entrances.Count == 0) return;

        // Read once, used for every one of this keeper's portals this tick. See
        // the quirk in the type doc — this is the line that reproduces it.
        int held = PortalRules.CreatureCount(world, database, playerId);
        int capacity = PortalRules.CreatureCapacity(database, portalsHeld);

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
            // creature count, and not shared between a keeper's portals: a
            // portal claimed after another had produced five creatures starts
            // again at 25 seconds, not 50 (Q86; the numbers are on
            // TileState.EntranceCreaturesGenerated).
            int required = PortalRules.SpawnIntervalTicks(
                database, centre.EntranceCreaturesGenerated);

            if (held >= capacity) continue;
            if (centre.EntranceIdleTicks < required) continue;

            // The portal is passed in because the flag gate depends on the room
            // type: a Hero Portal admits a different nine creatures, and a
            // keeper's claimed one admits none of them.
            CreatureDef? arriving = Pick(context, owned, playerId, portal.Definition);
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

    /// <summary>Which creature walks out, or null when nothing qualifies.</summary>
    /// <remarks>
    /// <para>Uniformly at random among the eligible types — a placeholder for a
    /// weighting the data says exists (Q90). Both
    /// <c>DecreaseEntrancePersentageOfLastSackedCreatureType</c> and
    /// <c>IncreaseEntrancePersentageOfNewCreatureTypes</c> are 33, so DK2 carries
    /// a percentage per creature type that moves as a keeper sacks and receives.
    /// Uniform is what it starts as; modelling it needs sacking, not yet built.</para>
    /// <para>Candidates are collected in creature-id order and the draw is made
    /// even when only one qualifies, so the RNG advances exactly one step per
    /// arrival and the stream does not depend on the keeper's rooms.</para>
    /// </remarks>
    private static CreatureDef? Pick(
        SimulationContext context, List<RoomInstance> ownedRooms, byte playerId,
        RoomDef portal)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<CreatureDef>();
        foreach (CreatureDef creature in database.Creatures.Values.OrderBy(c => c.CreatureId))
        {
            // Cheapest gate and the most fundamental: two creature flags, and
            // the only one of the four that does not consult the level at all.
            if (!PortalRules.CanComeFromPortal(creature, portal)) continue;

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
