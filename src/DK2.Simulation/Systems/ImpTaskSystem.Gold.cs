// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>Gold: picked up in passing, fetched, carried to a store and put down.</summary>
public sealed partial class ImpTaskSystem
{
    /// <summary>The store chooser <c>0x00464400</c> from state 10: a walk to the nearest store with
    /// room, if there is one.</summary>
    private bool TryDepositTheLoad(SimulationContext context, CreatureState creature)
    {
        if (!TryRouteToSomewhereItFits(context, creature)) return false;
        BeginDelivering(creature);
        ToTheStore(context, creature);
        return true;
    }

    /// <summary>DK2's hands test <c>0x00464360(1)</c>: full at <c>MaxGoldHeld</c> scaled by the
    /// imp's level (level 1 unscaled), less variable 74, 340 in every shipped level
    /// (imp-hands-full.md).</summary>
    private static bool HandsFull(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        return creature.GoldHeld >= FullAt(context, creature, definition);
    }

    private static int FullAt(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        return CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - HandsMargin(context.Database);
    }

    /// <summary>Variable 74 (<c>0x006F28B6</c>), the hands test's margin. A database without it is
    /// read as 0, as DK2's global is before a level loads.</summary>
    private static int HandsMargin(GameDatabase database)
    {
        return database.Variables.TryGet(VariableTypeId.GameTicks, out int margin) ? margin : 0;
    }

    /// <summary>A pile's centre within this of a worker, on both axes and strictly, is in its
    /// reach: a third of a tile (<c>0x555</c>), for the pick-up in passing and for state 27's
    /// take (<c>0x00466850</c>).</summary>
    private const int PileReach = 0x555;

    private static bool InReach(CreatureState creature, (long X, long Y) pile) =>
        Math.Abs(pile.X - creature.PosX) < PileReach && Math.Abs(pile.Y - creature.PosY) < PileReach;

    /// <summary>
    /// The worker arm of DK2's walk-over pick-up (<c>0x0048904E</c>), which the creature update
    /// runs every tick before the state handler, so state 10 then sees the gold in the hands. A
    /// keeper's worker below the hands test's mark takes the first neutral pile on its own tile
    /// whose centre is within <see cref="PileReach"/>, as much as it lacks against the level's
    /// full maximum (imp-hands-full.md, gold-on-the-floor.md §12, ADR-0293, ADR-0378). DK2 walks
    /// the tile's thing list; ours its objects in world order.
    /// </summary>
    private static void PickUpGoldInPassing(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        // A possessed worker is the creature arm's (WalkOverGoldSystem.IsWorkerArm).
        if (!WalkOverGoldSystem.IsWorkerArm(context.World, definition, creature)
            || creature.GoldHeld >= FullAt(context, creature, definition))
            return;
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        foreach (ObjectState pile in world.Objects)
        {
            if (pile.TileIndex != here || !FloorGold.IsPile(context.Database, pile) || pile.InHand
                || pile.CarriedByCreatureId >= 0 || pile.OwnerPlayerId != PlayerIds.Neutral)
                continue;
            if (!InReach(creature, PileCentre(world, pile))) continue;

            int lacks = CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - creature.GoldHeld;
            int taken = FloorGold.Take(world, context.Database, pile, lacks);
            creature.GoldHeld += taken;
            context.Emit(SystemName, "gold_picked_up_in_passing",
                $"imp={creature.Id} at={Describe(world, pile.TileIndex)} gold={taken} holding={creature.GoldHeld}");
            return;
        }
    }

    /// <summary>A pile's point: the centre of its third-of-a-tile cell, or of the tile.</summary>
    private static (long X, long Y) PileCentre(WorldState world, ObjectState pile)
    {
        long x = (pile.TileIndex % world.Width) * 4096L, y = (pile.TileIndex / world.Width) * 4096L;
        if (pile.Cell == MiningSlots.AnyCell) return (x + 2048, y + 2048);
        return (x + 682 + (pile.Cell % 3) * 1365, y + 682 + (pile.Cell / 3) * 1365);
    }

    /// <summary>
    /// Commits a worker to picking up the gold on <paramref name="standing"/>,
    /// along the route the last search planned.
    /// <para><b>One imp to a pile tile</b>, the same cap a floor job gets
    /// (<see cref="ImpCrowding"/>: one for a tile a worker stands on). Gold is
    /// on the floor and an imp stands on it to pick it up, so the stance decides
    /// the number. Whether the original crowds a pile differently is
    /// UNKNOWN.</para>
    /// </summary>
    private bool CommitFetch(SimulationContext context, CreatureState creature, TilePos standing)
    {
        WorldState world = context.World;
        int tileIndex = (standing.Y * world.Width) + standing.X;

        // 0x0046B6D0 takes the pile only if its walk starts, else tries the next thing (Q590).
        (long px, long py) = PointOfFirstPile(context, tileIndex);
        if (!context.Navigation.TryRouteToStance(creature, px, py, Navigation.MoveOptions.ExactSpot))
        {
            context.Emit(SystemName, "fetch_unreachable", $"imp={creature.Id} {Describe(world, tileIndex)}");
            return false;
        }
        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.LastTaskKind = ImpTaskKind.FetchGold;
        FromTaskSelection(context, creature);
        BeginTaskWalk(context, creature, ImpStates.PickingUpGold);

        context.Emit(SystemName, "fetch_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(world, tileIndex)} " +
            $"gold={FloorGold.OnTile(world, context.Database, tileIndex)} " +
            $"priority={FetchPriority} steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// Where the fetch walks: the point of the tile's first neutral pile, as the gold handler
    /// (<c>0x0046B6D0</c>) walks to the position of the pile whose task it took, so state 27's
    /// box (<see cref="PileReach"/>) holds it. Through <see cref="Navigation.WorldSteering.TryRouteToStance"/>,
    /// which steps to the point from the same tile, since the route request has no same-tile
    /// shortcut (imp-walk-arrival.md). The tile centre if no pile is neutral (ADR-0389).
    /// </summary>
    private static (long X, long Y) PointOfFirstPile(SimulationContext context, int tileIndex)
    {
        WorldState world = context.World;
        foreach (ObjectState pile in world.Objects)
        {
            if (pile.TileIndex == tileIndex && FloorGold.IsPile(context.Database, pile)
                && pile.OwnerPlayerId == PlayerIds.Neutral)
                return PileCentre(world, pile);
        }

        return (CreatureState.CentreOf(tileIndex % world.Width), CreatureState.CentreOf(tileIndex / world.Width));
    }

    /// <summary>
    /// Whether a worker standing here would be standing on gold it may pick up.
    /// A pile is worked from the tile itself, so this tests the tile underfoot
    /// and no neighbours — unlike a wall, worked from beside it.
    /// <para><b>The ground has to be the keeper's own, or water.</b> Read from
    /// the original's gold-fetching task, which refuses a tile that is neither
    /// water nor owned by the worker's player before it looks at anything on it.
    /// That is also why imps that mined a gem tile with unclaimed surroundings
    /// never carried the gold home (Q144) — not because they could not walk
    /// there. Why water is exempt is UNKNOWN; without the exception, gold that
    /// fell in would be unrecoverable for good.</para>
    /// </summary>
    private static bool CanFetchFrom(
        SimulationContext context, TilePos at, PlayerId playerId, int askingCreatureId)
    {
        WorldState world = context.World;
        if (!world.Contains(at)) return false;

        int index = (at.Y * world.Width) + at.X;
        ref TileState tile = ref world.TileAtIndex(index);

        if (tile.OwnerPlayerId != playerId
            && !context.Database.TerrainById(tile.TerrainId).IsWater)
        {
            return false;
        }

        if (FloorGold.OnTile(world, context.Database, index) <= 0) return false;

        return !ImpCrowding.FaceIsFull(
            world, context.Database, index, at, askingCreatureId, workedFromTheTile: true);
    }

    /// <summary>
    /// One tick of going to get gold: walk to the pile's point, fill your hands, become a carrier.
    /// <para><b>The pick-up is instant when the walk ends</b> rather than costing a work
    /// period. Picking gold up is not work — nothing is damaged, no health delta
    /// applies, and the balance table has no number for it — so charging it a
    /// swing would be inventing a duration. Whether the original animates a
    /// pause here is UNKNOWN (Q143), and wants a stopwatch.</para>
    /// <para>The rule that an imp will not take gold off ground that is not its
    /// keeper's own is in <see cref="CanFetchFrom"/>, not here (Q144).</para>
    /// </summary>
    private void UpdateFetching(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        WorldState world = context.World;

        // No pile test: the walk is state 6, which does not look at the pile, and 27 runs when
        // it ends even with the pile gone. The pick-up in passing takes it at the pile's point,
        // and 27 still sends full hands to the store (Level 3 seed01, imp 443 at 2343). Whether
        // 27's task check (0x00508070) fails once another imp has emptied the tile is not read.
        if (creature.TargetTileIndex < 0)
        {
            context.Emit(SystemName, "fetch_abandoned",
                $"imp={creature.Id} {Describe(world, Math.Max(0, creature.TargetTileIndex))} " +
                "reason=gone");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Still on the road: state 27 runs when the walk to the pile's point ends, not when the
        // tile is entered (Level 3 seed01: each fetch's gold came 1-7 ticks after entering, 4-15
        // units from the pile).
        if (creature.HasRoute) return;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            context.Emit(SystemName, "fetch_abandoned",
                $"imp={creature.Id} {Describe(world, creature.TargetTileIndex)} " +
                "reason=out_of_reach");
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        int taken = TakeFromPilesOn(context, creature, definition, creature.TargetTileIndex);

        context.Emit(SystemName, "gold_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} amount={taken} " +
            $"{Describe(world, creature.TargetTileIndex)} " +
            $"left={FloorGold.OnTile(world, context.Database, creature.TargetTileIndex)} " +
            $"carrying={creature.GoldHeld}");

        if (creature.GoldHeld <= 0)
        {
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // State 27 (0x00466850): full hands go to the store chooser (resume 28); a part-load, or
        // a full one with no store that has room, sets 10 at once, and the next choice may be
        // more gold, a claim or a wall with the gold still in its hands (ADR-0288).
        if (HandsFull(context, creature, definition) && TryDepositTheLoad(context, creature)) return;

        WorkEnded(context, creature);
        GoIdle(creature);
    }

    /// <summary>
    /// State 27's take (<c>0x00466850</c>): the neutral piles on one tile strictly within
    /// <see cref="PileReach"/> of the imp, each up to what the hands lack against the level's
    /// full maximum, stopping after the first pile that leaves them full by the hands test
    /// (<see cref="HandsFull"/>, the limit less variable 74), so the rest stay. Adds to the purse
    /// and returns how much came off (ADR-0382, ADR-0389).
    ///
    /// <para>Oldest first, spawn order, where DK2 walks the tile's thing list.</para>
    /// </summary>
    private static int TakeFromPilesOn(SimulationContext context, CreatureState creature, CreatureDef definition, int tileIndex)
    {
        var piles = new List<ObjectState>();
        FloorGold.PilesOn(context.World, context.Database, tileIndex, piles);

        int taken = 0;
        foreach (ObjectState pile in piles)
        {
            if (pile.OwnerPlayerId != PlayerIds.Neutral || !InReach(creature, PileCentre(context.World, pile))) continue;
            int lacks = CreatureLevelRules.MaxGoldHeld(context.Database, definition, creature.Level) - creature.GoldHeld;
            if (lacks <= 0) break;
            int took = FloorGold.Take(context.World, context.Database, pile, lacks);
            creature.GoldHeld += took;
            taken += took;
            if (HandsFull(context, creature, definition)) break;
        }

        return taken;
    }

    /// <summary>
    /// Commits a worker to the road, once
    /// <see cref="TryRouteToSomewhereItFits"/> has planned a route to somewhere
    /// its load fits. Sets the fields by hand rather than calling
    /// <see cref="CreatureState.ClearJob"/>, which would throw away the route
    /// that was just planned.
    /// </summary>
    private static void BeginDelivering(CreatureState creature)
    {
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
    }

    /// <summary>
    /// The store walk resumes in 28; its next update turns toward the tile centre,
    /// then 29 deposits on a later update (ADR-0502).
    /// </summary>
    private void UpdateDelivering(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (creature.Dk2StateId == StateShadow.TravelToPoint)
        {
            if (Steering.FollowPathRules.Progress(creature.Body) != Steering.Fixed16.One) return;
            // State 6 stops at its committed position before resuming the store-facing state.
            creature.StopMoving();
            if (!StateShadow.Arrive(context, SystemName, creature))
            {
                StateShadow.ToIdle(context, SystemName, creature);
                GoIdle(creature);
            }
            return;
        }

        if (creature.Dk2StateId == ImpStates.AtTheStore)
        {
            int heading = creature.Heading;
            int remaining = Steering.Bearing.TurnToward(ref heading,
                Steering.Bearing.Of(CreatureState.CentreOf(creature.Tile.X) - creature.PosX,
                    CreatureState.CentreOf(creature.Tile.Y) - creature.PosY),
                SimulationTiming.TurnRatePerTick(context.Database.CreatureById(creature.CreatureTypeId)));
            creature.Heading = heading;
            if (remaining == 0
                && !StateShadow.TryMove(context, SystemName, creature, ImpStates.PuttingDownGold))
            {
                StateShadow.ToIdle(context, SystemName, creature);
                GoIdle(creature);
            }
            return;
        }

        if (creature.Dk2StateId == ImpStates.PuttingDownGold && TryPutDown(context, creature))
            StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        else
            StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
    }

    /// <summary>
    /// State 29 (<c>0x00466AC0</c>) on the store tile the worker stands on: false when the tile is
    /// not a Treasury or live Heart of its keeper. Otherwise it hands over the smaller of its gold and
    /// the room's space, capacity less what the room holds, the capacity being tiles × the Treasury
    /// tile cap, and for a live Heart 16 × that same cap (DK2 reads the Treasury's variable there,
    /// as the Heart's own update does; gold-on-the-floor.md §11). The deposit <c>0x004F3C60</c> fills
    /// the room's chain from this tile, and what the tiles cannot take goes down as a new neutral
    /// pile on this tile, at one of eight points drawn at random. Then state 10, whatever is left.
    /// </summary>
    private static bool TryPutDown(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos here = creature.Tile;
        if (!world.Contains(here)) return false;

        int index = (here.Y * world.Width) + here.X;
        RoomInstance? room = RoomIndex.RoomAt(world, database, index);
        if (room is null || room.OwnerPlayerId != creature.OwnerPlayerId) return false;
        bool liveHeart = room.Definition.TypeId == RoomTypeId.DungeonHeart
            && !(world.HasPlayer(room.OwnerPlayerId) && world.Player(room.OwnerPlayerId).HeartRuined);
        if (!liveHeart && room.Definition.TypeId != RoomTypeId.Treasury) return false;

        int perTile = database[VariableTypeId.MaxGoldPerTreasuryTile];
        long capacity = (long)(liveHeart ? 16 : room.TileIndices.Count) * perTile;
        IReadOnlyList<int> roomTiles = room.TileIndices;
        long stored = 0;
        for (int i = 0; i < roomTiles.Count; i++) stored = checked(stored + world.TileAtIndex(roomTiles[i]).GoldHeld);
        long space = capacity - stored;
        // Unsigned in DK2: a room over its capacity reads as all the room in the world.
        int put = space == 0 ? 0 : space < 0 ? creature.GoldHeld : (int)Math.Min(space, creature.GoldHeld);
        if (put > 0)
        {
            int left = put - GoldStore.DepositFrom(world, database, room, index, put, context.Rng);
            creature.GoldHeld -= put;
            context.Emit(SystemName, "gold_delivered",
                $"imp={creature.Id} player={creature.OwnerPlayerId} amount={put - left} " +
                $"at=({here.X},{here.Y}) floor={left} carrying={creature.GoldHeld}");
        }
        return true;
    }

    private static bool HasSpaceForGold(WorldState world, GameDatabase database, RoomInstance room)
    {
        IReadOnlyList<int> tiles = room.TileIndices;
        for (int i = 0; i < tiles.Count; i++)
            if (GoldStore.SpaceIn(world, database, tiles[i]) > 0) return true;
        return false;
    }

    /// <summary>The eight walk points of <c>0x00698C20</c>, the outer cells of a 3×3 grid on
    /// {0x2AA, 0x7FF, 0xD54} in the table's order, the centre never.</summary>
    private static readonly (int X, int Y)[] StoreWalkPoints =
    [
        (0x2AA, 0x2AA), (0x7FF, 0x2AA), (0xD54, 0x2AA), (0x2AA, 0x7FF),
        (0xD54, 0x7FF), (0x2AA, 0xD54), (0x7FF, 0xD54), (0xD54, 0xD54),
    ];

    /// <summary>
    /// The gold-store chooser <c>0x00464400</c>: the nearest reachable store room by its first slab,
    /// a random chain tile and the first tile along the chain that takes a carrier
    /// (<see cref="GoldStore.DepositTileAlongChain"/>), then a walk to one of eight points of that
    /// tile drawn on the world stream (line 0x2FC). Only the nearest room is tried: no tile, or a
    /// walk that fails, is a plain failure with no message; the messages are for no room at all.
    /// </summary>
    private bool TryRouteToSomewhereItFits(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        int w = world.Width;
        var rooms = new List<(long Distance, RoomInstance Room)>();
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != player || room.Definition.TypeId is not
                (RoomTypeId.Treasury or RoomTypeId.DungeonHeart)) continue;
            if (!HasSpaceForGold(world, context.Database, room)) continue;
            int first = SlabChain.FirstSlab(world, room);
            long dx = CreatureState.CentreOf(first % w) - (long)creature.PosX;
            long dy = CreatureState.CentreOf(first / w) - (long)creature.PosY;
            rooms.Add((((dx * dx) >> 12) + ((dy * dy) >> 12), room));
        }
        rooms.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        foreach ((_, RoomInstance room) in rooms)
        {
            int first = SlabChain.FirstSlab(world, room);
            if (!context.Navigation.TryRoute(creature, new TilePos(first % w, first / w), Navigation.MoveOptions.ExactSpot)) continue;
            int[] chain = SlabChain.Order(world, room);
            int start = chain[context.Rng.NextInt(chain.Length)];
            int target = GoldStore.DepositTileAlongChain(world, context.Database, room, start);
            if (target < 0) return false;
            TilePos drop = new(target % w, target / w);
            (int px, int py) = StoreWalkPoints[context.Rng.NextInt(StoreWalkPoints.Length)];
            if (!context.Navigation.TryRouteTo(creature, (drop.X << 12) | px, (drop.Y << 12) | py, Navigation.MoveOptions.ExactSpot))
                return false;
            context.Emit(SystemName, "delivering",
                $"imp={creature.Id} player={player} gold={creature.GoldHeld} " +
                $"to=({drop.X},{drop.Y}) steps={creature.Body.PathTotalCount}");
            return true;
        }
        // No store: the chooser's message, 41, 42 or 43 for a Treasury.
        RoomNeedRules.NoStoreForCarrier(context, creature);
        return false;
    }
}
