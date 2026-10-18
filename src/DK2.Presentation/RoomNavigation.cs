// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public readonly record struct RoomNavigationEntry(TilePos Identity, PlayerId Owner, RoomTypeId Type, TilePos Focus);

public sealed class RoomNavigation
{
    private readonly PlayerId _viewer;
    private TilePos? _last;
    public RoomNavigation(PlayerId viewer) => _viewer = viewer;

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
