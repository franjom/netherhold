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
        var roomFurniture = new List<(int TileIndex, byte ObjectTypeId)>();

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            AutoFurnitureRules.DesiredFurniture(world, context.Database, room, roomFurniture);
            foreach ((int tile, byte type) in roomFurniture)
                desired.Add((room.OwnerPlayerId, tile, type));
        }

        var unmatched = new List<(byte Owner, int Tile, byte Type)>(desired);
        var remove = new List<int>();
        foreach (ObjectState thing in world.Objects)
        {
            if (!thing.AutoPlaced) continue;
            int match = unmatched.FindIndex(d =>
                d.Owner == thing.OwnerPlayerId && d.Tile == thing.TileIndex && d.Type == thing.ObjectTypeId);
            if (match >= 0) unmatched.RemoveAt(match);
            else remove.Add(thing.Id);
        }

        foreach (int id in remove) world.RemoveObject(id);
        foreach ((byte owner, int tile, byte type) in unmatched)
            world.SpawnObject(context.Database, type, owner, tile).AutoPlaced = true;
    }
}
