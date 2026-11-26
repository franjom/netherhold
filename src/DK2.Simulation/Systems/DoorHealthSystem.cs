// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The health half of DK2's door update (<c>0x004A55F0</c>), in its order: the recovery,
/// the owner test, the push off the tile (<see cref="DoorPushRules"/>), and past the barricade
/// return the support test, then the applicable death-effect request. It runs after
/// creatures, shots, objects and traps (<c>0x004B7830</c>), before removal. The open state
/// is <see cref="DoorSystem"/>'s; its timing against movement is unchanged (door-blocking.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-blocking.md")]
public sealed class DoorHealthSystem : ISimulationSystem
{
    public string Name => "door_health";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.Doors.Count == 0)
        {
            return;
        }

        context.Navigation.Refresh();
        IReadOnlyList<DoorState> doors = world.Doors;
        for (int i = doors.Count - 1; i >= 0; i--)
        {
            DoorState door = doors[i];
            if (!context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? definition))
            {
                continue;
            }

            DoorRules.Recover(context, door, definition);
            if (DoorRules.OwnerLost(world, door))
            {
                context.Emit("doors", "door_owner_lost", $"door={door.Id} player={door.PlayerId} tile_owner={world.Tile(door.X, door.Y).OwnerPlayerId}");
            }
            // Step 4, before the barricade's return: a closed built door, a barricade included, pushes off its tile.
            DoorPushRules.PushCreaturesOff(context, door);
            if ((definition.Flags & DoorFlags.Barricade) == 0 && DoorRules.SupportLost(context.Navigation, door))
            {
                context.Emit("doors", "door_unsupported", $"door={door.Id} at={door.X},{door.Y}");
            }

            if (DoorRules.IsDead(door) && !door.UnderConstruction && definition.DeathEffectId != 0)
            {
                int x = CreatureState.CentreOf(door.X), y = CreatureState.CentreOf(door.Y);
                context.RequestEffect("doors", ViewEffectRequest.AtTileCentre(new EffectId(definition.DeathEffectId),
                    door.PlayerId, new TilePos(door.X, door.Y), new Fp32(FloorHeightRules.At(world, context.Database, x, y)),
                    new Dk2Angle(door.Orientation == 0 ? (ushort)0x200 : (ushort)0)));
            }
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
            if (DoorRules.IsDead(doors[i]))
            {
                DoorRules.Remove(context, doors[i]);
            }
            else
            {
                i++;
            }
        }
    }
}
