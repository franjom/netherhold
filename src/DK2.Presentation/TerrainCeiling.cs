// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The roof DK2 draws over every open tile in the possession cameras
/// (kwd-terrain-rooms.md, <i>Ceiling: the possession-view roof</i>; <c>0x005A1CA0</c>): a 3 by 3
/// grid of vertices a tile, level with the wall tops beside a wall and rising to just under three
/// tiles over the floor four tiles from any. Heights are the engine's: up is negative z, the floor
/// is -1 and a full-height wall top -2. An adapter draws the piece with the one <c>Ceiling</c>
/// texture, only while a possession camera holds the view, after bending its vertices through the
/// terrain lattice as it bends the walls.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/kwd-terrain-rooms.md")]
public static class TerrainCeiling
{
    /// <summary>The engine texture every ceiling piece is drawn with.</summary>
    public const string TextureName = "Ceiling";

    /// <summary>Vertices a piece: row r along y, column c along x, at (x + c/2, y + r/2), index 3r + c.</summary>
    public const int Vertices = 9;

    /// <summary>Above this the centre and the open edge midpoints are never drawn (-2.5).</summary>
    public const float OpenLimit = -2.5f;

    /// <summary>How far, in tiles on each axis, a tile opening or closing reaches a drawn piece: the
    /// nibble window reaches 4, a piece reads the nibbles of its four corners, and
    /// <see cref="TerrainCeilingField"/>'s normals read half a tile further.</summary>
    public const int ChangeReach = 5;

    /// <summary>How far a change of the terrain lattice at a tile reaches a piece's bent vertices:
    /// a change forgets its 3 by 3 cells, and a piece is bent by the 3 by 3 cells around its tile.</summary>
    public const int BendReach = 2;

    private const int Reach = 4, NoneFound = 16, FarthestCounted = 13;

    private static readonly byte[] TriangleIndices = BuildTriangles();
    private static readonly byte[] NibbleOfDistance = BuildNibbles();

    /// <summary>The 8 triangles of a piece, three vertex indices each, in DK2's order: the diagonal
    /// of each quad alternates with the parity of its row plus its column.</summary>
    public static ReadOnlySpan<byte> Triangles => TriangleIndices;

    /// <summary>The texture coordinate of vertex (r, c): the whole texture once a tile.</summary>
    public static (float U, float V) Uv(int row, int column) => (column * 0.5f, row * 0.5f);

    /// <summary>Marks the tiles a roof treats as wall (<c>0x005091C0</c>): solid terrain, or a tile
    /// holding a pillar object, including the room pillars this remake represents virtually.</summary>
    public static void MarkClosed(WorldState world, GameDatabase database, Span<bool> closed)
    {
        ReadOnlySpan<TileState> tiles = world.Tiles;
        for (int i = 0; i < tiles.Length; i++)
            closed[i] = database.TerrainById(tiles[i].TerrainId).IsSolid;
        foreach (ObjectState thing in world.Objects)
            if (thing.TileLink.IsLinked && !thing.InHand && thing.CarriedByCreatureId < 0
                && database.ObjectById(thing.ObjectTypeId) is { } definition && (definition.Flags & ObjectFlags.Pillar) != 0)
                closed[thing.TileLink.TileIndex] = true;
        RoomPillarLayout.MarkClosed(world, database, closed);
    }

    /// <summary>The tile's height nibble (cell word <c>+2</c> bits 12 to 15): 0 to 15 from the squared
    /// distance between its least corner (x, y) and the nearest closed tile's square, counted up to
    /// 13 (<c>0x0044F880</c>). Tiles outside the map count as closed, as DK2's last pass after a
    /// change counts them (<c>0x00452340</c>); its level-start pass skips them, which differs only
    /// beside a map edge that is not solid. Outside the map the nibble is 0.</summary>
    public static int Nibble(int width, int height, ReadOnlySpan<bool> closed, int x, int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return 0;
        int best = NoneFound;
        for (int b = y - Reach; b < y + Reach && best > 0; b++)
        {
            int dy = b >= y ? b - y : y - b - 1;
            for (int a = x - Reach; a < x + Reach; a++)
            {
                int dx = a >= x ? a - x : x - a - 1;
                int distance = (dx * dx) + (dy * dy);
                if (distance >= best || distance > FarthestCounted) continue;
                if ((uint)a >= (uint)width || (uint)b >= (uint)height || closed[(b * width) + a]) best = distance;
            }
        }
        return NibbleOfDistance[best];
    }

    /// <summary>True when (x, y) gets a roof piece: on the map and not closed.</summary>
    public static bool Has(int width, int height, ReadOnlySpan<bool> closed, int x, int y) =>
        (uint)x < (uint)width && (uint)y < (uint)height && !closed[(y * width) + x];

    /// <summary>The nine heights of tile (x, y)'s piece. Each blends the nibbles of the four tiles
    /// whose least corners are the piece's corners; the centre, and an edge's midpoint when the tile
    /// across that edge is open, are kept at <see cref="OpenLimit"/> or higher up.</summary>
    public static void Heights(int width, int height, ReadOnlySpan<bool> closed, int x, int y, Span<float> z)
    {
        int n00 = Nibble(width, height, closed, x, y), n10 = Nibble(width, height, closed, x + 1, y);
        int n01 = Nibble(width, height, closed, x, y + 1), n11 = Nibble(width, height, closed, x + 1, y + 1);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                int s = ((2 - c) * (2 - r) * n00) + ((2 - c) * r * n01) + (c * (2 - r) * n10) + (c * r * n11);
                z[(3 * r) + c] = ((s * s) - (128 * s) - 4096) / 2048f;
            }
        Limit(ref z[4]);
        if (Has(width, height, closed, x, y - 1)) Limit(ref z[1]);
        if (Has(width, height, closed, x - 1, y)) Limit(ref z[3]);
        if (Has(width, height, closed, x + 1, y)) Limit(ref z[5]);
        if (Has(width, height, closed, x, y + 1)) Limit(ref z[7]);
    }

    private static void Limit(ref float z)
    {
        if (z > OpenLimit) z = OpenLimit;
    }

    private static byte[] BuildTriangles()
    {
        var triangles = new byte[24];
        int at = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                byte v00 = (byte)((3 * i) + j), v01 = (byte)(v00 + 1), v10 = (byte)(v00 + 3), v11 = (byte)(v00 + 4);
                ReadOnlySpan<byte> quad = ((i + j) & 1) == 0
                    ? [v01, v00, v11, v11, v00, v10]
                    : [v01, v00, v10, v11, v01, v10];
                quad.CopyTo(triangles.AsSpan(at));
                at += 6;
            }
        return triangles;
    }

    // min(15, floor(sqrt(16 * d2))) for d2 = 0 to 16.
    private static byte[] BuildNibbles()
    {
        var nibbles = new byte[NoneFound + 1];
        for (int d = 0; d <= NoneFound; d++)
        {
            int root = 0;
            while ((root + 1) * (root + 1) <= 16 * d) root++;
            nibbles[d] = (byte)Math.Min(15, root);
        }
        return nibbles;
    }
}
