// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

[Flags]
public enum TileHoverEdges { None = 0, North = 1, South = 2, West = 4, East = 8, Lower = 16, Blue = 32,
    All = North | South | West | East | Lower }

public readonly record struct TileHoverSegment(int From, int To, TileHoverEdges Required);

public static class TileHoverOutline
{
    public const float Radius = .03f, BluePeak = .61328125f;
    private static readonly TileHoverSegment[] Segments =
    [
        new(0,1,(TileHoverEdges)1), new(1,2,(TileHoverEdges)1),
        new(18,19,(TileHoverEdges)17), new(19,20,(TileHoverEdges)17),
        new(2,5,(TileHoverEdges)8), new(5,8,(TileHoverEdges)8),
        new(20,23,(TileHoverEdges)24), new(23,26,(TileHoverEdges)24),
        new(6,7,(TileHoverEdges)2), new(7,8,(TileHoverEdges)2),
        new(24,25,(TileHoverEdges)18), new(25,26,(TileHoverEdges)18),
        new(0,3,(TileHoverEdges)4), new(3,6,(TileHoverEdges)4),
        new(18,21,(TileHoverEdges)20), new(21,24,(TileHoverEdges)20),
        new(0,9,(TileHoverEdges)21), new(9,18,(TileHoverEdges)21),
        new(2,11,(TileHoverEdges)25), new(11,20,(TileHoverEdges)25),
        new(8,17,(TileHoverEdges)26), new(17,26,(TileHoverEdges)26),
        new(6,15,(TileHoverEdges)22), new(15,24,(TileHoverEdges)22),
    ];

    public static void Grid(TilePos tile, Span<Vector3> points)
    {
        if (points.Length < 27) throw new ArgumentException("27 outline points required.", nameof(points));
        for (int layer = 0; layer < 3; layer++)
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    points[layer * 9 + row * 3 + column] = new(tile.X + column * .5f, tile.Y + row * .5f, -2.05f + layer * .5f);
    }

    public static int Indices(TileHoverEdges mask, Span<int> indices)
    {
        if (indices.Length < 48) throw new ArgumentException("48 endpoint indices required.", nameof(indices));
        int count = 0;
        foreach (TileHoverSegment segment in Segments)
            if ((mask & segment.Required) == segment.Required)
            { indices[count++] = segment.From; indices[count++] = segment.To; }
        return count;
    }
}

public sealed class TileHoverPulse
{
    public float Level { get; private set; }
    private bool _rising = true;
    public float Advance(int elapsedMilliseconds)
    {
        float delta = Math.Max(0, elapsedMilliseconds) * .004f;
        Level += _rising ? delta : -delta;
        if (Level > 1) { Level = 1; _rising = false; }
        if (Level < 0) { Level = 0; _rising = true; }
        return Level;
    }
}
