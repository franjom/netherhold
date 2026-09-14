// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Reconciles room-owned furniture with the current room shapes.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/auto-placed-furniture.md")]
public sealed class AutoFurnitureSystem : ISimulationSystem
{
    public string Name => "AutoFurniture";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        var desired = new List<(byte Owner, int Tile, byte Type)>();
        var unmatched = new HashSet<(byte Owner, int Tile, byte Type)>();
        var roomFurniture = new List<(int TileIndex, byte ObjectTypeId)>();

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            AutoFurnitureRules.DesiredFurniture(world, context.Database, room, roomFurniture);
            foreach ((int tile, byte type) in roomFurniture)
            {
                var key = (room.OwnerPlayerId, tile, type);
                if (unmatched.Add(key)) desired.Add(key);
            }
        }

        var remove = new List<int>();
        foreach (ObjectState thing in world.Objects)
        {
            // Held objects retain their old TileIndex. Shipped furniture cannot
            // reach this state (Q196), but no held object may satisfy a room slot.
            if (thing.InHand) continue;

            var key = (thing.OwnerPlayerId, thing.TileIndex, thing.ObjectTypeId);
            if (unmatched.Remove(key)) continue;
            if (thing.AutoPlaced) remove.Add(thing.Id);
        }

        foreach (int id in remove) world.RemoveObject(id);
        foreach ((byte owner, int tile, byte type) in desired)
        {
            if (!unmatched.Contains((owner, tile, type))) continue;
            // HatcherySystem owns coop creation and its coops_placed event;
            // this pass only retains or removes coops as the room shape changes.
            if (type == HatcheryRules.CoopObjectId) continue;
            if (context.Database.ObjectById(type) is null) continue;
            world.SpawnObject(context.Database, type, owner, tile).AutoPlaced = true;
        }
    }
}
