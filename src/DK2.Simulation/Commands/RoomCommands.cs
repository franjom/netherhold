// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

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
public sealed record BuildRoomCommand(PlayerId PlayerId, RoomTypeId RoomId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public static BuildRoomCommand At(PlayerId playerId, RoomTypeId roomId, params TilePos[] tiles) =>
        new(playerId, roomId, tiles);

    /// <summary>Every tile in a rectangle dragged from (<paramref name="x0"/>, <paramref name="y0"/>)
    /// to (<paramref name="x1"/>, <paramref name="y1"/>), in the order DK2's build wave lays them
    /// (<see cref="SlabChain.Wave"/>): from the corner released, which is where the room's slab
    /// chain starts (room-slab-chain.md §2).</summary>
    public static BuildRoomCommand Rect(PlayerId playerId, RoomTypeId roomId, int x0, int y0, int x1, int y1) =>
        new(playerId, roomId, SlabChain.Wave(x0, y0, x1, y1));

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add((byte)RoomId);
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
public sealed record SellRoomCommand(PlayerId PlayerId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public static SellRoomCommand At(PlayerId playerId, params TilePos[] tiles) =>
        new(playerId, tiles);

    /// <summary>Every tile in a dragged rectangle, in the order DK2's sell wave takes them
    /// (<c>0x004C7160</c>, the same sweep as building's).</summary>
    public static SellRoomCommand Rect(PlayerId playerId, int x0, int y0, int x1, int y1) =>
        new(playerId, SlabChain.Wave(x0, y0, x1, y1));

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
