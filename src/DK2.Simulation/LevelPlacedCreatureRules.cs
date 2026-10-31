// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hand-drop-landing.md")]
public static class LevelPlacedCreatureRules
{
    public const byte PlacedAsDropped = 0x40;

    internal static void Land(SimulationContext context)
    {
        if (context.Tick.Value != 0) return;
        WorldState world = context.World;
        foreach (CreatureState creature in world.Creatures)
        {
            if ((creature.Flags & PlacedAsDropped) == 0 || creature.Dk2StateId != DropLandingRules.LandingState
                || HandRules.IsHeld(creature) || !world.Contains(creature.Tile)) continue;
            int here = creature.Tile.Y * world.Width + creature.Tile.X;
            RoomInstance? room = RoomIndex.RoomAt(world, context.Database, here);
            CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle);
            if (CreatureDeathRules.IsDown(creature)) continue;
            creature.DroppedOnARoom = room is not null;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            bool enemyRoom = room is not null && room.OwnerPlayerId != creature.OwnerPlayerId
                && room.OwnerPlayerId != PlayerIds.Neutral;
            bool keeperWorker = PlayerIds.IsKeeper(creature.OwnerPlayerId)
                && (definition.Flags & CreatureFlags.IsWorker) != 0;
            if ((enemyRoom || !keeperWorker) && room?.Definition.TypeId == RoomTypeId.Prison
                && AutoFurnitureRules.IsFloorSlot(world, room, here))
            {
                creature.PrisonOwnerPlayerId = room.OwnerPlayerId;
                CreatureStateMachine.TrySetState(context.Hooks, creature, PrisonRules.AdmittedState);
                CreatureStateMachine.TrySetState(context.Hooks, creature, PrisonRules.DecidingState);
            }
            else if (room?.Definition.TypeId == RoomTypeId.TortureChamber
                && (room.OwnerPlayerId == creature.OwnerPlayerId
                    ? TortureRules.TryStrapOwn(context.Hooks, creature, here)
                    : TortureRules.TryStrap(context.Hooks, creature, room.OwnerPlayerId, here))) continue;
            else
            {
                if (!keeperWorker && room?.Definition.TypeId == RoomTypeId.Portal
                    && LeavingRules.IsDropCentre(world, context.Database, creature.Tile, creature.OwnerPlayerId))
                {
                    DropLandingRules.AfterGettingUp(context, "LevelStart", creature, definition);
                    continue;
                }
                if (keeperWorker && definition.CreatureId != 1
                    && LeavingRules.IsDropCentre(world, context.Database, creature.Tile, creature.OwnerPlayerId))
                {
                    StateShadow.Move(context, "LevelStart", creature, LeavingRules.PortalDropState);
                    continue;
                }
                if ((definition.Flags & CreatureFlags.IsWorker) != 0)
                    CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.AfterDrop);
                else if (!Combat.StatusEffectRules.StunOnLanding(context, creature, definition))
                    DropLandingRules.AfterGettingUp(context, "LevelStart", creature, definition);
            }
        }
    }
}
