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
        if (points.Length < 27)
        {
            throw new ArgumentException("27 outline points required.", nameof(points));
        }

        for (int layer = 0; layer < 3; layer++)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    points[layer * 9 + row * 3 + column] = new(tile.X + column * .5f, tile.Y + row * .5f, -2.05f + layer * .5f);
                }
            }
        }
    }

    public static int Indices(TileHoverEdges mask, Span<int> indices)
    {
        if (indices.Length < 48)
        {
            throw new ArgumentException("48 endpoint indices required.", nameof(indices));
        }

        int count = 0;
        foreach (TileHoverSegment segment in Segments)
        {
            if ((mask & segment.Required) == segment.Required)
            { indices[count++] = segment.From; indices[count++] = segment.To; }
        }

        return count;
    }

    /// <summary>The selection cage over <paramref name="area"/> (0x0043CC30, hand-cursor-rendering.md).
    /// Each boundary tile's segments follow its edge mask, the grid lifted
    /// 0.05 around the lattice bend, each segment a ribbon <see cref="Radius"/> wide turned to <paramref name="eye"/>, in
    /// the renderers' frame. The colour is red (pulse, 0, 0), or blue (0, 0, pulse x <see cref="BluePeak"/>) when
    /// <paramref name="blue"/>, at the pulse level read before this call advances it by the time since the last call.</summary>
    /// <param name="lattice">The terrain's bend, or null for the unbent grid.</param>
    /// <param name="milliseconds">The host's clock; the first call, or one at 0, advances the pulse by nothing.</param>
    public static void Build(TileHoverRibbon ribbon, TileRectangle area, bool blue, Vector3 eye, TerrainLattice? lattice,
        long milliseconds)
    {
        if (ribbon is null)
        {
            throw new ArgumentNullException(nameof(ribbon));
        }

        int delta = ribbon.LastMilliseconds == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, milliseconds - ribbon.LastMilliseconds));
        ribbon.LastMilliseconds = milliseconds;
        float level = ribbon.Pulse.Level;
        ribbon.Pulse.Advance(delta);
        ribbon.Clear();
        for (int y = area.Y0; y < area.Y1; y++)
        {
            for (int x = area.X0; x < area.X1; x++)
            {
                TileHoverEdges mask = TileHoverEdges.Lower
                    | (y == area.Y0 ? TileHoverEdges.North : 0) | (y == area.Y1 - 1 ? TileHoverEdges.South : 0)
                    | (x == area.X0 ? TileHoverEdges.West : 0) | (x == area.X1 - 1 ? TileHoverEdges.East : 0);
                if (mask == TileHoverEdges.Lower)
                {
                    continue;
                }

                AddTile(ribbon, new TilePos(x, y), mask, eye, lattice);
            }
        }

        ribbon.Colour = blue ? new Vector3(0, 0, level * BluePeak) : new Vector3(level, 0, 0);
    }

    private static void AddTile(TileHoverRibbon ribbon, TilePos at, TileHoverEdges mask, Vector3 eye, TerrainLattice? lattice)
    {
        Vector3[] grid = ribbon.Grid, points = ribbon.Points;
        Grid(at, grid);
        for (int i = 0; i < grid.Length; i++)
        {
            Vector3 p = grid[i];
            if (lattice != null)
            {
                p.Z += .05f;
                p = lattice.Deform(p, at.X, at.Y);
                p.Z -= .05f;
            }
            points[i] = new Vector3(p.X, -p.Z, -p.Y);
        }
        int count = Indices(mask, ribbon.Endpoints);
        for (int i = 0; i < count; i += 2)
        {
            Vector3 a = points[ribbon.Endpoints[i]], b = points[ribbon.Endpoints[i + 1]];
            Vector3 side = UnitOrZero(Cross(b - a, eye - (a + b) * .5f)) * Radius;
            ribbon.AddQuad(a - side, a + side, b - side, b + side);
        }
    }

    /// <summary>Unity's <c>Vector3.Cross</c>, component by component, so the ribbon keeps Unity's floats.</summary>
    private static Vector3 Cross(Vector3 l, Vector3 r)
    {
        return new(l.Y * r.Z - l.Z * r.Y, l.Z * r.X - l.X * r.Z, l.X * r.Y - l.Y * r.X);
    }

    /// <summary>Unity's <c>Vector3.normalized</c>: zero at a length of 1e-5 or less (an eye on the segment's line).</summary>
    private static Vector3 UnitOrZero(Vector3 v)
    {
        float length = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length > 1e-5f ? new Vector3(v.X / length, v.Y / length, v.Z / length) : Vector3.Zero;
    }
}

/// <summary>The cage ribbon <see cref="TileHoverOutline.Build"/> writes, reused from frame to frame: four vertices a
/// segment (its start's two sides, then its end's), V running 0 to 1 across the ribbon, two triangles (v, v+1, v+2) and
/// (v+2, v+1, v+3), and the additive colour. It keeps the pulse and its clock between builds. 0x0059DD30 builds two
/// crossed ribbons a segment, with UVs read from its vertex layout (HYPOTHESIS). The rendered cage uses
/// one ribbon per segment turned to the eye.</summary>
public sealed class TileHoverRibbon
{
    private Vector3[] _positions = new Vector3[64];
    private Vector2[] _uvs = new Vector2[64];
    private int[] _indices = new int[96];

    public ReadOnlySpan<Vector3> Positions
    {
        get
        {
            return _positions.AsSpan(0, VertexCount);
        }
    }

    public ReadOnlySpan<Vector2> Uvs
    {
        get
        {
            return _uvs.AsSpan(0, VertexCount);
        }
    }

    public ReadOnlySpan<int> Indices
    {
        get
        {
            return _indices.AsSpan(0, IndexCount);
        }
    }

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    /// <summary>The ribbon's additive RGB, 0 to 1.</summary>
    public Vector3 Colour { get; internal set; }

    internal TileHoverPulse Pulse { get; } = new();
    internal long LastMilliseconds { get; set; }
    internal Vector3[] Grid { get; } = new Vector3[27];
    internal Vector3[] Points { get; } = new Vector3[27];
    internal int[] Endpoints { get; } = new int[48];

    internal void Clear() { VertexCount = 0; IndexCount = 0; }

    internal void AddQuad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        if (VertexCount + 4 > _positions.Length)
        {
            Array.Resize(ref _positions, _positions.Length * 2);
            Array.Resize(ref _uvs, _uvs.Length * 2);
            Array.Resize(ref _indices, _indices.Length * 2);
        }
        int v = VertexCount;
        _positions[v] = a0; _positions[v + 1] = a1; _positions[v + 2] = b0; _positions[v + 3] = b1;
        _uvs[v] = new Vector2(0, 0); _uvs[v + 1] = new Vector2(0, 1); _uvs[v + 2] = new Vector2(1, 0); _uvs[v + 3] = new Vector2(1, 1);
        int i = IndexCount;
        _indices[i] = v; _indices[i + 1] = v + 1; _indices[i + 2] = v + 2;
        _indices[i + 3] = v + 2; _indices[i + 4] = v + 1; _indices[i + 5] = v + 3;
        VertexCount += 4; IndexCount += 6;
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
