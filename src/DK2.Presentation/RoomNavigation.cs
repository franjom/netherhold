// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public readonly record struct RoomNavigationEntry(TilePos Identity, PlayerId Owner, RoomTypeId Type, TilePos Focus);

public sealed class RoomNavigation
{
    private readonly PlayerId _viewer;
    private TilePos? _last;
    public RoomNavigation(PlayerId viewer) => _viewer = viewer;

    /// <summary>Every room of the map in <see cref="RoomIndex.Build"/>'s order, into <paramref name="into"/> (cleared first):
    /// its first tile as its identity, its owner, its type and its centre field as the focus. A room whose definition has
    /// no type is left out. Moved out of Unity's DungeonRunner.NavigateRoom (migration plan step 2c).</summary>
    public static void Entries(WorldState world, GameDatabase database, List<RoomNavigationEntry> into)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (into is null) throw new ArgumentNullException(nameof(into));
        into.Clear();
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, database);
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomInstance room = rooms[i];
            if (room.Definition.TypeId is not RoomTypeId roomType) continue;
            TilePos identity = world.TilePosition(room.TileIndices[0]);
            into.Add(new RoomNavigationEntry(identity, new PlayerId(room.OwnerPlayerId), roomType,
                new TilePos(room.CentreFieldX, room.CentreFieldY)));
        }
    }

    public TilePos? Next(IReadOnlyList<RoomNavigationEntry> rooms, RoomTypeId type)
    {
        int previous = -1, first = -1;
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomNavigationEntry room = rooms[i];
            if (room.Owner != _viewer || room.Type != type) continue;
            if (first < 0) first = i;
            if (room.Identity == _last) previous = i;
        }
        int chosen = first;
        if (previous >= 0)
            for (int i = previous + 1; i < rooms.Count; i++)
                if (rooms[i].Owner == _viewer && rooms[i].Type == type) { chosen = i; break; }
        if (chosen < 0) return null;
        _last = rooms[chosen].Identity;
        return rooms[chosen].Focus;
    }
}
