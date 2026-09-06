// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

/// <summary>
/// The two direction tables the original's keeper uses, and the rule that fixes
/// a keeper's build directions when it is created (docs/KEEPER_AI.md §5.5).
/// </summary>
public static class KeeperDirections
{
    /// <summary>
    /// The site finder's table: 0 north, 1 west, 2 south, 3 east. <c>Dx,Dy</c> is
    /// the walking step; <c>Sx,Sy</c> are the signs the side×side square is laid
    /// out with, so that it is centred on the approach line and extends forward.
    /// </summary>
    public static readonly (int Dx, int Dy, int Sx, int Sy)[] Search =
    [
        (0, -1, 1, -1),
        (-1, 0, -1, 1),
        (0, 1, -1, 1),
        (1, 0, 1, -1),
    ];

    /// <summary>The line walker's table: 0 north, 1 east, 2 south, 3 west — clockwise.</summary>
    public static readonly (int Dx, int Dy)[] Clockwise =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    /// <summary>
    /// Fixed once, at creation: take the heart's offset from the map centre along
    /// its dominant axis. <c>Defend</c> points from the centre toward the heart
    /// (toward the map edge behind it); <c>Open</c> is the opposite. Rooms with id
    /// below 6 are sited in the defend direction, everything else in the open one.
    /// Codes are in <see cref="Search"/> order.
    /// </summary>
    public static (int Defend, int Open) AtCreation(TilePos heart, int mapWidth, int mapHeight)
    {
        int dx = heart.X - (mapWidth / 2);
        int dy = heart.Y - (mapHeight / 2);
        int defend;
        if (Math.Abs(dy) < Math.Abs(dx))
            defend = dx <= 0 ? 1 : 3;
        else
            defend = dy > 0 ? 2 : 0;
        return (defend, (defend + 2) & 3);
    }

    /// <summary>Clockwise heading code of a unit step, for the line walker.</summary>
    public static int HeadingOf(int dx, int dy)
    {
        if (dx > 0) return 1;
        if (dx < 0) return 3;
        return dy > 0 ? 2 : 0;
    }
}
