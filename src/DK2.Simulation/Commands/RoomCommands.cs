// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Builds a room on a set of tiles, paying for it.
///
/// <para>CONFIRMED: a keeper picks a room, paints tiles with it, and gold leaves
/// the treasury as the tiles change.</para>
///
/// <para>Ours, not DK2's, as in <see cref="TagTilesCommand"/>: the shape of the
/// command. A drag that crosses an unbuildable tile skips that tile rather than
/// abandoning the rectangle — an interface question the simulation has to answer
/// somehow. Tiles are considered in the order given, which matters when the
/// keeper can afford some and not all.</para>
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
/// <para>Same shape as <see cref="BuildRoomCommand"/>, and tiles are considered
/// in the order given — which matters because each refund is deposited before
/// the next tile is sold.</para>
///
/// <para>No room id, because a keeper sells whatever is there.</para>
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
