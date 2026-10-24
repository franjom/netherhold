// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Navigation;

/// <summary>One portal piece's crystal footprint: the convex hull of its <c>Portal_Gem</c> primitive
/// from the floor to render height 1.85 (0.85 tile of body), in 16.16 units relative to the tile
/// centre. An explicit remake correction (ADR-0527), not a native collision claim; the hulls and how
/// they were measured are in portal-crystal-collision.md.</summary>
public sealed class PortalCrystalShape
{
    private static readonly PortalCrystalShape Piece0 = new([(18160, 23017), (20879, 10046), (31332, -2851),
        (45712, 3670), (53288, 21365), (47953, 31753), (31332, 34250), (20460, 29289)]);
    private static readonly PortalCrystalShape Piece3 = new([(18140, 22047), (35527, 9905), (44814, 6592),
        (51033, 6363), (52191, 14140), (32768, 32768)]);
    private static readonly PortalCrystalShape Piece5 = new([(-43235, -32282), (-41754, -36666), (-26994, -47743),
        (-21404, -43949), (-16384, -32768), (-17878, -21928), (-21784, -10813), (-25493, -10367),
        (-36589, -20715), (-39230, -24949)]);
    private static readonly PortalCrystalShape Piece8 = new([(-43412, -41970), (-40731, -47802), (-34032, -47947),
        (-22642, -32768), (-29294, -23914), (-32768, -20040), (-36209, -19831), (-43182, -25185)]);

    /// <summary>Supporting lines: each edge, a bevel at each vertex and the four axis-aligned sides.
    /// Normals are unit vectors scaled to 1024; <c>D</c> is the hull's extent along each.</summary>
    internal readonly (int X, int Y, long D)[] Planes;

    private PortalCrystalShape((int X, int Y)[] vertices)
    {
        Vertices = vertices.Select(v => new Fixed16Vector(v.X, v.Y)).ToArray();
        Min = new(Vertices.Min(v => v.X), Vertices.Min(v => v.Y));
        Max = new(Vertices.Max(v => v.X), Vertices.Max(v => v.Y));
        var centre = new Fixed16Vector((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);
        var edges = new Fixed16Vector[Vertices.Count];
        for (int i = 0; i < edges.Length; i++)
        {
            Fixed16Vector a = Vertices[i], b = Vertices[(i + 1) % edges.Length];
            var normal = Unit(b.Y - a.Y, a.X - b.X);
            edges[i] = (long)normal.X * (centre.X - a.X) + (long)normal.Y * (centre.Y - a.Y) > 0 ? -normal : normal;
        }
        var normals = new List<Fixed16Vector>(edges);
        for (int i = 0; i < edges.Length; i++)
        {
            Fixed16Vector next = edges[(i + 1) % edges.Length];
            normals.Add(Unit(edges[i].X + next.X, edges[i].Y + next.Y));
        }
        normals.AddRange([new(UnitLength, 0), new(-UnitLength, 0), new(0, UnitLength), new(0, -UnitLength)]);
        Planes = normals.Select(n => (n.X, n.Y, Vertices.Max(v => (long)n.X * v.X + (long)n.Y * v.Y))).ToArray();
    }

    // Rounding a normal's components can leave it up to ~0.07% longer than 1024, so a pad can fall
    // that much short of the radius; harmless. The shipped hulls have no zero-length edge.
    private const int UnitLength = 1024;

    private static Fixed16Vector Unit(long x, long y)
    {
        long squared = x * x + y * y, root = 0;
        for (long bit = 1L << 31; bit != 0; bit >>= 1)
            if ((root + bit) * (root + bit) <= squared) root += bit;
        return new(Scale(x), Scale(y));
        int Scale(long c) => (int)(Math.Sign(c) * ((Math.Abs(c) * UnitLength * 2 + root) / (root * 2)));
    }

    public IReadOnlyList<Fixed16Vector> Vertices { get; }
    public Fixed16Vector Min { get; }
    public Fixed16Vector Max { get; }

    /// <summary>The crystal of piece <paramref name="piece"/> (row-major in the 3x3 classifier), or null.</summary>
    public static PortalCrystalShape? ForPiece(int piece) => piece switch
    {
        0 => Piece0,
        3 => Piece3,
        5 => Piece5,
        8 => Piece8,
        _ => null,
    };
}

/// <summary>A placed crystal: its shape at a tile centre. Every query takes the mover's radius and
/// pushes each supporting line out by it, a bevelled Minkowski sum.</summary>
internal readonly record struct PortalCrystalBounds(Fixed16Vector Origin, PortalCrystalShape Shape)
{
    private const int Margin = 256;
    private const int MaxSpan = 8 << 16;

    public Fixed16Vector Centre => Origin + new Fixed16Vector((Shape.Min.X + Shape.Max.X) / 2, (Shape.Min.Y + Shape.Max.Y) / 2);

    private static int PadOf(Fp32 radius) => Math.Max(0, radius.Raw) << 4;

    private long Side(int plane, Fixed16Vector local, long pad)
    {
        var (x, y, d) = Shape.Planes[plane];
        return (long)x * local.X + (long)y * local.Y - d - pad;
    }

    public bool Contains(Fixed16Vector point, Fp32 radius = default)
    {
        Fixed16Vector local = point - Origin;
        long pad = (long)PadOf(radius) << 10;
        for (int i = 0; i < Shape.Planes.Length; i++)
            if (Side(i, local, pad) >= 0) return false;
        return true;
    }

    public bool Allows(Fixed16Vector from, Fixed16Vector to, Fp32 radius = default)
    {
        if (from == to) return true;
        int pad = PadOf(radius);
        Fixed16Vector a = from - Origin, b = to - Origin;
        if (Math.Max(a.X, b.X) <= Shape.Min.X - pad || Math.Min(a.X, b.X) >= Shape.Max.X + pad ||
            Math.Max(a.Y, b.Y) <= Shape.Min.Y - pad || Math.Min(a.Y, b.Y) >= Shape.Max.Y + pad) return true;
        // Products of side values stay inside 64 bits for spans up to 8 tiles; split longer ones. A
        // midpoint inside the hull then takes the escape rule; no move in play spans 8 tiles.
        if (Math.Abs((long)b.X - a.X) > MaxSpan || Math.Abs((long)b.Y - a.Y) > MaxSpan)
        {
            var middle = new Fixed16Vector((int)(((long)from.X + to.X) >> 1), (int)(((long)from.Y + to.Y) >> 1));
            return Allows(from, middle, radius) && Allows(middle, to, radius);
        }

        long padded = (long)pad << 10;
        if (Contains(from, radius))
        {
            long nearest = long.MinValue;
            for (int i = 0; i < Shape.Planes.Length; i++) nearest = Math.Max(nearest, Side(i, a, padded));
            for (int i = 0; i < Shape.Planes.Length; i++)
                if (Side(i, a, padded) == nearest && Side(i, b, padded) >= nearest) return true;
            return false;
        }

        // Cyrus-Beck: the open interior is crossed when the latest entry precedes the earliest exit.
        long enterNum = 0, enterDen = 1, exitNum = 1, exitDen = 1;
        for (int i = 0; i < Shape.Planes.Length; i++)
        {
            long start = Side(i, a, padded), change = Side(i, b, padded) - start;
            if (change == 0)
            {
                if (start >= 0) return true;
            }
            else if (change < 0)
            {
                if (start * enterDen > enterNum * -change) { enterNum = start; enterDen = -change; }
            }
            else if (-start * exitDen < exitNum * change) { exitNum = -start; exitDen = change; }
            if (enterNum * exitDen >= exitNum * enterDen) return true;
        }
        return false;
    }

    public Fixed16Vector Project(Fixed16Vector from, Fixed16Vector target, Fp32 radius = default)
    {
        if (!Contains(target, radius)) return target;
        Fixed16Vector local = target - Origin;
        long pad = (long)PadOf(radius) << 10;
        Fixed16Vector best = target;
        long distance = long.MaxValue, approach = long.MaxValue;
        for (int i = 0; i < Shape.Planes.Length; i++)
        {
            var (x, y, _) = Shape.Planes[i];
            long move = (-Side(i, local, pad) >> 10) + Margin;
            var point = target + new Fixed16Vector((int)((x * move) >> 10), (int)((y * move) >> 10));
            long next = Squared(point - target), nextApproach = Squared(point - from);
            if (next < distance || (next == distance && nextApproach < approach))
            { distance = next; approach = nextApproach; best = point; }
        }
        return best;
    }

    private static long Squared(Fixed16Vector value) => (long)value.X * value.X + (long)value.Y * value.Y;

    public static List<PortalCrystalBounds> Build(WorldState world, GameDatabase database)
    {
        var result = new List<PortalCrystalBounds>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId is not (RoomTypeId.Portal or RoomTypeId.HeroPortal)) continue;
            var tiles = new HashSet<int>(room.TileIndices);
            foreach (int tile in room.TileIndices)
            {
                int x = tile % world.Width, y = tile / world.Width;
                if (x == 0 || y == 0 || x == world.Width - 1 || y == world.Height - 1) continue;
                int cx = !tiles.Contains(tile - 1) ? 0 : tiles.Contains(tile + 1) ? 1 : 2;
                int cy = !tiles.Contains(tile - world.Width) ? 0 : tiles.Contains(tile + world.Width) ? 1 : 2;
                if (PortalCrystalShape.ForPiece(cx + 3 * cy) is not { } shape) continue;
                result.Add(new(new Fixed16Vector(CreatureState.CentreOf(x) << 4, CreatureState.CentreOf(y) << 4), shape));
            }
        }
        return result;
    }
}
