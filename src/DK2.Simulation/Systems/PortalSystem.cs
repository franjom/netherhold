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
/// <para>DK2's pick (<c>0x004DBF90</c>) recounts the owner's portal-born creatures and recomputes the
/// capacity (<c>0x004DCE30</c>) every time it runs, and the arrival creator (<c>0x004DC9A0</c>) links the new
/// creature into its owner's list and marks it portal-born at once, so a second portal ripe in the same turn
/// sees the first one's arrival: at one under the capacity it refuses and draws nothing. Ours counts live for
/// each portal in the room walk (<see cref="RoomTickSystem"/>).</para>
/// <para>Cost: one <see cref="RoomIndex.Build"/> sweep per tick, which
/// <see cref="ManaSystem"/> already pays.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portals.md")]
public sealed class PortalSystem : IRoomTickStep
{
    private const string SystemName = "Portal";

    public string Name => SystemName;

    public void Execute(SimulationContext context) => RoomTickSystem.RunAlone(context, this);

    /// <summary>Only a keeper's portals run. An unclaimed portal belongs to the neutral player
    /// (<see cref="PlayerIds.Neutral"/>) — 167 of the campaign's are — and its clock does not run, so a keeper
    /// who claims one waits a full interval for the first arrival (Q86). This decides only the clock: no
    /// shipped level grants a creature pool or availability below player 3, so Pick would refuse every
    /// candidate. The owner is read from the tile now, not from the room found at the walk's start.</summary>
    public bool Applies(SimulationContext context, RoomInstance room) =>
        PortalRules.IsEntrance(room.Definition)
        && PlayerIds.IsKeeper(Owner(context.World, room)) && PortalRules.CanProduce(context.World, room);

    public void Tick(SimulationContext context, RoomInstance room) => TickPortal(context, room);

    private static byte Owner(WorldState world, RoomInstance room) => world.TileAtIndex(room.TileIndices[0]).OwnerPlayerId;

    /// <summary>The owner's rooms and how many portals it holds, counted now: DK2's capacity routine walks the
    /// owner's room list and tests the room type only — not size, not whether the room still has its centre. A
    /// broken portal, or a Hero Portal a keeper has claimed and cannot use, still raises the cap, and both
    /// portal types count (see <see cref="PortalRules.CreatureCapacity"/>).</summary>
    private static List<RoomInstance> OwnedRooms(SimulationContext context, PlayerId playerId, out int portalsHeld)
    {
        var owned = new List<RoomInstance>();
        portalsHeld = 0;
        foreach (RoomInstance room in RoomIndex.Build(context.World, context.Database))
        {
            if (Owner(context.World, room) != playerId) continue;
            owned.Add(room);
            if (PortalRules.IsEntrance(room.Definition)) portalsHeld++;
        }

        return owned;
    }

    /// <summary>One ripe portal's turn. DK2's room tick visits the portals newest first, and each pick counts
    /// the owner's creatures and capacity as they stand, after the earlier portals' arrivals.</summary>
    private static void TickPortal(SimulationContext context, RoomInstance portal)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        PlayerId playerId = new(Owner(world, portal));

        int entranceTile = PortalRules.EntranceTileIndex(world, portal);
        ref TileState centre = ref world.TileAtIndex(entranceTile);

        centre.EntranceIdleTicks++;

        // Per portal, from this portal's own tally — not the keeper's
        // creature count, and not shared between a keeper's portals: a
        // portal claimed after another had produced five creatures starts
        // again at 25 seconds, not 50 (Q86; the numbers are on
        // TileState.EntranceCreaturesGenerated).
        int required = PortalRules.SpawnIntervalTicks(
            database, centre.EntranceCreaturesGenerated);

        if (centre.EntranceIdleTicks < required)
        {
            if (centre.EntranceIdleTicks == required - 3 * SimulationTiming.OriginalTicksPerGameSecond
                && portal.Definition.RoomEffect425Id != 0)
            {
                List<RoomInstance> previewRooms = OwnedRooms(context, playerId, out int previewPortals);
                if (PortalRules.CreatureCount(world, playerId) >= PortalRules.CreatureCapacity(database, previewPortals)) return;
                List<CreatureDef> preview = Candidates(context, previewRooms, playerId,
                    portal.Definition, out _);
                // The native preview counts base-weight candidates without noticing,
                // drawing, or decaying entrance modifiers, even when portals are closed.
                if (preview.Any(c => PortalChoiceRules.BaseWeight(world, c, previewRooms, playerId) > 0))
                    context.RequestEffect(SystemName, ViewEffectRequest.AtTileCentre(
                        new EffectId(portal.Definition.RoomEffect425Id), playerId,
                        world.TilePosition(entranceTile), new Fp32(0)));
            }
            return;
        }

        // A due roll restarts the clock whatever it produces: 0x004DBBE0 stamps
        // room+0x49 straight after the pick, untested (0x004DBC7C). A portal held
        // up by a missing lair or a full dungeon waits for its next roll, not the
        // moment the obstacle goes (ADR-0314). No free lair space holds the pick
        // itself (ADR-0614).
        centre.EntranceIdleTicks = 0;
        List<RoomInstance> owned = OwnedRooms(context, playerId, out int portalsHeld);
        int held = PortalRules.CreatureCount(world, playerId);
        int capacity = PortalRules.CreatureCapacity(database, portalsHeld);
        if (held >= capacity) return;

        // The portal is passed in because the flag gate depends on the room
        // type: a Hero Portal admits a different nine creatures, and a
        // keeper's claimed one admits none of them.
        CreatureDef? arriving = Pick(context, owned, playerId, portal.Definition, out string candidates);
        bool open = PortalRules.PortalsOpen(world, playerId);
        context.Emit(SystemName, "roll",
            $"player={playerId} portal_tile={entranceTile} held={held} capacity={capacity} " +
            $"generated={centre.EntranceCreaturesGenerated} open={open} candidates=[{candidates}] " +
            $"chosen={arriving?.Name ?? "-"}");
        if (arriving is null) return;

        // Closed portals still draw and decay the boosts; the creator 0x004DC9A0
        // then refuses (PortalsOpen clear), so neither tally nor creature moves.
        if (!open) return;

        CreatureState? spawned = PortalRules.CreateArrival(context.Hooks, portal, arriving.CreatureId);
        if (spawned is null) return;

        context.Emit(SystemName, "creature_entered",
            $"player={playerId} creature={spawned.Id} type={arriving.Name} " +
            $"at=({spawned.Tile.X},{spawned.Tile.Y}) portal_tile={entranceTile} " +
            $"held={held} capacity={capacity} interval={required} " +
            $"generated={centre.EntranceCreaturesGenerated}");
    }

    /// <summary>Which creature walks out, or null when nothing qualifies.</summary>
    /// <remarks>
    /// <para>The gates here decide who may come; <see cref="PortalChoiceRules"/> weighs them
    /// as <c>0x004DBF90</c> does, with a new type boosted (ADR-0272). The sack block it
    /// reads has no writer until sacking is built.</para>
    /// <para>Candidates are collected in creature-id order and one draw is made whenever
    /// any carries weight, even a single one.</para>
    /// </remarks>
    private static CreatureDef? Pick(
        SimulationContext context, List<RoomInstance> ownedRooms, PlayerId playerId,
        RoomDef portal, out string candidateNames)
    {
        List<CreatureDef> candidates = Candidates(context, ownedRooms, playerId, portal, out candidateNames);
        return PortalChoiceRules.Pick(context.World, context.Rng, playerId, candidates, ownedRooms);
    }

    private static List<CreatureDef> Candidates(
        SimulationContext context, List<RoomInstance> ownedRooms, PlayerId playerId,
        RoomDef portal, out string candidateNames)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<CreatureDef>();
        // 0x004DBF90 and its noticing pass 0x004DBD90 each start with the keeper's free lair space
        // (0x004C6DD0) and weigh no type while it is negative: no notice, no draw, no decay.
        int lairSpace = PortalRules.FreeLairSpace(world, database, playerId, ownedRooms);
        if (lairSpace < 0)
        {
            candidateNames = $"rejected=lair_space:{lairSpace}";
            return candidates;
        }

        var rejected = new List<string>();
        var money = new GoldAmount(GoldStore.Total(world, playerId));
        var mana = new ManaAmount(world.Player(playerId).Mana);
        foreach (CreatureDef creature in database.Creatures.Values.OrderBy(c => c.CreatureId))
        {
            // Cheapest gate and the most fundamental: two creature flags, and
            // the only one of the four that does not consult the level at all.
            if (!PortalRules.CanComeFromPortal(creature, portal)) continue;

            // No availability test: 0x004DBF90 reads the pool (0x00506400), the two flags, the
            // attraction and the capacity, never the creature table. Level 3 makes the Firefly,
            // Goblin and Warlock UNAVAILABLE at its start and its portal still produces them
            // (ADR-0316). Only creatures the level pools are worth naming when refused.
            int limit = PortalRules.PoolLimit(database, playerId, creature.CreatureId);
            if (limit <= PortalRules.CreatureCountOfType(world, playerId, creature.CreatureId))
            {
                rejected.Add($"{creature.Name}:pool");
                continue;
            }

            if (!PortalRules.IsAttracted(creature, ownedRooms, money, mana))
            {
                rejected.Add($"{creature.Name}:room");
                continue;
            }

            // The fifth gate, and the second half of attraction: the room's
            // contribution less one per creature of the type already held.
            if (!AttractionBudget.Admits(world, creature, ownedRooms, playerId))
            {
                rejected.Add($"{creature.Name}:budget");
                continue;
            }

            candidates.Add(creature);
        }

        candidateNames = string.Join(",", candidates.Select(c => c.Name));
        if (rejected.Count > 0) candidateNames += " rejected=" + string.Join(",", rejected);

        return candidates;
    }
}
