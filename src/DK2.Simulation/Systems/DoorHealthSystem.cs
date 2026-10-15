// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The health half of DK2's door update (<c>0x004A55F0</c>), in its order: the recovery,
/// the owner test, and past the barricade return the support test. It runs after the creatures
/// have moved and swung, as DK2's thing pass updates the creature list before the doors
/// (<c>0x004B7830</c>); the open state is <see cref="DoorSystem"/>'s, whose timing against movement
/// is unchanged by where this half runs (door-blocking.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-blocking.md")]
public sealed class DoorHealthSystem : ISimulationSystem
{
    public string Name => "door_health";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.Doors.Count == 0) return;
        context.Navigation.Refresh();
        foreach (DoorState door in world.Doors)
        {
            if (!context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)) continue;
            DoorRules.Recover(context, door, definition);
            if (DoorRules.OwnerLost(world, door))
                context.Emit("doors", "door_owner_lost", $"door={door.Id} player={door.PlayerId} tile_owner={world.Tile(door.X, door.Y).OwnerPlayerId}");
            if (definition.Flags.HasFlag(DoorFlags.Barricade)) continue;
            if (DoorRules.SupportLost(context.Navigation, door))
                context.Emit("doors", "door_unsupported", $"door={door.Id} at={door.X},{door.Y}");
        }
    }
}

/// <summary>The second walk of DK2's thing pass (<c>0x004B7830</c>): after every thing has updated,
/// each whose death query says dead is removed, this turn — here after the shots, which are things
/// too.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-blocking.md")]
public sealed class DoorRemovalSystem : ISimulationSystem
{
    public string Name => "door_removal";

    public void Execute(SimulationContext context)
    {
        context.World.PruneNavStamps(); // every turn, as the doors' second walk is; the door's and the Heart's stamps alike
        IReadOnlyList<DoorState> doors = context.World.Doors;
        for (int i = 0; i < doors.Count;)
        {
            if (DoorRules.IsDead(doors[i])) DoorRules.Remove(context, doors[i]);
            else i++;
        }
    }
}
