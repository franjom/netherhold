// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>Carrying: objects and bodies fetched, carried and delivered.</summary>
public sealed partial class ImpTaskSystem
{
    // ------------------------------------------------------- carrying objects

    /// <summary>
    /// DK2's imp task <b>kind 8</b> (<c>0x00468520</c>): the ring scan over the whole map from a
    /// side of <see cref="DrawnStartSide"/> with the objects handler <see cref="TryObjectAt"/> on each tile, the
    /// first tile that gives a job winning; inside the drop scan, that one tile.
    /// <para><b>Second in Tier A</b>, above marked excavation and above claiming
    /// (<c>imp-task-priority.md</c> §4, ADR-0065) - so an imp that finds a book
    /// on the floor shelves it before it digs anything the keeper marked.</para>
    /// </summary>
    private bool TryTakeObject(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid)
    {
        // The kind-8 arm (0x004686D6) scans with no gate, so the draw comes first; the refusals
        // below are the objects handler's (0x0046B9C0), run on the scan's own tile (ADR-0351).
        int side = _only is null ? DrawnStartSide(context) : 0;
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) return false;
        if (creature.CarriedObjectId >= 0 || creature.CarriedCreatureId >= 0) return false;
        if (_only is TilePos only) return TryObjectAt(context, creature, grid, only);

        WorldState world = context.World;
        var rings = new DiamondRings(world, creature.Tile, side);
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (RingScanVisits(context, world, tile, creature.Tile) && TryObjectAt(context, creature, grid, tile)) return true;
        }
        return false;
    }

    /// <summary>
    /// The objects handler (<c>0x0046B9C0</c>) on one tile, the keeper's own or water: its loose
    /// objects in list order, the first that takes a job winning. A special (flag <c>0x04</c>) needs
    /// a Library with room, a crate (<c>0x10</c>) a blueprint (the crate branch <c>0x00468910</c>)
    /// or a Workshop, a book (<c>0x08</c>) a Library; a failed search skips the rest of the tile's
    /// specials, or of its crates and books together. The tile's one task is taken from a holder
    /// strictly farther from the object, the holder kicked first (imp-task-objects-and-walks.md §1).
    /// A crate's blueprint branch (<c>0x00468910</c>) runs before the shared latch is tested, and
    /// its success returns at once: no route check, no task, no holder taken from (ADR-0381).
    /// Not built: a special already standing in its room is not skipped (ours keeps no room id).
    /// </summary>
    private bool TryObjectAt(SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos at)
    {
        WorldState world = context.World;
        int index = (at.Y * world.Width) + at.X;
        ref TileState tile = ref world.TileAtIndex(index);
        if (tile.OwnerPlayerId != creature.OwnerPlayerId && !context.Database.TerrainById(tile.TerrainId).IsWater)
            return false;

        bool specialsFailed = false, othersFailed = false;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != index || !ObjectCarryRules.IsLoose(context.Database, thing)) continue;
            ObjectFlags flags = context.Database.ObjectById(thing.ObjectTypeId)!.Flags;

            if (flags.HasFlag(ObjectFlags.TypeSpecial))
            {
                if (specialsFailed) continue;
                if (ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing) < 0)
                {
                    specialsFailed = true;
                    continue;
                }
            }
            else if (flags.HasFlag(ObjectFlags.TypeCrate) || flags.HasFlag(ObjectFlags.TypeSpellBook))
            {
                if (flags.HasFlag(ObjectFlags.TypeCrate)
                    && BlueprintForLooseCrate(context, creature, grid, thing) is { } blueprint
                    && CommitCrateFetch(context, creature, blueprint, thing, "loose"))
                    return true;
                if (othersFailed) continue;
                if (ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing) < 0)
                {
                    othersFailed = true;
                    continue;
                }
            }
            else
            {
                continue;
            }

            if (!_pathfinder.TryFindNearest(grid, creature.Tile, s => s == at, _route)) continue;
            (long tx, long ty) = PileCentre(world, thing);
            if (!PointReachable(context, creature, (int)tx, (int)ty)) continue;
            if (ObjectHolder(context, creature, index) is { } holder)
            {
                (long ox, long oy) = (tx, ty);
                if (!TaskStealing.CanLoseItsTask(holder)
                    || TaskStealing.DistanceSquared(creature.PosX, creature.PosY, ox, oy)
                        >= TaskStealing.DistanceSquared(holder.PosX, holder.PosY, ox, oy))
                    continue;
                TakeTaskFrom(context, holder, creature, index);
                holder.TargetObjectId = -1;
            }

            // 0x0046B9C0: a walk that does not start moves on to the tile's next object (Q590).
            if (CommitObjectFetch(context, creature, thing, at)) return true;
        }
        return false;
    }

    /// <summary>The worker holding this tile's kind-8 task: one walking to pick up an object on it.</summary>
    private static CreatureState? ObjectHolder(SimulationContext context, CreatureState asking, int tileIndex)
    {
        foreach (CreatureState other in context.World.Creatures)
        {
            if (other.Id == asking.Id || other.OwnerPlayerId != asking.OwnerPlayerId
                || other.Activity != CreatureActivity.FetchingObject) continue;
            if (context.World.ObjectById(other.TargetObjectId) is { } target && target.TileIndex == tileIndex)
                return other;
        }
        return null;
    }

    private bool CommitObjectFetch(SimulationContext context, CreatureState creature, ObjectState thing, TilePos at)
    {
        WorldState world = context.World;
        if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.ExactSpot))
        {
            context.Emit(SystemName, "object_unreachable", $"imp={creature.Id} object={thing.Id}");
            return false;
        }
        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        creature.TargetObjectId = thing.Id;
        creature.TimerTicks = 0;
        creature.LastTaskKind = ImpTaskKind.CarryObject;
        FromTaskSelection(context, creature);
        BeginTaskWalk(context, creature, ImpStates.PickingUpObject);

        context.Emit(SystemName, "object_claimed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"at=({at.X},{at.Y}) steps={_route.Count}");
        return true;
    }

    /// <summary>Walking to a loose object, and picking it up on arrival.</summary>
    private void UpdateFetchingObject(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        ObjectState? thing = world.ObjectById(creature.TargetObjectId);

        if (thing is null || !ObjectCarryRules.IsLoose(context.Database, thing))
        {
            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={creature.TargetObjectId} reason=gone");
            creature.TargetObjectId = -1;
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != thing.TileIndex)
        {
            if (creature.HasRoute) return;

            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={thing.Id} reason=out_of_reach");
            creature.TargetObjectId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        int destination = CrateRules.IsCrate(context.Database, thing)
            ? CrateHome(context, creature)
            : ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing);

        if (destination < 0 || !RouteTo(context, creature, grid, destination))
        {
            context.Emit(SystemName, "object_abandoned",
                $"imp={creature.Id} object={thing.Id} reason=nowhere_to_take_it");
            creature.TargetObjectId = -1;
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // 30 walks it to its room in 6, resuming in the state for its kind.
        BeginTaskWalk(context, creature,
            ImpStates.CarryingStateFor(context.Database.ObjectById(thing.ObjectTypeId)!.Flags));

        creature.TargetObjectId = -1;
        ObjectCarryRules.PickUp(creature, thing);
        creature.TargetTileIndex = destination;

        context.Emit(SystemName, "object_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"to={Describe(world, destination)} steps={creature.Body.PathTotalCount}");
    }

    /// <summary>Carrying a loose object, and shelving it on arrival.</summary>
    private void UpdateCarryingObject(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        ObjectState? thing = world.ObjectById(creature.CarriedObjectId);

        if (thing is null)
        {
            // An invalid carried id: 31-33 return 0, which is idle.
            creature.CarriedObjectId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.FollowCarrier(world, creature, thing);

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            if (creature.HasRoute) return;

            int destination = CrateRules.IsCrate(context.Database, thing)
                ? CrateHome(context, creature)
                : ObjectCarryRules.TryDestinationFor(world, context.Database, creature, thing);

            if (destination >= 0 && RouteTo(context, creature, grid, destination))
            {
                // The carry state finds the tile unsuitable and asks the helper again.
                creature.TargetTileIndex = destination;
                int carrying = creature.UtilityNewState;
                StateShadow.Arrive(context, SystemName, creature);
                BeginTaskWalk(context, creature, carrying);
                return;
            }

            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            ObjectCarryRules.PutDown(creature, thing);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        if (CrateRules.IsCrate(context.Database, thing))
        {
            StoreCrate(context, creature, grid, thing, here);
            return;
        }

        // Somebody else shelved something here while this imp was walking.
        if (ObjectCarryRules.ObjectOn(world, here) is { } sitting && sitting.Id != thing.Id)
        {
            context.Emit(SystemName, "object_dropped",
                $"imp={creature.Id} object={thing.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=tile_taken");
            ObjectCarryRules.PutDown(creature, thing);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.Deliver(creature, thing, creature.OwnerPlayerId, here);

        // A book joining a Library is the room's take (0x004E12A0): it makes its item
        // available, or is destroyed when it duplicates a spell the keeper already has.
        if (context.Database.ObjectById(thing.ObjectTypeId)?.IsSpellBook == true)
            ResearchTreeRules.Shelve(world, context.Database, creature.OwnerPlayerId, thing);

        context.Emit(SystemName, "object_stored",
            $"imp={creature.Id} player={creature.OwnerPlayerId} object={thing.Id} " +
            $"type={ObjectName(context, thing)} " +
            $"{Describe(world, here)}");

        // Stored: the carry state sets 10.
        WorkEnded(context, creature);
        GoIdle(creature);
    }

    // ---------------------------------------------------------------- carrying

    /// <summary>
    /// Looks for a body to carry: a collapsed creature when
    /// <paramref name="corpses"/> is false, a corpse when it is true.
    /// <para>Two calls rather than one search for both, because DK2 has two task
    /// kinds and searches them in order — an unconscious creature anywhere on
    /// the map outranks a corpse at the imp's feet. Within a kind the nearest by
    /// walking wins (HYPOTHESIS, Q25).</para>
    /// <para><b>A body with nowhere to go is not taken</b>: a collapsed creature
    /// whose bed was sold, and an enemy whose keeper has no prison at all. A
    /// <i>full</i> prison is not that case - the imp carries the enemy there and
    /// is turned away at the door (ADR-0148).</para>
    /// </summary>
    private bool TryTakeBody(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid, bool corpses)
    {
        WorldState world = context.World;

        if (creature.CarriedCreatureId >= 0 || creature.GoldHeld > 0) return false;

        // 0x0046D810 settles the Lair and the Prison once per scan, the first time a body asks, and
        // the corpse selections their Graveyard (once, not per tile: the room index is rebuilt).
        _lairResult = _prisonResult = null;
        _corpseGraveyard = null;
        int graveyard = -1;

        // 0x0046D810 asks Route_IsPointReachable(body, 0x80) before walking to or stealing it.
        bool found = _pathfinder.TryFindNearest(
            grid, creature.Tile,
            at => InScanRange(at)
                  && BodyToCarryAt(context, creature, definition, at, corpses, graveyard) is { } candidate
                  && PointReachable(context, creature, candidate.PosX, candidate.PosY),
            _route);

        if (!found) return false;

        TilePos at = _route.Count > 0 ? _route[_route.Count - 1] : creature.Tile;
        CreatureState? body = BodyToCarryAt(context, creature, definition, at, corpses, graveyard);
        if (body is null) return false;

        // A walk that does not start takes nothing (Q590).
        if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.ExactSpot))
        {
            context.Emit(SystemName, "body_unreachable", $"imp={creature.Id} body={body.Id}");
            return false;
        }
        creature.CarriedCreatureId = -1;
        creature.TargetTileIndex = (at.Y * world.Width) + at.X;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.LastTaskKind = corpses ? ImpTaskKind.CarryCorpse : ImpTaskKind.CarryUnconscious;
        FromTaskSelection(context, creature);
        BeginTaskWalk(context, creature,
            corpses ? ImpStates.PickingUpCorpse : ImpStates.PickingUpCasualty);

        // Remembered so that the walk and the pick-up agree about which body
        // this is, even if a second one falls on the same tile before the imp
        // gets there.
        creature.TargetBodyId = body.Id;

        context.Emit(SystemName, "body_claimed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"body={body.Id} kind={(corpses ? "corpse" : "unconscious")} " +
            $"at=({at.X},{at.Y}) steps={_route.Count}");
        return true;
    }

    /// <summary>
    /// The body on <paramref name="at"/> this worker could carry somewhere, or
    /// null. Casualties on this tile prefer marked foreigners, own creatures,
    /// then other foreigners; equal-rank ties retain the simulation's spawn order.
    /// </summary>
    /// <remarks>For a casualty the rescue selector (<c>0x0046D810</c>) takes only bodies on a tile the
    /// worker's keeper owns, and posts its room message as it meets each one: an enemy's Prison
    /// result, or an own creature's Lair result when it has no bed. Duplicates are harmless: 41-43
    /// keep one record per room type.
    /// An enemy is carried only on result 4, and so is an own creature with no bed, whose Lair and
    /// point are chosen at the pick-up (<see cref="RouteOwnCasualty"/>). A corpse, likewise only on
    /// its keeper's tile, asks <see cref="GraveyardRules.ForCorpse"/> once per search.</remarks>
    private CreatureState? BodyToCarryAt(
        SimulationContext context, CreatureState worker, CreatureDef definition,
        TilePos at, bool corpses, int graveyard)
    {
        WorldState world = context.World;
        CreatureState? best = null;

        foreach (CreatureState body in world.Creatures)
        {
            if (body.Tile != at) continue;
            if (CreatureDeathRules.IsCorpse(body) != corpses) continue;
            if (!CarryRules.CanCarry(definition, worker, body)) continue;
            bool bedless = false;

            // The selector meets only bodies on a tile its keeper owns (the cell word +0x08's
            // low 12 bits against the worker's owner); elsewhere it neither posts nor carries.
            if (world.Tile(at).OwnerPlayerId != worker.OwnerPlayerId) continue;
            // The selections take only a neutral dead body lying in no room (0x0046D3C0 walks the
            // neutral player's list, 0x0046CF40 tests the owner; both test +0x132).
            if (corpses && (body.GraveyardSlot >= 0 || body.BodyOwnerPlayerId != PlayerIds.Neutral)) continue;
            if (corpses)
            {
                _corpseGraveyard ??= GraveyardRules.ForCorpse(context, worker, search: _only is null);
                graveyard = _corpseGraveyard.Value;
            }

            if (!corpses && body.OwnerPlayerId != worker.OwnerPlayerId)
            {
                _prisonResult ??= RoomNeedRules.RescueResult(context, worker, definition, RoomTypeId.Prison);
                RoomNeedRules.ForRescue(context, worker.OwnerPlayerId, _prisonResult.Value, RoomTypeId.Prison);
                if (_prisonResult != RoomNeedRules.RescueRoom.Usable) continue;
            }
            else if (!corpses && !LairRules.HasBed(world, context.Database, body))
            {
                _lairResult ??= RoomNeedRules.RescueResult(context, worker, definition, RoomTypeId.Lair);
                RoomNeedRules.ForRescue(context, worker.OwnerPlayerId, _lairResult.Value, RoomTypeId.Lair);
                // DK2 raises the carry on result 4 whatever the creature; ours carries only one with a
                // lair object, which every own creature with a dying state has.
                if (_lairResult != RoomNeedRules.RescueRoom.Usable
                    || !LairRules.NeedsBed(context.Database.CreatureById(body.CreatureTypeId))) continue;
                bedless = true;
            }

            // A corpse goes to the graveyard already found; a collapsed
            // creature goes to its own bed, which is a per-body question and a
            // cheap one.
            // A bedless body's Lair is chosen at the pick-up; the selector has settled it has one.
            if (!bedless && (corpses ? graveyard : CarryRules.TryDestinationFor(world, context.Database, worker, body)) < 0)
                continue;
            int rank = corpses ? 0 : RescuePriorityRules.Rank(body, worker.OwnerPlayerId);
            int bestRank = best is null ? int.MaxValue
                : corpses ? 0 : RescuePriorityRules.Rank(best, worker.OwnerPlayerId);
            if (rank < bestRank || (rank == bestRank && body.Id < best!.Id)) best = body;
        }

        return best;
    }

    private static bool IsOwnCasualty(CreatureState worker, CreatureState body) =>
        !CreatureDeathRules.IsCorpse(body) && body.OwnerPlayerId == worker.OwnerPlayerId;

    /// <summary>The casualty chooser <c>0x0046E610</c> for the keeper's own creature, routed: its bed
    /// when a route reaches it, else a room point (<see cref="RoomWalkRules.RoomPoint"/>, a draw) in
    /// the nearest Lair with space (<see cref="RoomNeedRules.NearestLairWithSpace"/>); -1 with neither.</summary>
    private int RouteOwnCasualty(SimulationContext context, CreatureState worker, WorldPathGrid grid, CreatureState body)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int bed = LairRules.BedTileIndex(world, database, body);
        if (bed >= 0 && RouteTo(context, worker, grid, bed)) return bed;
        if (!LairRules.NeedsBed(database.CreatureById(body.CreatureTypeId))) return -1;
        if (RoomNeedRules.NearestLairWithSpace(context, worker, database.CreatureById(worker.CreatureTypeId), body) is not { } lair
            || RoomWalkRules.RoomPoint(world, database, lair, context.Rng) is not int point)
            return -1;
        return RouteTo(context, worker, grid, point) ? point : -1;
    }

    /// <summary>Walking to a body, and picking it up on arrival.</summary>
    private void UpdateFetchingBody(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        CreatureState? body = world.CreatureById(creature.TargetBodyId);

        // Somebody else took it, it rotted, or it was rescued by another imp.
        if (body is null || !CarryRules.IsCarryable(body))
        {
            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={creature.TargetBodyId} reason=gone");
            ForgetBody(creature);
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Still on the road.
        if (creature.Tile != body.Tile)
        {
            if (creature.HasRoute) return;

            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={body.Id} reason=out_of_reach");
            ForgetBody(creature);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        int destination = IsOwnCasualty(creature, body) ? RouteOwnCasualty(context, creature, grid, body)
            : CreatureDeathRules.IsCorpse(body) ? GraveyardRules.CorpseDestination(context, creature.OwnerPlayerId)
            : CarryRules.TryDestinationFor(world, context.Database, creature, body);
        if (destination < 0 || (!IsOwnCasualty(creature, body) && !RouteTo(context, creature, grid, destination)))
        {
            context.Emit(SystemName, "body_abandoned",
                $"imp={creature.Id} body={body.Id} reason=nowhere_to_take_it");
            ForgetBody(creature);
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // 40 walks a corpse to 41; 42 chooses by ownership through 0x0046E610.
        BeginTaskWalk(context, creature,
            CreatureDeathRules.IsCorpse(body) ? ImpStates.CarryingCorpse
            : body.OwnerPlayerId == creature.OwnerPlayerId ? ImpStates.CarryingToLair
            : ImpStates.CarryingToPrison);

        ForgetBody(creature);
        CarryRules.PickUp(creature, body);
        creature.TargetTileIndex = destination;

        context.Emit(SystemName, "body_picked_up",
            $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
            $"kind={(CreatureDeathRules.IsCorpse(body) ? "corpse" : "unconscious")} " +
            $"to={Describe(world, destination)} steps={creature.Body.PathTotalCount}");
    }

    /// <summary>Carrying a body, and delivering it on arrival.</summary>
    private void UpdateCarryingBody(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        CreatureState? body = world.CreatureById(creature.CarriedCreatureId);

        // It rotted in the imp's arms, which a long walk to a distant graveyard
        // really can do (Q164).
        if (body is null)
        {
            // A bad carried id: the carry state returns 0, which is idle.
            creature.CarriedCreatureId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        // The body goes where the arms go, and this runs after the mover, so it
        // is never a tick behind.
        CarryRules.FollowCarrier(creature, body);

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        // State 45 delivers only on a Lair tile; elsewhere (the bed's tile sold during the walk) it
        // runs the casualty chooser again (0x00468420).
        // State 41 lays a corpse only on a free floor slot of a Graveyard with a place
        // (0x004E6380, 0x004E6490); elsewhere it looks for another (0x00467AA0).
        bool own = IsOwnCasualty(creature, body);
        bool corpse = CreatureDeathRules.IsCorpse(body);
        if (here != creature.TargetTileIndex
            || (own && RoomIndex.RoomAt(world, context.Database, here) is not { Definition.TypeId: RoomTypeId.Lair })
            || (corpse && !GraveyardRules.TryLay(world, context.Database, body, here)))
        {
            if (here != creature.TargetTileIndex && creature.HasRoute) return;

            // The destination moved or the route was lost. Try again from here;
            // failing that, put the body down where it stands.
            int destination = own ? RouteOwnCasualty(context, creature, grid, body)
                : corpse ? GraveyardRules.CorpseRedirect(context, creature, context.Database.CreatureById(creature.CreatureTypeId))
                : CarryRules.TryDestinationFor(world, context.Database, creature, body);
            if (destination >= 0 && (own || RouteTo(context, creature, grid, destination)))
            {
                // 41, 43 and 45 each re-route to themselves.
                creature.TargetTileIndex = destination;
                int carrying = creature.UtilityNewState;
                StateShadow.Arrive(context, SystemName, creature);
                BeginTaskWalk(context, creature, carrying);
                return;
            }

            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            CarryRules.PutDown(creature, body);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        StateShadow.Arrive(context, SystemName, creature);
        Deliver(context, creature, body);
    }

    /// <summary>
    /// Arrival: a corpse laid in the graveyard, the keeper's own collapsed
    /// creature into its bed, an enemy's into a cell.
    /// </summary>
    private static void Deliver(
        SimulationContext context, CreatureState creature, CreatureState body)
    {
        WorldState world = context.World;
        CreatureDef definition = context.Database.CreatureById(body.CreatureTypeId);

        // State 41 has laid it (GraveyardRules.TryLay): it lies on its floor slot, holding the place,
        // until its lifetime runs out and it is buried (0x004B1070, CreatureHealthSystem).
        if (CreatureDeathRules.IsCorpse(body))
        {
            CarryRules.PutDown(creature, body);
            context.Emit(SystemName, "corpse_laid",
                $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                $"type={definition.Name} {Describe(world, body.GraveyardSlot)}");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        CarryRules.PutDown(creature, body);

        // Somebody else's creature goes into a cell rather than a bed - kind
        // 13's other branch, and the only route into a prison there is
        // (ADR-0064). Admission is decided HERE, at the door: a prison that
        // filled up during the walk turns the body away onto the rim (ADR-0148);
        // with every prison already full no imp sets off (ADR-0354).
        if (body.OwnerPlayerId != creature.OwnerPlayerId)
        {
            // Our one walk ends at the cell, so 43's inner leg to 44 and 44's own walks fall
            // in this tick: admitted walks away through 46, refused puts it down through 47
            // and 48, and both end in 10.
            WalkOn(context, creature, ImpStates.AtThePrisonDoor);
            WorkerPickupRules.SetEnabled(creature, false);
            int cell = creature.TargetTileIndex;
            if (PrisonRules.AdmitsAt(world, context.Database, creature.OwnerPlayerId, cell)
                && PrisonRules.Imprison(
                    context.Hooks, body, creature.OwnerPlayerId, cell))
            {
                WalkOn(context, creature, ImpStates.LeavingThePrisoner);
                WorkerPickupRules.SetEnabled(creature, true);
                context.Emit(SystemName, "creature_imprisoned",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                    $"type={definition.Name} owner={body.OwnerPlayerId} " +
                    $"{Describe(world, cell)} health={body.Health}");
            }
            else
            {
                // 47 walks to the room point and 48 puts the body down there; our walk is elided,
                // as 44's is, so the body is set on the point.
                if (RoomIndex.RoomAt(world, context.Database, cell) is RoomInstance prison
                    && PrisonRules.IsPrisonOf(prison, creature.OwnerPlayerId)
                    && PrisonRules.RefusedBodyPoint(world, context.Database, prison, context.Rng) is int point)
                {
                    body.PosX = CreatureState.CentreOf(point % world.Width);
                    body.PosY = CreatureState.CentreOf(point / world.Width);
                }

                context.Emit(SystemName, "body_dropped",
                    $"imp={creature.Id} body={body.Id} " +
                    $"at=({body.Tile.X},{body.Tile.Y}) reason=not_admitted");
                WalkOn(context, creature, ImpStates.TakingTheRefusedBody);
                WorkerPickupRules.SetEnabled(creature, true);
                WalkOn(context, creature, ImpStates.PuttingDownTheRefusedBody);
            }

            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // 45 kicks the body to 1 and 79, which goes to a bed in this room or makes one here.
        int bed = LairRules.BedTileIndex(world, context.Database, body);
        int at = (body.Tile.Y * world.Width) + body.Tile.X;
        bool bedHere = bed >= 0 && RoomIndex.RoomAt(world, context.Database, at) is { } lair && lair.Contains(bed);
        if (bedHere ? CreatureDeathRules.Rescue(context.Hooks, body) : CreatureDeathRules.RescueIntoLair(context, body))
        {
            context.Emit(SystemName, "creature_rescued",
                $"imp={creature.Id} player={creature.OwnerPlayerId} body={body.Id} " +
                $"type={definition.Name} {Describe(world, creature.TargetTileIndex)} " +
                $"health={body.Health} (the lair heals it from here, ADR-0049)");
            WorkEnded(context, creature);
        }
        else
        {
            context.Emit(SystemName, "body_dropped",
                $"imp={creature.Id} body={body.Id} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=bed_gone");
            StateShadow.ToIdle(context, SystemName, creature);
        }

        GoIdle(creature);
    }

    /// <summary>An imp task's walk, which DK2 starts through the walk wrapper <c>0x004651A0</c>
    /// (45 call sites, every imp task handler; not the wander, state 87): its ability scan, then
    /// the walk. Ours begins the walk only once a route is found, where DK2 scans before routing.</summary>
    private static void BeginTaskWalk(SimulationContext context, CreatureState creature, int resume)
    {
        CastOnTheWalk(context, creature);
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
    }

    /// <summary>
    /// The walk wrapper's ability scan (<c>0x004651A0</c>, ADR-0385): slots 0 to 2 within the
    /// creature's level, with a spell, and ready by <c>0x0048C800</c>. A teleport slot (flag
    /// <c>0x20</c>) ends the scan; a speed slot (<c>0x40</c>, the Imp's Imp Haste in slot 0) is cast on
    /// the spot: the slot stamped, then <c>0x0048CAD0</c> with the record's two words, refused while a
    /// speed effect runs. The teleport itself is not built (Q418), and the speed-blur display word
    /// <c>+0x4DD</c> is not kept.
    /// </summary>
    private static void CastOnTheWalk(SimulationContext context, CreatureState creature)
    {
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        long now = context.Tick.Value;
        int count = Math.Min(definition.Spells.Count, SpellRules.Slots);
        for (int slot = 0; slot < count; slot++)
        {
            if (definition.Spells[slot].LevelAvailable > creature.Level) continue;
            if (!SpellRules.CanCast(database, creature, definition, slot, now)) continue;
            CreatureSpellDef spell = SpellRules.SpellOf(database, definition, slot)!;
            if ((spell.Flags & PossessionRules.TeleportSpellFlag) != 0) break;
            if ((spell.Flags & PossessionRules.SpeedSpellFlag) == 0) continue;
            creature.SpellStamps[slot] = now;
            StatusEffectRules.ModifySpeed(context, creature, unchecked((int)spell.ShotData1), unchecked((int)spell.ShotData2));
        }
    }

    /// <summary>A walk of DK2's that ours does not make: 6 resuming in the state, arrived at on
    /// the same tick.</summary>
    private static void WalkOn(SimulationContext context, CreatureState creature, int resume)
    {
        if (!CreatureStateMachine.HasState(creature)) return;
        BeginTaskWalk(context, creature, resume);
        StateShadow.Arrive(context, SystemName, creature);
    }

    /// <summary>
    /// An object's name for an event line, or its raw type id when the record
    /// is missing - which is a level referring to an object the OBJECTS table
    /// does not have, and is worth seeing rather than crashing on.
    /// </summary>
    private static string ObjectName(SimulationContext context, ObjectState thing)
    {
        return context.Database.ObjectById(thing.ObjectTypeId)?.Name ?? $"#{thing.ObjectTypeId}";
    }

    /// <summary>Routes a worker to one tile by index. Returns false if there is no way.</summary>
    private bool RouteTo(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, int tileIndex)
    {
        WorldState world = context.World;
        var to = new TilePos(tileIndex % world.Width, tileIndex / world.Width);

        bool prison = context.Database.RoomByTerrain(world.Tile(to).TerrainId)?.TypeId == RoomTypeId.Prison;
        return context.Navigation.TryRoute(creature, to,
            prison ? Navigation.MoveOptions.PrisonDelivery : Navigation.MoveOptions.ExactSpot);
    }

    /// <summary>Clears the "body I am walking towards" note.</summary>
    private static void ForgetBody(CreatureState creature)
    {
        creature.TargetBodyId = -1;
    }
}
