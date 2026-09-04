// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Builds a room on a set of tiles, paying for it.
///
/// The player-facing mechanic is CONFIRMED: a keeper picks a room, paints tiles
/// with it, and gold leaves the treasury as the tiles change. What is <b>ours,
/// not DK2's</b>, is the same thing as in <see cref="TagTilesCommand"/> — the
/// shape of the command. The original is a drag over the map; whether a drag
/// that crosses one unbuildable tile abandons the whole rectangle or skips that
/// tile and carries on is a question about the interface, and this command
/// answers it the second way because the simulation has to answer it somehow.
/// Tiles are considered in the order given, which matters when the keeper can
/// afford some of them and not all.
/// </summary>
public sealed record BuildRoomCommand(byte PlayerId, byte RoomId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public static BuildRoomCommand At(byte playerId, byte roomId, params TilePos[] tiles) =>
        new(playerId, roomId, tiles);

    /// <summary>Every tile in a rectangle, in row-major order.</summary>
    public static BuildRoomCommand Rect(byte playerId, byte roomId, int x0, int y0, int x1, int y1)
    {
        var tiles = new List<TilePos>();
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                tiles.Add(new TilePos(x, y));
        return new BuildRoomCommand(playerId, roomId, tiles);
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(RoomId);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}

/// <summary>
/// Sells the rooms on a set of tiles, refunding half of what each cost.
///
/// <para>Same shape as <see cref="BuildRoomCommand"/> and for the same reason —
/// a keeper drags over the map, and a drag that crosses a tile it cannot sell
/// skips that tile rather than abandoning the whole rectangle. That is a
/// question about the interface, and the simulation has to answer it somehow.
/// Tiles are considered in the order given, which matters because each refund is
/// deposited before the next tile is sold.</para>
///
/// <para>The room is not named: a keeper sells whatever is there. Which is why
/// this carries tiles and no room id, where building carries both.</para>
/// </summary>
public sealed record SellRoomCommand(byte PlayerId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public static SellRoomCommand At(byte playerId, params TilePos[] tiles) =>
        new(playerId, tiles);

    /// <summary>Every tile in a rectangle, in row-major order.</summary>
    public static SellRoomCommand Rect(byte playerId, int x0, int y0, int x1, int y1)
    {
        var tiles = new List<TilePos>();
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                tiles.Add(new TilePos(x, y));
        return new SellRoomCommand(playerId, tiles);
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}
