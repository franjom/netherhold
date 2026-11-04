// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>
/// Selects immediate builder calls or the player's queued rectangle wave.
/// </summary>
public enum RoomBuildMode { Direct, PlayerWave }

/// <summary>Builds paid room tiles directly, or queues a player's rectangle from raw press/release corners.</summary>
public sealed record BuildRoomCommand(PlayerId PlayerId, RoomTypeId RoomId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public RoomBuildMode Mode { get; init; }
    public TilePos Press { get; init; }
    public TilePos Release { get; init; }
    /// <summary>Immediate tile-builder input for computer keepers and scenario setup.</summary>
    public static BuildRoomCommand At(PlayerId playerId, RoomTypeId roomId, params TilePos[] tiles) =>
        new(playerId, roomId, tiles);

    /// <summary>Every tile in a rectangle dragged from (<paramref name="x0"/>, <paramref name="y0"/>)
    /// to (<paramref name="x1"/>, <paramref name="y1"/>), in the order DK2's build wave lays them
    /// (<see cref="SlabChain.Wave"/>): from the corner released, which is where the room's slab
    /// chain starts (room-slab-chain.md §2).</summary>
    public static BuildRoomCommand Rect(PlayerId playerId, RoomTypeId roomId, int x0, int y0, int x1, int y1) =>
        new(playerId, roomId, SlabChain.Wave(x0, y0, x1, y1))
        {
            Mode = RoomBuildMode.PlayerWave, Press = new TilePos(x0, y0), Release = new TilePos(x1, y1),
        };

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add((byte)RoomId);
        hash.Add((byte)Mode);
        hash.Add(Press.X);
        hash.Add(Press.Y);
        hash.Add(Release.X);
        hash.Add(Release.Y);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}

/// <summary>Selects an immediate sale of the given tiles or the player's queued rectangle wave.</summary>
public enum RoomSellMode { Direct, PlayerWave }

/// <summary>
/// Sells the rooms on a set of tiles, refunding half of what each cost: at once
/// (<see cref="RoomSellMode.Direct"/>, DK2's one-tile action <c>0x30</c>, scenario setup and log replays) or as
/// the player's queued wave over a rectangle (<see cref="RoomSellMode.PlayerWave"/>, action <c>0x31</c>).
/// <para>Tiles are considered in the order given, because each refund is banked before the next tile goes.
/// No room id: a keeper sells whatever is there.</para>
/// </summary>
public sealed record SellRoomCommand(PlayerId PlayerId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public RoomSellMode Mode { get; init; }
    /// <summary>The rectangle's minimum corner, where a wave ends.</summary>
    public TilePos Min { get; init; }
    /// <summary>The rectangle's maximum corner, where a wave starts.</summary>
    public TilePos Max { get; init; }

    public static SellRoomCommand At(PlayerId playerId, params TilePos[] tiles) =>
        new(playerId, tiles);

    /// <summary>A dragged rectangle, normalised as the Hand's sell release sends it (<c>0x004075E0</c>), so the
    /// wave (<c>0x004C7160</c>) starts at the maximum corner whichever way it was dragged. <see cref="Tiles"/>
    /// lists the rectangle in wave order.</summary>
    public static SellRoomCommand Rect(PlayerId playerId, int x0, int y0, int x1, int y1)
    {
        var min = new TilePos(Math.Min(x0, x1), Math.Min(y0, y1));
        var max = new TilePos(Math.Max(x0, x1), Math.Max(y0, y1));
        return new SellRoomCommand(playerId, SlabChain.Wave(min.X, min.Y, max.X, max.Y))
        {
            Mode = RoomSellMode.PlayerWave, Min = min, Max = max,
        };
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add((byte)Mode);
        hash.Add(Min.X);
        hash.Add(Min.Y);
        hash.Add(Max.X);
        hash.Add(Max.Y);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}
