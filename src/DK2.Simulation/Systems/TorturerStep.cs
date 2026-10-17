// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A torturer in its keeper's Torture Chamber: one turn of states 127, 128, 129 and
/// the walks between them (ADR-0196, <c>state-category-hooks.md</c> §3.3).</summary>
/// <remarks>127 walks in 6 to the first victim no torturer of the keeper's is whipping
/// (resuming 128), else onto a free device (resuming 130), else idles. 128 needs a victim on
/// its target tile, then 1, 129 and action 63; 129 holds while the whip plays, then 1, 128.
/// The whip hurts nothing: it spawns effect <c>0xED</c> at the victim.
/// <para>Ours, stated: she walks onto the victim's tile rather than 0.6 of a tile off, faces
/// it at once, and the whip's action runs before the state each turn; the room capacity
/// test (<c>0x004E53B0</c>) is <c>RoomCapacityRules.HasRoom</c> (ADR-0205). DK2's needs routine can
/// pull her out of 127–129; ours, like every job's, does not.</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class TorturerStep
{
    /// <summary>One original turn. Returns false when the torturer drops to idle, which ends
    /// the job.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            StateShadow.Arrive(context, system, creature);
            if (creature.Dk2StateId == TortureRules.StrappingState) return StrapSelf(context, system, creature);
        }

        return creature.Dk2StateId switch
        {
            TortureRules.TorturerChoosingState => Choose(context, system, creature),
            TortureRules.TorturerFacingState => Face(context, system, creature),
            TortureRules.TorturerWhippingState => Whip(context, system, creature),
            _ => false,
        };
    }

    /// <summary>The keeper's victim nearest the start of the creature list in this chamber
    /// that no torturer of the keeper's is whipping, if any.</summary>
    public static CreatureState? UnattendedVictim(WorldState world, GameDatabase database, CreatureState torturer)
    {
        RoomInstance? room = ChamberUnder(world, database, torturer);
        if (room is null) return null;

        foreach (CreatureState victim in world.Creatures)
        {
            if (victim.Id == torturer.Id || !TortureRules.IsBeingTortured(victim) || HandRules.IsHeld(victim)) continue;
            int at = TileOf(world, victim);
            if (!EatingRules.Contains(room, at) || TortureRules.KeeperOf(victim) != torturer.OwnerPlayerId) continue;
            if (!IsWhipped(world, torturer.OwnerPlayerId, at)) return victim;
        }

        return null;
    }

    private static bool Choose(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        CreatureState? victim = UnattendedVictim(world, database, creature);
        if (victim is not null && context.Navigation.TryRoute(creature, victim.Tile, Navigation.MoveOptions.TortureApproach))
        {
            creature.TortureTargetTile = TileOf(world, victim);
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, TortureRules.TorturerFacingState);
            context.Emit(system, "torturer_to_victim",
                $"creature={creature.Id} victim={victim.Id} at=({victim.Tile.X},{victim.Tile.Y})");
            return true;
        }

        ObjectState? device = FreeDevice(world, database, creature);
        if (device is not null
            && context.Navigation.TryRoute(creature, TilePosOf(world, device.TileIndex), Navigation.MoveOptions.Loose))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, TortureRules.StrappingState);
            creature.TortureVoluntary = true; // 0x00483E65, once the walk has started
            context.Emit(system, "torturer_to_device",
                $"creature={creature.Id} device={device.Id} type={device.ObjectTypeId}");
            return true;
        }

        return false;
    }

    private static bool Face(SimulationContext context, string system, CreatureState creature)
    {
        CreatureState? victim = VictimAt(context.World, creature.TortureTargetTile, creature.Id);
        if (victim is null) return false;

        StateShadow.Move(context, system, creature, CreatureStateMachine.Idle);
        StateShadow.Move(context, system, creature, TortureRules.TorturerWhippingState);
        creature.WhipTurnsLeft = TortureRules.WhipTurns;
        context.Emit(system, "whip_started", $"creature={creature.Id} victim={victim.Id}");
        return true;
    }

    private static bool Whip(SimulationContext context, string system, CreatureState creature)
    {
        CreatureState? victim = VictimAt(context.World, creature.TortureTargetTile, creature.Id);

        // Action 63 first: on its last turn it spawns effect 0xED at the victim, no hurt.
        if (creature.WhipTurnsLeft > 0 && --creature.WhipTurnsLeft == 0 && victim is not null)
            context.Emit(system, "whipped", $"creature={creature.Id} victim={victim.Id} effect=0xED");

        if (victim is not null && creature.WhipTurnsLeft > 0) return true;

        creature.WhipTurnsLeft = 0;
        StateShadow.Move(context, system, creature, CreatureStateMachine.Idle);
        StateShadow.Move(context, system, creature, TortureRules.TorturerFacingState);
        return true;
    }

    private static bool StrapSelf(SimulationContext context, string system, CreatureState creature)
    {
        if (!TortureRules.TryStrapSelf(context.Hooks, creature)) return false;
        context.Emit(system, "torturer_strapped",
            $"creature={creature.Id} at=({creature.Tile.X},{creature.Tile.Y})");
        return true;
    }

    /// <summary>0x004F2180: the nearest device in the chamber no creature is on.</summary>
    private static ObjectState? FreeDevice(WorldState world, GameDatabase database, CreatureState creature)
    {
        RoomInstance? room = ChamberUnder(world, database, creature);
        if (room is null) return null;

        ObjectState? best = null;
        long bestDistance = long.MaxValue;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.InHand || !TortureRules.IsDevice(thing.ObjectTypeId) || !EatingRules.Contains(room, thing.TileIndex)) continue;
            if (TortureRules.FreeDeviceAt(world, database, creature.OwnerPlayerId, thing.TileIndex, creature.Id) is null) continue;

            long distance = EatingRules.DistanceSquared(world, creature.Tile, thing.TileIndex);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = thing;
            }
        }

        return best;
    }

    private static bool IsWhipped(WorldState world, byte keeper, int tileIndex)
    {
        foreach (CreatureState other in world.Creatures)
        {
            if (other.OwnerPlayerId == keeper && other.Dk2StateId == TortureRules.TorturerWhippingState
                && other.TortureTargetTile == tileIndex) return true;
        }

        return false;
    }

    private static CreatureState? VictimAt(WorldState world, int tileIndex, int exceptId)
    {
        if (tileIndex < 0) return null;
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id != exceptId && TortureRules.IsBeingTortured(other) && !HandRules.IsHeld(other)
                && TileOf(world, other) == tileIndex) return other;
        }

        return null;
    }

    private static RoomInstance? ChamberUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        RoomInstance? room = RoomIndex.RoomAt(world, database, TileOf(world, creature));
        return room is not null && room.Definition.TypeId == RoomTypeId.TortureChamber
            && room.OwnerPlayerId == creature.OwnerPlayerId ? room : null;
    }

    private static int TileOf(WorldState world, CreatureState creature) =>
        (creature.Tile.Y * world.Width) + creature.Tile.X;

    private static TilePos TilePosOf(WorldState world, int tileIndex) =>
        new(tileIndex % world.Width, tileIndex / world.Width);
}
