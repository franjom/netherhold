// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>DK2's door update (<c>0x004A55F0</c>, doors-and-traps.md), in its own order: the
/// owner's lock command, then the lock-pick countdown, then the open state. A completed door is
/// open while a picked lock is still running, shut while its lock mode is 3, and otherwise open
/// while an eligible creature stands on one of three tiles — its own and the two beside it along
/// the passage. Eligible: allied with the door's owner, not the neutral player, not dying, and
/// permitted by the lock mode. An open door's tile is plain floor in the navigation byte; a
/// closed one carries its owner and lock (<see cref="Navigation.WorldSteering"/>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class DoorSystem : ISimulationSystem
{
    public string Name => "doors";

    public void Execute(SimulationContext context)
    {
        Lock(context);

        foreach (DoorState door in context.World.Doors)
        {
            // The countdown runs before anything reads it and whatever the door is doing:
            // the original decrements at 0x004A56A1, above both the construction bypass and
            // the open decision.
            if (door.LockPickTicks > 0) door.LockPickTicks--;
            if (door.UnderConstruction) continue;
            if (context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)
                && definition.Flags.HasFlag(DoorFlags.Barricade)) continue;

            door.Open = door.LockPickTicks > 0
                || (door.LockMode != 3 && AnyEligibleAtHand(context, door));
        }
    }

    /// <summary>The owner's lock command (<c>0x00513800</c>): a door of this player's, toggled
    /// between locked and unlocked. A command naming a tile with no door of theirs does nothing,
    /// which is the original's three refusals — no live thing, the wrong type, another
    /// owner.</summary>
    private static void Lock(SimulationContext context)
    {
        foreach (LockDoorCommand request in context.Commands<LockDoorCommand>())
        {
            foreach (DoorState door in context.World.Doors)
            {
                if (door.X != request.Tile.X || door.Y != request.Tile.Y) continue;
                if (door.PlayerId != request.PlayerId) continue;
                door.LockMode = door.LockMode == 0 ? (byte)3 : (byte)0;
            }
        }
    }

    private static bool AnyEligibleAtHand(SimulationContext context, DoorState door)
    {
        WorldState world = context.World;
        foreach (CreatureState creature in world.Creatures)
        {
            TilePos at = creature.Tile;
            int dx = at.X - door.X, dy = at.Y - door.Y;
            bool onDoor = dx == 0 && dy == 0;
            bool beside = door.Orientation == 0
                ? dy == 0 && (dx == 1 || dx == -1)
                : dx == 0 && (dy == 1 || dy == -1);
            if (!onDoor && !beside) continue;
            if (Eligible(context, door, creature)) return true;
        }

        return false;
    }

    /// <summary>The creature arms of the update (<c>0x004A5E9E</c>–<c>0x004A5F1B</c> and its two
    /// copies). The read lock writers use 0 and 3: the lock command toggles them, the loader
    /// sets 3, and an effect handler sets 0. Modes 1 and 2 retain their read eligibility arms.</summary>
    /// <remarks>DK2's third test is the creature flag <c>+0x3E6</c> bit 21, <c>CreatureDying</c>.
    /// Ours is <see cref="CreatureDeathRules.IsDown"/>, which is that state plus the tick window
    /// in which a creature is out of health and has not been booked as collapsed yet — the guard
    /// every other system here uses, and a wider door than the original's by at most one game
    /// second.</remarks>
    public static bool Eligible(SimulationContext context, DoorState door, CreatureState creature)
    {
        if (!context.World.AreAllied(door.PlayerId, creature.OwnerPlayerId)) return false;
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;

        bool worker = context.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
        return door.LockMode switch
        {
            0 => true,
            1 => !worker,
            2 => worker,
            _ => false,
        };
    }

}
