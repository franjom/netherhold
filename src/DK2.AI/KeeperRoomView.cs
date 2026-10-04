// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

public sealed class KeeperRoomView
{
    internal KeeperRoomView(WorldState world, RoomInstance room)
        : this(room.Definition, room.OwnerPlayerId, room.TileIndices,
            room.MinX, room.MinY, room.MaxX, room.MaxY, FloorSlotsInChainOrder(world, room),
            SlabChain.Order(world, room),
            room.Definition.TypeId == RoomTypeId.Hatchery ? EatingRules.NewestChicken(world, room)?.Id ?? -1 : -1) { }

    internal KeeperRoomView(RoomDef definition, byte ownerPlayerId, IEnumerable<int> tiles,
        int minX, int minY, int maxX, int maxY, IEnumerable<int>? floorSlots = null,
        IEnumerable<int>? chainOrder = null, int objectListHead = -1)
    {
        Definition = definition;
        OwnerPlayerId = ownerPlayerId;
        TileIndices = Array.AsReadOnly(tiles.ToArray());
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        FloorSlots = Array.AsReadOnly((floorSlots ?? []).ToArray());
        ChainOrder = chainOrder is null ? TileIndices : Array.AsReadOnly(chainOrder.ToArray());
        ObjectListHead = objectListHead;
    }

    public RoomDef Definition { get; }
    public byte OwnerPlayerId { get; }
    public IReadOnlyList<int> TileIndices { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    /// <summary>The tiles <c>0x004E8210(1, 0)</c> counts and draws among: those the floor-slot test
    /// <c>0x00454110</c> accepts, in the room's slab-chain order (computer-keeper-management.md §2).</summary>
    public IReadOnlyList<int> FloorSlots { get; }

    /// <summary>Every tile in slab-chain order, <c>FirstSlab</c> first: the walk the expansion's
    /// corner search (<c>0x004FBE00</c>) makes, whose first extreme wins a tie.</summary>
    public IReadOnlyList<int> ChainOrder { get; }

    /// <summary>A Hatchery's object-list head (<c>+0x0C</c>), the chicken make-happy lifts
    /// (<see cref="EatingRules.NewestChicken"/>); -1 with none and for every other room.</summary>
    public int ObjectListHead { get; }

    private static IEnumerable<int> FloorSlotsInChainOrder(WorldState world, RoomInstance room)
    {
        HashSet<int> accepted = AutoFurnitureRules.FloorSlotSet(world, room);
        return SlabChain.Order(world, room).Where(accepted.Contains);
    }
}
