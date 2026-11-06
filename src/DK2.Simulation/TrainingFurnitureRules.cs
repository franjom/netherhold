// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Training Room's neighbor-work furniture replacement (training-target-rebuild.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/training-target-rebuild.md")]
internal static class TrainingFurnitureRules
{
    private const byte TargetObjectId = 29;

    internal static void Rebuild(WorldState world, GameDatabase database, RoomInstance room, SimRandom rng)
    {
        for (int i = world.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = world.Objects[i];
            if (thing.AutoPlaced && !thing.InHand && room.Contains(thing.TileIndex)) world.RemoveObject(thing.Id);
        }

        var desired = new List<(int TileIndex, byte ObjectTypeId)>();
        AutoFurnitureRules.DesiredFurniture(world, database, room, desired);
        HashSet<int> floor = AutoFurnitureRules.FloorSlotSet(world, room);
        var order = new Dictionary<int, int>();
        int ordinal = 0;
        foreach (int tile in SlabChain.Order(world, room)) order[tile] = ordinal++;
        desired.Sort((a, b) =>
        {
            bool aFloor = floor.Contains(a.TileIndex), bFloor = floor.Contains(b.TileIndex);
            return aFloor != bFloor ? (aFloor ? -1 : 1) : order[a.TileIndex].CompareTo(order[b.TileIndex]);
        });
        foreach ((int tile, byte type) in desired)
        {
            if (database.ObjectById(type) is null) continue;
            world.SpawnObject(database, type, room.OwnerPlayerId, tile).AutoPlaced = true;
            // Native writes this to animation +D4/+D8; object animation phases remain unmirrored.
            if (type == TargetObjectId && !floor.Contains(tile)) rng.NextInt(4096);
        }
    }
}
