// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

public sealed partial class ImpTaskSystem
{
    private static bool IsSecretDoorState(CreatureState creature) =>
        creature.Dk2StateId is ImpStates.SecretDoorApproach or ImpStates.SecretDoorWork
        || creature.Dk2StateId == StateShadow.TravelToPoint && creature.UtilityNewState == ImpStates.SecretDoorApproach;

    private bool TryTakeSecretDoor(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        long nearest = int.MaxValue;
        TilePos? chosen = null;
        byte chosenSlot = MiningSlots.None;
        CreatureState? chosenHolder = null;
        // Pool insertion is newest first; equal distances keep the first entry.
        foreach (int index in Enumerable.Range(0, world.Tiles.Length)
            .Where(i => world.TileAtIndex(i).SecretDoorTasks.Contains(creature.OwnerPlayerId))
            .OrderByDescending(i => world.SecretDoorEntries.GetValueOrDefault((creature.OwnerPlayerId,
                new TilePos(i % world.Width, i / world.Width)))?.Order ?? 0))
        {
            ref TileState tile = ref world.TileAtIndex(index);
            if (!tile.SecretDoor)
            {
                world.RemoveSecretDoorEntry(creature.OwnerPlayerId, new TilePos(index % world.Width, index / world.Width));
                continue;
            }
            var target = new TilePos(index % world.Width, index / world.Width);
            long distance = TaskStealing.DistanceSquared(creature.PosX, creature.PosY,
                CreatureState.CentreOf(target.X), CreatureState.CentreOf(target.Y));
            if (distance >= nearest) continue;
            (int side, int step) = MiningSlots.StartSide(creature.Tile.X - target.X, creature.Tile.Y - target.Y);
            for (int turn = 0; turn < 4; turn++, side = (side + step) & 3)
            {
                var stand = new TilePos(target.X + SideDx[side], target.Y + SideDy[side]);
                if (!world.Contains(stand) || context.Database.TerrainById(world.Tile(stand).TerrainId).IsSolid) continue;
                int cx = CreatureState.CentreOf(stand.X), cy = CreatureState.CentreOf(stand.Y);
                if (!PointReachable(context, creature, cx, cy)) continue;
                byte slot = (byte)(MiningSlots.Face(stand, target) * MiningSlots.PerFace + 1);
                CreatureState? holder = SecretDoorHolder(world, creature, index, slot);
                if (holder is not null && (!TaskStealing.CanLoseItsTask(holder)
                    || distance >= TaskStealing.DistanceSquared(holder.PosX, holder.PosY, cx, cy))) continue;
                (nearest, chosen, chosenSlot, chosenHolder) = (distance, target, slot, holder);
                if (holder is null) break;
            }
        }
        if (chosen is not TilePos at) return false;
        if (chosenHolder is not null)
        {
            if (!CreatureStateMachine.TrySetState(context.Hooks, chosenHolder, ImpStates.TaskSelection)) return false;
            TakeTaskFrom(context, chosenHolder, creature, at.Y * world.Width + at.X);
        }
        return TakeSecretDoorSlot(context, creature, at, chosenSlot);
    }

    private static int SecretDoorSide(byte slot) => (slot / MiningSlots.PerFace) switch { 0 => 2, 1 => 1, 2 => 3, _ => 0 };

    private static CreatureState? SecretDoorHolder(WorldState world, CreatureState creature, int index, byte slot)
    {
        var tile = new TilePos(index % world.Width, index / world.Width);
        int holder = world.SecretDoorEntries.GetValueOrDefault((creature.OwnerPlayerId, tile))?.Holders[SecretDoorSide(slot)] ?? 0;
        return world.CreatureById(holder);
    }

    private bool TrySecretDoorAt(SimulationContext context, CreatureState creature, TilePos target)
    {
        WorldState world = context.World;
        ref TileState tile = ref world.Tile(target);
        PlayerId owner = creature.OwnerPlayerId;
        if (!tile.SecretDoorTasks.Contains(owner)) return false;
        if (!tile.SecretDoor)
        {
            world.RemoveSecretDoorEntry(owner, target);
            return false;
        }

        int index = target.Y * world.Width + target.X;
        (int side, int step) = MiningSlots.StartSide(creature.Tile.X - target.X, creature.Tile.Y - target.Y);
        for (int turn = 0; turn < 4; turn++)
        {
            var stand = new TilePos(target.X + SideDx[side], target.Y + SideDy[side]);
            if (!world.Contains(stand) || context.Database.TerrainById(world.Tile(stand).TerrainId).IsSolid
                || !PointReachable(context, creature, CreatureState.CentreOf(stand.X), CreatureState.CentreOf(stand.Y)))
            {
                side = (side + step) & 3;
                continue;
            }
            byte slot = (byte)(MiningSlots.Face(stand, target) * MiningSlots.PerFace + 1);
            (long sx, long sy) = MiningSlots.StancePoint(stand, slot);
            CreatureState? holder = SecretDoorHolder(world, creature, index, slot);
            if (holder is not null)
            {
                // This handler measures the actual holder, including one already working.
                // A refused holder retries the same side; only a blocked or failed walk advances it.
                if (!TaskStealing.CanLoseItsTask(holder)
                    || TaskStealing.DistanceSquared(creature.PosX, creature.PosY, sx, sy)
                        >= TaskStealing.DistanceSquared(holder.PosX, holder.PosY, sx, sy) + 0x32)
                    continue;
                if (!CreatureStateMachine.TrySetState(context.Hooks, holder, ImpStates.TaskSelection)) return false;
                TakeTaskFrom(context, holder, creature, index);
            }
            if (!TakeSecretDoorSlot(context, creature, target, slot))
            {
                side = (side + step) & 3;
                continue;
            }
            return true;
        }
        return false;
    }

    private static bool TakeSecretDoorSlot(SimulationContext context, CreatureState creature, TilePos target, byte slot)
    {
        TilePos stand = (slot / MiningSlots.PerFace) switch
        {
            0 => new TilePos(target.X, target.Y + 1),
            1 => new TilePos(target.X + 1, target.Y),
            2 => new TilePos(target.X - 1, target.Y),
            _ => new TilePos(target.X, target.Y - 1),
        };
        (long sx, long sy) = MiningSlots.StancePoint(stand, slot);
        if (!context.Navigation.TryRouteToStance(creature, sx, sy, MoveOptions.ExactSpot)) return false;
        int index = target.Y * context.World.Width + target.X;
        creature.TargetTileIndex = index;
        creature.SecretDoorTask = true;
        creature.WorkSlot = slot;
        context.World.SecretDoorEntry(creature.OwnerPlayerId, target).Holders[SecretDoorSide(slot)] = creature.Id;
        StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
        BeginTaskWalk(context, creature, ImpStates.SecretDoorApproach);
        creature.LastTaskKind = ImpTaskKind.Excavate;
        creature.LastTaskTileIndex = index;
        context.Emit(SystemName, "job_taken", $"imp={creature.Id} player={creature.OwnerPlayerId} {Describe(context.World, index)} SecretDoor");
        return true;
    }

    private static void UpdateSecretDoor(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!IsSecretDoorState(creature)) return;
        if (StateShadow.IsTravelling(creature))
        {
            if (FollowPathRules.Progress(creature.Body) != Fixed16.One) return;
            StateShadow.Arrive(context, SystemName, creature);
            return;
        }
        WorldState world = context.World;
        ref TileState tile = ref world.TileAtIndex(creature.TargetTileIndex);
        var target = new TilePos(creature.TargetTileIndex % world.Width, creature.TargetTileIndex / world.Width);
        if (creature.Dk2StateId == ImpStates.SecretDoorApproach)
        {
            if (!tile.SecretDoorTasks.Contains(creature.OwnerPlayerId)
                || Math.Abs(creature.Tile.X - target.X) > 1 || Math.Abs(creature.Tile.Y - target.Y) > 1)
            {
                WorkEnded(context, creature);
                GoIdle(creature);
                return;
            }
            int heading = creature.Heading;
            int remaining = Bearing.TurnToward(ref heading,
                Bearing.Of(CreatureState.CentreOf(target.X) - creature.PosX,
                    CreatureState.CentreOf(target.Y) - creature.PosY), definition.TurnRate);
            creature.Heading = heading;
            if (remaining != 0) return;
            if (!StateShadow.TryMove(context, SystemName, creature, ImpStates.SecretDoorWork)
                || world.Tile(creature.Tile).SecretDoor)
            {
                StateShadow.ToIdle(context, SystemName, creature);
                GoIdle(creature);
                return;
            }
            if (ActionAnimationClock.CanPlay(definition, WorkArtSlot))
                ActionAnimationClock.BeginCounted(creature, definition, WorkArtSlot, 1, context.Rng);
            return;
        }
        if (creature.ActionArtSlot != 0)
        {
            if (!creature.ActionHandlerDone)
            {
                // State 38 starts action 1 at the task centre, with the ordinary dig guard.
                if (TileWorkRules.IsDiggableWall(context.Database.TerrainById(tile.TerrainId)))
                {
                    world.QueueWork(new TileWorkOrder(creature.TargetTileIndex, TileWorkKind.Dig,
                        creature.OwnerPlayerId, creature.Id, Strike: TileWorkStrike.DiggableWallGuard));
                    creature.ActionHandlerDone = true;
                }
                else creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
            }
            if (!ActionAnimationClock.CountedActionEnded(creature)) return;
        }
        tile.SecretDoor = false;
        world.RemoveSecretDoorEntry(creature.OwnerPlayerId, target);
        context.Emit(SystemName, "secret_door_revealed",
            $"imp={creature.Id} player={creature.OwnerPlayerId} {Describe(world, creature.TargetTileIndex)}");
        StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
    }
}
