// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A room's border cursor (room <c>+0x81</c> phase, <c>+0x87/+0x88</c> tile,
/// <c>+0x89</c> orientation): a point that runs round the room's contour and marks where its
/// flames are born. Orientation 0 runs east along the north edge, 1 south along the east edge,
/// 2 west along the south edge, 3 north along the west edge.</summary>
public record struct RoomBorderCursor
{
    public byte X;
    public byte Y;
    public byte Orientation;
    /// <summary>How far along the tile's edge, 0..4095.</summary>
    public int Phase;

    internal readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Orientation);
        hash.Add(Phase);
    }
}

/// <summary>Room_BorderEffectTick (<c>0x004E7CD0</c>) and its cursor's initializer
/// (<c>0x004E7CA0</c>); docs/research/room-border-effects.md.</summary>
public static class RoomBorderRules
{
    /// <summary>Flame Round Room, which chains Room Flame Data (118) and its sparks.</summary>
    public const ushort FlameEffectId = 20;

    /// <summary>The initializer's phase step (<c>+0x85</c>); no other writer is known.</summary>
    internal const int PhaseStep = 800;

    /// <summary><c>0x006A22F8</c>: per orientation, three neighbour probes and the fallthrough
    /// taken when all three are the room's. Each entry is the probe's offset, the move and the
    /// next orientation.</summary>
    private static ReadOnlySpan<sbyte> Walk =>
    [
        1, 0, 0, 0, 1,    1, -1, 1, 0, 0,    0, -1, 1, -1, 3,    0, 0, 1, 0, 0,
        0, 1, 0, 0, 2,    1, 1, 0, 1, 1,     1, 0, 1, 1, 0,      0, 0, 0, 1, 1,
        -1, 0, 0, 0, 3,   -1, 1, -1, 0, 2,   0, 1, -1, 1, 1,     0, 0, -1, 0, 2,
        0, -1, 0, 0, 0,   -1, -1, 0, -1, 3,  -1, 0, -1, -1, 2,   0, 0, 0, -1, 3,
    ];

    /// <summary>A new room's cursor (<c>0x004E7CA0</c>): its first slab, orientation 3, phase 0.
    /// DK2's first slab is the tile the room was started with; ours is the room's lowest tile
    /// index, as rooms here are derived from their tiles (INFERRED, and the walk soon finds the
    /// contour from any tile).</summary>
    public static RoomBorderCursor Start(WorldState world, RoomInstance room)
    {
        int first = room.TileIndices[0];
        return new RoomBorderCursor
        {
            X = (byte)(first % world.Width), Y = (byte)(first / world.Width), Orientation = 3,
        };
    }

    /// <summary>One room tick of the cursor: the phase moves on, and on crossing a tile's length
    /// the cursor walks the table until a probe finds a tile not the room's (same room and owner,
    /// <c>0x004E7D2D..</c>). Probes off the map find none.</summary>
    public static RoomBorderCursor Advance(RoomBorderCursor cursor, WorldState world, RoomInstance room)
    {
        cursor.Phase += PhaseStep;
        if (cursor.Phase <= 0xFFF) return cursor;
        cursor.Phase -= 0x1000;
        ReadOnlySpan<sbyte> walk = Walk;
        bool edge;
        do
        {
            int probe = 0;
            edge = false;
            for (; probe < 3; probe++)
            {
                int at = (cursor.Orientation * 4 + probe) * 5;
                int x = cursor.X + walk[at], y = cursor.Y + walk[at + 1];
                if (!world.Contains(x, y) || !room.Contains(y * world.Width + x)) { edge = true; break; }
            }
            int entry = (cursor.Orientation * 4 + probe) * 5;
            cursor.X = (byte)(cursor.X + walk[entry + 2]);
            cursor.Y = (byte)(cursor.Y + walk[entry + 3]);
            cursor.Orientation = (byte)walk[entry + 4];
        } while (!edge);
        return cursor;
    }

    /// <summary>Where the cursor's flame is born: the edge it runs along, half a tile's eighth
    /// in from it, the travelled coordinate clamped to 512..3584, at height 4096.</summary>
    public static FpPosition3 FlamePoint(RoomBorderCursor cursor)
    {
        int along = Math.Clamp(cursor.Phase, 0x200, 0xE00);
        int back = Math.Clamp(0xFFF - cursor.Phase, 0x200, 0xE00);
        (int x, int y) = cursor.Orientation switch
        {
            0 => (along, 0x200),
            1 => (0xE00, along),
            2 => (back, 0xE00),
            _ => (0x200, back),
        };
        return new FpPosition3(new((cursor.X << 12) | x), new((cursor.Y << 12) | y), new(Fp32.One));
    }
}
