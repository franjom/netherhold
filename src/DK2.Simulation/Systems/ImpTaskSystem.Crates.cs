// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>Task kinds 9 and 10 and states 34 to 37: a crate carried to a placed trap or door,
/// and the build (ADR-0214, <c>workshop-crates.md</c>).</summary>
/// <remarks>Three task handlers start the job, each walking to the crate in state 6 and resuming
/// in 34 (a trap) or 36 (a door). The whole-map one <c>0x0046BD70</c> takes the keeper's
/// blueprint list in order and, for each, the first Workshop holding a crate for it. The drop
/// scan's <c>0x0046C1A0</c> takes a blueprint within reach of the drop and the nearest Workshop
/// with its crate. Kind 8's <c>0x00468910</c> takes a loose crate straight to the nearest
/// blueprint that needs it. 34 and 36 take the crate off their tile, out of its Workshop, and
/// walk it to the blueprint, resuming in 35 and 37, which set it down and build the thing if it
/// is still there. A task is one imp's: none of the three takes a blueprint another imp is
/// already on.</remarks>
public sealed partial class ImpTaskSystem
{
    private readonly List<int> _crateTiles = [];

    /// <summary>The whole-map handler for kinds 9 and 10, <c>0x0046BD70</c>: the keeper's blueprint
    /// list in order. An entry no longer under construction leaves the list and the walk starts
    /// again from the head; one another imp holds is passed over; otherwise the keeper's rooms
    /// are asked in turn, and the first Workshop holding a crate for it, with both the crate and
    /// the blueprint reachable, is the job. No distance is measured.</summary>
    /// <remarks>Ours takes the rooms in <see cref="RoomIndex"/> order, where DK2 walks the keeper's
    /// room list.</remarks>
    private bool TryTakeBlueprint(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (!CanTakeACrate(creature) || !context.World.HasPlayer(creature.OwnerPlayerId)) return false;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        List<BlueprintRef> list = world.Player(creature.OwnerPlayerId).Blueprints;
        IReadOnlyList<RoomInstance>? rooms = null;

        for (int i = 0; i < list.Count;)
        {
            BlueprintRef blueprint = list[i];
            if (!CrateRules.IsBlueprint(world, blueprint))
            {
                list.RemoveAt(i);
                i = 0;
                continue;
            }

            if (!IsHeld(world, creature, blueprint))
            {
                rooms ??= RoomIndex.Build(world, database);
                byte crateObject = CrateRules.CrateFor(database, world, blueprint);
                foreach (RoomInstance room in rooms)
                {
                    if (room.OwnerPlayerId != creature.OwnerPlayerId || room.Definition.TypeId != RoomTypeId.Workshop) continue;
                    if (CrateRules.CrateIn(world, database, room, blueprint.Kind, crateObject) is not { } crate) continue;
                    if (!Reachable(grid, creature, CrateRules.TileOf(world, blueprint)) || !Reachable(grid, creature, crate.TileIndex)) continue;
                    return CommitCrateFetch(context, creature, blueprint, crate, "blueprint_list");
                }
            }

            i++;
        }

        return false;
    }

    /// <summary>The drop scan's handler for kinds 9 and 10, <c>0x0046C1A0</c>: a blueprint on a
    /// tile of the keeper's within the drop's reach, not held by another imp, and the Workshop
    /// with a crate for it nearest by DK2's measure, the crate reachable. The measure is the
    /// Workshop's distance from the imp for a trap and from the door for a door.</summary>
    /// <remarks>Ours measures to the room's first tile rather than its position word.</remarks>
    private bool TryTakeBlueprintNear(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (!CanTakeACrate(creature)) return false;
        WorldState world = context.World;
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, context.Database);
        (BlueprintRef Blueprint, ObjectState Crate)? job = null;

        // The drop scan confines the search to one tile, where the nearest-first search would
        // flood the worker's whole region to meet it: ask that tile alone, as the search would
        // meet it once, when reached (ADR-0453).
        bool found;
        if (_only is TilePos only)
        {
            found = Reaches(grid, creature, only) && (job = BlueprintJobAt(context, creature, grid, rooms, only)) != null;
            if (!found) _route.Clear();
        }
        else
        {
            found = _pathfinder.TryFindNearest(
                grid, creature.Tile,
                at => InScanRange(at) && (job = BlueprintJobAt(context, creature, grid, rooms, at)) != null,
                _route);
        }
        if (!found || job is not { } taken) return false;
        return CommitCrateFetch(context, creature, taken.Blueprint, taken.Crate, "dropped");
    }

    private (BlueprintRef, ObjectState)? BlueprintJobAt(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, IReadOnlyList<RoomInstance> rooms, TilePos at)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int tile = (at.Y * world.Width) + at.X;
        if (world.TileAtIndex(tile).OwnerPlayerId != creature.OwnerPlayerId) return null;

        foreach (byte kind in (ReadOnlySpan<byte>)[CrateRules.TrapKind, CrateRules.DoorKind])
        {
            int count = kind == CrateRules.DoorKind ? world.Doors.Count : world.Traps.Count;
            for (int i = 0; i < count; i++)
            {
                var blueprint = new BlueprintRef(kind, kind == CrateRules.DoorKind ? world.Doors[i].Id : world.Traps[i].Id);
                if (CrateRules.TileOf(world, blueprint) != tile || !CrateRules.IsBlueprint(world, blueprint)) continue;
                if (CrateRules.OwnerOf(world, blueprint) != creature.OwnerPlayerId || IsHeld(world, creature, blueprint)) continue;

                byte crateObject = CrateRules.CrateFor(database, world, blueprint);
                ObjectState? best = null;
                long bestDistance = long.MaxValue;
                TilePos from = kind == CrateRules.TrapKind ? creature.Tile : at;
                foreach (RoomInstance room in rooms)
                {
                    if (room.OwnerPlayerId != creature.OwnerPlayerId || room.Definition.TypeId != RoomTypeId.Workshop) continue;
                    long distance = EatingRules.DistanceSquared(world, from, room.TileIndices[0]);
                    if (distance >= bestDistance) continue;
                    if (CrateRules.CrateIn(world, database, room, kind, crateObject) is not { } crate) continue;
                    if (!Reachable(grid, creature, crate.TileIndex)) continue;
                    best = crate;
                    bestDistance = distance;
                }

                if (best != null) return (blueprint, best);
            }
        }

        return null;
    }

    /// <summary>Kind 8's crate branch, <c>0x00468910</c>, asked before the crate's Workshop: the
    /// keeper's traps (for a trap crate) or doors (a door crate) under construction that need
    /// this crate's object, the nearest to the imp that no other imp holds and that the imp can
    /// reach.</summary>
    private BlueprintRef? BlueprintForLooseCrate(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, ObjectState crate)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (CrateRules.KindOf(database, crate.ObjectTypeId) is not { } kind) return null;

        int count = kind == CrateRules.DoorKind ? world.Doors.Count : world.Traps.Count;
        BlueprintRef? best = null;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var blueprint = new BlueprintRef(kind, kind == CrateRules.DoorKind ? world.Doors[i].Id : world.Traps[i].Id);
            if (CrateRules.OwnerOf(world, blueprint) != creature.OwnerPlayerId || !CrateRules.IsBlueprint(world, blueprint)) continue;
            if (CrateRules.CrateFor(database, world, blueprint) != crate.ObjectTypeId) continue;

            int tile = CrateRules.TileOf(world, blueprint);
            long distance = EatingRules.DistanceSquared(world, creature.Tile, tile);
            if (distance >= bestDistance || IsHeld(world, creature, blueprint) || !Reachable(grid, creature, tile)) continue;
            best = blueprint;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>The walk to the crate in state 6, resuming in 34 or 36; the task records the
    /// blueprint's tile and the crate's object.</summary>
    private bool CommitCrateFetch(
        SimulationContext context, CreatureState creature, BlueprintRef blueprint, ObjectState crate, string how)
    {
        WorldState world = context.World;
        var at = new TilePos(crate.TileIndex % world.Width, crate.TileIndex / world.Width);
        if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.ExactSpot)) return false;

        creature.TargetTileIndex = CrateRules.TileOf(world, blueprint);
        creature.TargetWork = default;
        creature.TargetObjectId = crate.Id;
        creature.TimerTicks = 0;
        creature.LastTaskKind = TaskKindOf(blueprint.Kind);
        FromTaskSelection(context, creature);
        BeginTaskWalk(context, creature, FetchStateOf(blueprint.Kind));

        context.Emit(SystemName, "crate_claimed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} crate={crate.Id} " +
            $"type={ObjectName(context, crate)} for={Describe(world, creature.TargetTileIndex)} how={how}");
        return true;
    }

    /// <summary>A crate trip copied from a DK2 capture (<see cref="WorldResync"/>): the walk
    /// <see cref="CommitCrateFetch"/> starts, to the crate resuming in 34 or 36, or, with the crate
    /// already in hand, the walk state 34 or 36 starts to the blueprint, resuming in 35 or 37. No
    /// ability is cast on the walk; a resync is not a choice. False when no route reaches it.</summary>
    internal static bool ReissueCrateJob(
        SimulationContext context, CreatureState creature, int blueprintTile, byte kind, ObjectState crate, bool carrying)
    {
        WorldState world = context.World;
        int to = carrying ? blueprintTile : crate.TileIndex;
        if (!context.Navigation.TryRoute(creature, new TilePos(to % world.Width, to / world.Width), Navigation.MoveOptions.ExactSpot))
            return false;

        creature.TargetTileIndex = blueprintTile;
        creature.TargetWork = default;
        creature.TargetObjectId = carrying ? -1 : crate.Id;
        creature.TimerTicks = 0;
        creature.SecretDoorTask = false;
        creature.LastTaskKind = TaskKindOf(kind);
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint,
            carrying ? InstallStateOf(kind) : FetchStateOf(kind));
        context.Emit(WorldResync.SystemName, "crate_job_reissued",
            $"imp={creature.Id} crate={crate.Id} for={Describe(world, blueprintTile)} carrying={carrying}");
        return true;
    }

    /// <summary>States 34 and 36 on arrival: a crate of the task's kind and object on this tile
    /// that nobody carries; the walk to the blueprint's tile, resuming in 35 or 37; and only
    /// then the crate leaves its Workshop and is picked up. None, or no walk: the task is let go
    /// and the imp sets 10.</summary>
    private void UpdateFetchingCrate(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return;
            StateShadow.Arrive(context, SystemName, creature);
        }

        byte kind = KindOfState(creature.Dk2StateId);
        byte crateObject = world.ObjectById(creature.TargetObjectId)?.ObjectTypeId ?? 0;
        var to = new TilePos(creature.TargetTileIndex % world.Width, creature.TargetTileIndex / world.Width);
        foreach (ObjectState crate in world.Objects)
        {
            if (crate.TileIndex != here || crate.CarriedByCreatureId >= 0 || crate.InHand) continue;
            if (crate.ObjectTypeId != crateObject || CrateRules.KindOf(database, crate.ObjectTypeId) != kind) continue;
            if (!RouteTo(context, creature, grid, creature.TargetTileIndex)) continue;

            if (RoomIndex.RoomAt(world, database, here) is { } room && CrateRules.IsListedIn(world, database, room, crate))
                CrateRules.Unlist(world, database, room, crate);
            creature.TargetObjectId = -1;
            ObjectCarryRules.PickUp(creature, crate);
            BeginTaskWalk(context, creature, InstallStateOf(kind));
            context.Emit(SystemName, "crate_picked_up",
                $"imp={creature.Id} player={creature.OwnerPlayerId} crate={crate.Id} " +
                $"to=({to.X},{to.Y}) steps={creature.Body.PathTotalCount}");
            return;
        }

        context.Emit(SystemName, "crate_abandoned",
            $"imp={creature.Id} crate={creature.TargetObjectId} reason=no_crate_or_route");
        creature.TargetObjectId = -1;
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        GoIdle(creature);
    }

    /// <summary>States 35 and 37 on arrival: the crate is set down where the imp stands, and if a
    /// blueprint of the kind needing it is still on the task's tile the crate is used up and the
    /// thing is built, the blueprint leaving the list. Either way the task is let go and the imp
    /// sets 10; a crate not used lies loose. A carried id gone returns 0, which is idle.</summary>
    private void UpdateCarryingCrate(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        ObjectState? crate = world.ObjectById(creature.CarriedObjectId);
        if (crate is null)
        {
            creature.CarriedObjectId = -1;
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        ObjectCarryRules.FollowCarrier(world, creature, crate);
        if (creature.HasRoute) return;

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (here != creature.TargetTileIndex)
        {
            // The walk ended short of the blueprint: state 6 fails, which is idle.
            context.Emit(SystemName, "crate_dropped",
                $"imp={creature.Id} crate={crate.Id} at=({creature.Tile.X},{creature.Tile.Y}) reason=no_route");
            ObjectCarryRules.PutDown(creature, crate);
            StateShadow.ToIdle(context, SystemName, creature);
            GoIdle(creature);
            return;
        }

        if (StateShadow.IsTravelling(creature)) StateShadow.Arrive(context, SystemName, creature);
        byte kind = KindOfState(creature.Dk2StateId);
        ObjectCarryRules.PutDown(creature, crate);

        if (CrateRules.BlueprintAt(world, context.Database, creature.OwnerPlayerId, kind, here, crate.ObjectTypeId) is { } blueprint)
        {
            CrateRules.Build(world, context.Database, blueprint, crate);
            context.Emit(SystemName, kind == CrateRules.DoorKind ? "door_built" : "trap_built",
                $"imp={creature.Id} player={creature.OwnerPlayerId} {Describe(world, creature.TargetTileIndex)}");
        }
        else
        {
            context.Emit(SystemName, "crate_dropped",
                $"imp={creature.Id} crate={crate.Id} at=({creature.Tile.X},{creature.Tile.Y}) reason=no_blueprint");
        }

        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        GoIdle(creature);
    }

    /// <summary>State 32 on arrival (<c>0x00466DF0</c>): the crate is set down; on a tile of the
    /// keeper's own Workshop that takes it (<see cref="CrateRules.List"/>) it joins the room and
    /// the imp sets 10. Otherwise the destination helper is asked again, and with nowhere to go
    /// the crate lies where it was put down and the imp is idle.</summary>
    private void StoreCrate(SimulationContext context, CreatureState creature, WorldPathGrid grid, ObjectState crate, int here)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        ObjectCarryRules.PutDown(creature, crate);
        crate.TileIndex = here;

        if (RoomIndex.RoomAt(world, database, here) is { } room && room.Definition.TypeId == RoomTypeId.Workshop
            && room.OwnerPlayerId == creature.OwnerPlayerId && CrateRules.List(context.Hooks, room, crate))
        {
            context.Emit(SystemName, "object_stored",
                $"imp={creature.Id} player={creature.OwnerPlayerId} object={crate.Id} " +
                $"type={ObjectName(context, crate)} {Describe(world, crate.TileIndex)}");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        int destination = CrateHome(context, creature);
        if (destination >= 0 && destination != here && RouteTo(context, creature, grid, destination))
        {
            ObjectCarryRules.PickUp(creature, crate);
            creature.TargetTileIndex = destination;
            int carrying = creature.UtilityNewState;
            StateShadow.Arrive(context, SystemName, creature);
            BeginTaskWalk(context, creature, carrying);
            return;
        }

        context.Emit(SystemName, "object_dropped",
            $"imp={creature.Id} object={crate.Id} at=({creature.Tile.X},{creature.Tile.Y}) reason=workshop_full");
        StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
    }

    /// <summary>Whether another imp holds the blueprint's task: it is on the way to the crate or
    /// carrying it, for that tile and kind.</summary>
    private static bool IsHeld(WorldState world, CreatureState asking, BlueprintRef blueprint)
    {
        int tile = CrateRules.TileOf(world, blueprint);
        int fetch = FetchStateOf(blueprint.Kind), install = InstallStateOf(blueprint.Kind);
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id == asking.Id || other.OwnerPlayerId != asking.OwnerPlayerId
                || other.TargetTileIndex != tile) continue;
            int state = StateShadow.IsTravelling(other) ? other.UtilityNewState : other.Dk2StateId;
            if (state == fetch || state == install) return true;
        }

        return false;
    }

    /// <summary>Whether the worker can walk to the tile, from the worker's one flood
    /// (<see cref="Reaches"/>). This runs inside the nearest-first searches' predicates, where the
    /// pathfinder's own buffers are in use; the flood has its own, and leaves the route alone.</summary>
    private bool Reachable(WorldPathGrid grid, CreatureState creature, int tileIndex)
    {
        var to = new TilePos(tileIndex % grid.Width, tileIndex / grid.Width);
        return to == creature.Tile || Flooded(grid, creature).Reaches(to);
    }

    /// <summary>The imp's pick-up <c>0x0048B4A0</c> refuses while it holds something.</summary>
    private static bool CanTakeACrate(CreatureState creature) =>
        creature.CarriedObjectId < 0 && creature.CarriedCreatureId < 0 && creature.GoldHeld <= 0;

    /// <summary>For a crate going home: a random tile of the nearest Workshop with space that no
    /// furniture slot takes, as the destination helper's <c>0x004E8430</c> picks, or -1.</summary>
    private int CrateHome(SimulationContext context, CreatureState creature)
    {
        if (ObjectCarryRules.WorkshopFor(context.World, context.Database, creature) is not { } workshop) return -1;
        CrateRules.CrateTiles(context.World, context.Database, workshop, _crateTiles);
        return _crateTiles.Count == 0 ? -1 : _crateTiles[context.Rng.NextInt(_crateTiles.Count)];
    }

    private static bool IsCrateState(int state) =>
        state is CrateRules.FetchTrapCrateState or CrateRules.InstallTrapState
            or CrateRules.FetchDoorCrateState or CrateRules.InstallDoorState;

    private static bool IsCrateJob(CreatureState creature) =>
        IsCrateState(StateShadow.IsTravelling(creature) ? creature.UtilityNewState : creature.Dk2StateId);

    private static byte KindOfState(int state) =>
        state is CrateRules.FetchDoorCrateState or CrateRules.InstallDoorState ? CrateRules.DoorKind : CrateRules.TrapKind;

    private static int FetchStateOf(byte kind) =>
        kind == CrateRules.DoorKind ? CrateRules.FetchDoorCrateState : CrateRules.FetchTrapCrateState;

    private static int InstallStateOf(byte kind) =>
        kind == CrateRules.DoorKind ? CrateRules.InstallDoorState : CrateRules.InstallTrapState;

    private static ImpTaskKind TaskKindOf(byte kind) =>
        kind == CrateRules.DoorKind ? ImpTaskKind.BuildDoor : ImpTaskKind.BuildTrap;
}
