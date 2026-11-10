// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

public sealed class KeeperRoomView
{
    internal KeeperRoomView(WorldState world, GameDatabase database, RoomInstance room)
        : this(room.Definition, room.OwnerPlayerId, room.TileIndices,
            room.MinX, room.MinY, room.MaxX, room.MaxY, FloorSlotsInChainOrder(world, room),
            SlabChain.Order(world, room),
            room.Definition.TypeId == RoomTypeId.Hatchery ? EatingRules.NewestChicken(world, room)?.Id ?? -1 : -1,
            RoomRules.IsRuined(world, room),
            // Only the prisoner step's Torture move asks for wall slots (0x004E8210(1, 1)).
            room.Definition.TypeId == RoomTypeId.TortureChamber
                ? AutoFurnitureRules.WallSlotsOf(world, database, room).Select(s => s.Tile)
                : null) { }

    internal KeeperRoomView(RoomDef definition, PlayerId ownerPlayerId, IEnumerable<int> tiles,
        int minX, int minY, int maxX, int maxY, IEnumerable<int>? floorSlots = null,
        IEnumerable<int>? chainOrder = null, int objectListHead = -1, bool ruined = false,
        IEnumerable<int>? wallSlots = null)
    {
        WallSlots = Array.AsReadOnly((wallSlots ?? []).ToArray());
        Definition = definition;
        Ruined = ruined;
        OwnerPlayerId = ownerPlayerId.Value;
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

    /// <summary>The room's ruin, room <c>+0x9A</c> bit 0 (ADR-0662).</summary>
    public bool Ruined { get; }
    public IReadOnlyList<int> TileIndices { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    /// <summary>The tiles <c>0x004E8210(1, 0)</c> counts and draws among: those the floor-slot test
    /// <c>0x00454110</c> accepts, in the room's slab-chain order (computer-keeper-management.md §2).</summary>
    public IReadOnlyList<int> FloorSlots { get; }

    /// <summary>The tiles the wall-slot test <c>0x00454230</c> accepts, in chain order; captured for a
    /// Torture Chamber only, empty for every other room.</summary>
    public IReadOnlyList<int> WallSlots { get; }

    /// <summary><c>0x004E8210(1, 1)</c>'s candidates: every tile either slot test accepts, counted
    /// once, in chain order.</summary>
    public IEnumerable<int> FurnitureSlots()
    {
        var accepted = new HashSet<int>(FloorSlots);
        accepted.UnionWith(WallSlots);
        foreach (int tile in ChainOrder)
            if (accepted.Contains(tile)) yield return tile;
    }

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
