// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A creature that steps into an enemy's Torture Chamber holding one of its allies
/// frees it: the contact test <c>0x004E8660</c> and state 134 (ADR-0201, <see cref="RescueRules"/>).
/// In a Prison it breaks the door open in 122 (ADR-0229, <see cref="JailBreakRules"/>).</summary>
/// <remarks>Ours, stated: the contact test runs for an idle creature or one holding a state
/// id, not for one still on our own activities (combat, leaving, fleeing), as the busy
/// creature is refused elsewhere; the 134 walk goes onto the victim's tile. The freed victim
/// is let go as a captive dropped off a device is: its own side's, idle.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public sealed class RescueSystem : ISimulationSystem
{
    private const string SystemName = "Rescue";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        for (int i = 0; i < world.Creatures.Count; i++)
        {
            CreatureState creature = world.Creatures[i];
            if (creature.Possessed) continue;
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsDown(creature)) continue;

            int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
            if (creature.Activity == CreatureActivity.Rescuing)
            {
                creature.ContactTileIndex = here;
                if (IsBreakingOut(creature)) BreakOut(context, creature);
                else Step(context, creature, here);
                continue;
            }

            if (here == creature.ContactTileIndex) continue;
            creature.ContactTileIndex = here;
            if (MayStart(creature) && RescueRules.StateFor(world, database, creature, here) is int state) Start(context, creature, state);
        }
    }

    private static bool MayStart(CreatureState creature) =>
        creature.Activity is CreatureActivity.Idle or CreatureActivity.AtWork or CreatureActivity.GoingToJob;

    private static void Start(SimulationContext context, CreatureState creature, int state)
    {
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, state)) return;
        NeedRules.EndJob(creature);

        creature.StopMoving();
        creature.RescueTurnsLeft = RescueRules.WaitTurns;
        context.Emit(SystemName, "rescue_started",
            $"creature={creature.Id} owner={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y})");
    }

    private static void Step(SimulationContext context, CreatureState creature, int here)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return;
            creature.StopMoving();
            if (!CreatureStateMachine.Arrive(context.Hooks, creature)) { Stop(context, creature); return; }
            creature.RescueTurnsLeft = RescueRules.WaitTurns;
        }

        if (creature.RescueTurnsLeft > 0 && --creature.RescueTurnsLeft > 0) return;

        RoomInstance? room = RoomIndex.RoomAt(world, database, here);
        if (room is null || room.Definition.TypeId != RoomTypeId.TortureChamber) { Stop(context, creature); return; }

        CreatureState? nearest = null;
        long best = long.MaxValue;
        foreach (CreatureState victim in RescueRules.AlliedVictims(world, database, creature, room))
        {
            if (victim.Tile == creature.Tile)
            {
                Free(context, creature, victim);
                return;
            }

            long distance = EatingRules.DistanceSquared(world, creature.Tile, (victim.Tile.Y * world.Width) + victim.Tile.X);
            if (distance < best) { best = distance; nearest = victim; }
        }

        if (nearest is not null && context.Navigation.TryRoute(creature, nearest.Tile, Navigation.MoveOptions.ExactSpot))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, RescueRules.RescuingState);
            context.Emit(SystemName, "rescuer_to_victim",
                $"creature={creature.Id} victim={nearest.Id} at=({nearest.Tile.X},{nearest.Tile.Y})");
            return;
        }

        Stop(context, creature);
    }

    private static bool IsBreakingOut(CreatureState creature) =>
        creature.Dk2StateId == JailBreakRules.BreakingOutState
        || (StateShadow.IsTravelling(creature) && creature.UtilityNewState == JailBreakRules.BreakingOutState);

    /// <summary>122, a turn: the walk to the door, then <see cref="JailBreakRules.BreakOut"/>. A state
    /// entered this turn runs from the next.</summary>
    private static void BreakOut(SimulationContext context, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return;
            creature.StopMoving();
            if (!CreatureStateMachine.Arrive(context.Hooks, creature)) Stop(context, creature);
            return;
        }
        if (!JailBreakRules.BreakOut(context, SystemName, creature)) Stop(context, creature);
        else if (creature.Dk2StateId == ImpStates.Wandering) creature.ClearJob();
    }

    /// <summary>134 moves the victim to 133, which drops it to idle off the device; 134 then
    /// returns 0 and the rescuer is idle too.</summary>
    private static void Free(SimulationContext context, CreatureState rescuer, CreatureState victim)
    {
        CreatureStateMachine.TrySetState(context.Hooks, victim, RescueRules.FreedState);
        TortureRules.Unstrap(context.Hooks, victim);
        PrisonRules.Release(context.Hooks, victim);
        context.Emit(SystemName, "captive_rescued",
            $"creature={rescuer.Id} victim={victim.Id} owner={victim.OwnerPlayerId} " +
            $"at=({victim.Tile.X},{victim.Tile.Y})");
        Stop(context, rescuer);
    }

    private static void Stop(SimulationContext context, CreatureState creature)
    {
        creature.StopMoving();
        creature.RescueTurnsLeft = 0;
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle);
        creature.ClearJob();
    }
}
