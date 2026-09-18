// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>DK2's door update (<c>0x004A55F0</c>, doors-and-traps.md): a completed door is open
/// while an eligible creature stands on it or beside it along the passage, and lock mode 3 keeps
/// it shut. Eligible: allied with the door's owner (alliances are unbuilt, so the same owner),
/// not the neutral player, and for lock mode 1 not a worker, for mode 2 only a worker. The
/// three tile chains the original scans are read; that they are the door tile and its two
/// passage neighbours is a HYPOTHESIS, as is ignoring the creature flag at <c>+0x3E6</c> bit 21
/// (Q353). An open door's tile is plain floor in the navigation byte; a closed one carries its
/// owner and lock (<see cref="Navigation.WorldSteering"/>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class DoorSystem : ISimulationSystem
{
    public string Name => "doors";

    public void Execute(SimulationContext context)
    {
        foreach (DoorState door in context.World.Doors)
            door.Open = !door.UnderConstruction && door.LockMode != 3 && AnyEligibleAtHand(context, door);
    }

    private static bool AnyEligibleAtHand(SimulationContext context, DoorState door)
    {
        WorldState world = context.World;
        bool alongX = Passable(context, door.X - 1, door.Y) || Passable(context, door.X + 1, door.Y);
        bool alongY = Passable(context, door.X, door.Y - 1) || Passable(context, door.X, door.Y + 1);

        foreach (CreatureState creature in world.Creatures)
        {
            TilePos at = creature.Tile;
            int dx = at.X - door.X, dy = at.Y - door.Y;
            bool onDoor = dx == 0 && dy == 0;
            bool beside = (alongX && dy == 0 && (dx == 1 || dx == -1)) || (alongY && dx == 0 && (dy == 1 || dy == -1));
            if (!onDoor && !beside) continue;
            if (Eligible(context, door, creature)) return true;
        }

        return false;
    }

    /// <summary>The creature arms of the update (<c>0x004A5E9E</c>–<c>0x004A5F1B</c>). Lock modes 1
    /// and 2 have their arms but no caller sets them in v1.70: the lock command toggles 3 and 0,
    /// the loader sets 3, and an effect handler sets 0 (doors-and-traps.md).</summary>
    public static bool Eligible(SimulationContext context, DoorState door, CreatureState creature)
    {
        if (creature.OwnerPlayerId != door.PlayerId) return false;
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

    private static bool Passable(SimulationContext context, int x, int y) =>
        context.World.Contains(x, y) && !context.Database.TerrainById(context.World.Tile(x, y).TerrainId).IsSolid;
}
