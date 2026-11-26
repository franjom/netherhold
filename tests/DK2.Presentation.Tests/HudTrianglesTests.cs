// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The triangle-list draw (<see cref="HudDrawKind.Triangles"/>) in a <see cref="HudFrame"/> and its round cut
/// (<see cref="HudTriangles.ClipToCircle"/>), which the minimap's camera footprint goes through (plan step 3b).</summary>
public sealed class HudTrianglesTests
{
    private static readonly HudVertex[] Square =
    [
        new(-10, -10, 0, 0), new(10, -10, 1, 0), new(10, 10, 1, 1),
        new(-10, -10, 0, 0), new(10, 10, 1, 1), new(-10, 10, 0, 1),
    ];

    private static float Area(ReadOnlySpan<HudVertex> triangles)
    {
        float area = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            area += MathF.Abs((triangles[i + 1].X - triangles[i].X) * (triangles[i + 2].Y - triangles[i].Y)
                - (triangles[i + 2].X - triangles[i].X) * (triangles[i + 1].Y - triangles[i].Y)) / 2;
        }

        return area;
    }

    private static HudVertex[] Clip(HudVertex[] triangles, float x, float y, float radius)
    {
        var into = new HudVertex[triangles.Length / 3 * HudTriangles.MaxVerticesPerTriangle];
        return into.AsSpan(0, HudTriangles.ClipToCircle(triangles, x, y, radius, into)).ToArray();
    }

    [Fact]
    public void ATriangleWithEveryCornerInTheCircleIsCopiedUnchanged() =>
        Assert.Equal(Square, Clip(Square, 0, 0, 15));

    [Fact]
    public void ATriangleFarOutsideTheCircleIsDropped() =>
        Assert.Empty(Clip(Square, 100, 100, 5));

    [Fact]
    public void ACoveringSquareLeavesTheCirclesTangentPolygon()
    {
        HudVertex[] cut = Clip(Square, 0, 0, 5);
        float polygon = HudTriangles.CircleSides * 25 * MathF.Tan(MathF.PI / HudTriangles.CircleSides);
        Assert.Equal(polygon, Area(cut), 2);
        Assert.InRange(Area(cut), MathF.PI * 25, MathF.PI * 25 * 1.0011f);
        foreach (HudVertex v in cut)
        {
            Assert.True(MathF.Sqrt(v.X * v.X + v.Y * v.Y) <= 5 / MathF.Cos(MathF.PI / HudTriangles.CircleSides) + 1e-4f);
        }
    }

    [Fact]
    public void ACutEdgeInterpolatesTheImageCoordinates()
    {
        HudVertex[] cut = Clip(Square, 10, 0, 6);
        Assert.NotEmpty(cut);
        Assert.InRange(Area(cut), MathF.PI * 36 / 2 * .99f, MathF.PI * 36 / 2 * 1.01f);
        // U and V are linear in x and y over the square, so every cut vertex keeps that relation.
        foreach (HudVertex v in cut)
        {
            Assert.Equal((v.X + 10) / 20, v.U, 4);
            Assert.Equal((v.Y + 10) / 20, v.V, 4);
        }
    }

    [Fact]
    public void PartTrianglesAreRejectedAndClippingAllocatesNothing()
    {
        var into = new HudVertex[2 * HudTriangles.MaxVerticesPerTriangle];
        Assert.Throws<ArgumentException>(() => HudTriangles.ClipToCircle(Square.AsSpan(0, 4), 0, 0, 5, into));
        HudTriangles.ClipToCircle(Square, 0, 0, 5, into);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            HudTriangles.ClipToCircle(Square, i * .01f, 0, 5, into);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AFrameKeepsATriangleListWithItsBoundsAndOrder()
    {
        var frame = new HudFrame();
        frame.Add(new HudDraw(HudDrawKind.Fill, default, new HudRect(0, 0, 1, 1), HudColor.White));
        frame.AddTriangles("art", Square, HudColor.Grey(9), HudBlend.Additive);
        frame.AddTriangles("art", Square.AsSpan(0, 3), HudColor.White);
        frame.AddTriangles("art", ReadOnlySpan<HudVertex>.Empty, HudColor.White);
        Assert.Equal(3, frame.Count);
        HudDraw list = frame.Draws[1];
        Assert.Equal(new HudDraw(HudDrawKind.Triangles, "art", new HudRect(-10, -10, 20, 20), HudColor.Grey(9), HudBlend.Additive,
            Vertices: new HudVertexRange(0, 6)), list);
        Assert.Equal(Square, frame.Vertices(list).ToArray());
        Assert.Equal(Square.AsSpan(0, 3).ToArray(), frame.Vertices(frame.Draws[2]).ToArray());
        Assert.True(frame.Vertices(frame.Draws[0]).IsEmpty);
        Assert.Throws<ArgumentException>(() => frame.AddTriangles("art", Square.AsSpan(0, 2), HudColor.White));
        frame.Clear();
        frame.AddTriangles("art", Square.AsSpan(3, 3), HudColor.White);
        Assert.Equal(new HudVertexRange(0, 3), frame.Draws[0].Vertices);
    }

    [Fact]
    public void FramesThatDifferOnlyInTheirVerticesAreNotTheSameAndACopyTakesThem()
    {
        var a = new HudFrame();
        var b = new HudFrame();
        a.AddTriangles("art", Square, HudColor.White);
        var moved = (HudVertex[])Square.Clone();
        moved[2] = moved[2] with { X = 11 };
        moved[4] = moved[4] with { X = 11 };
        b.AddTriangles("art", moved, HudColor.White);
        Assert.Equal(a.Draws[0] with { Rect = default }, b.Draws[0] with { Rect = default });
        Assert.False(a.SameAs(b));
        moved[2] = Square[2];
        moved[4] = Square[4] with { V = .5f };
        b.Clear();
        b.AddTriangles("art", moved, HudColor.White);
        Assert.Equal(a.Draws[0], b.Draws[0]);
        Assert.False(a.SameAs(b));
        var copy = new HudFrame();
        for (int i = 0; i < 30; i++)
        {
            copy.AddTriangles("other", Square, HudColor.White);
        }

        copy.CopyFrom(b);
        Assert.True(copy.SameAs(b));
        Assert.Equal(moved, copy.Vertices(copy.Draws[0]).ToArray());
        var small = new HudFrame();
        var big = new HudFrame();
        for (int i = 0; i < 40; i++)
        {
            big.AddTriangles("art", Square, HudColor.White);
        }

        small.CopyFrom(big);
        Assert.True(small.SameAs(big));
    }
    private static float SignedArea(in HudVertex a, in HudVertex b, in HudVertex c) =>
        ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;

    [Fact]
    public void ATriangleWithOnlyItsThirdCornerOutsideIsCut()
    {
        HudVertex[] triangle = [new(0, 0, 0, 0), new(4, 0, 1, 0), new(0, 9, 0, 1)];
        HudVertex[] cut = Clip(triangle, 0, 0, 5);
        Assert.NotEqual(triangle, cut);
        foreach (HudVertex v in cut)
        {
            Assert.True(MathF.Sqrt(v.X * v.X + v.Y * v.Y) <= 5 / MathF.Cos(MathF.PI / HudTriangles.CircleSides) + 1e-4f);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCutTriangleKeepsItsSourcesWinding(bool reversed)
    {
        HudVertex[] source = reversed ? [Square[0], Square[2], Square[1], Square[3], Square[5], Square[4]] : Square;
        float sign = MathF.Sign(SignedArea(source[0], source[1], source[2]));
        HudVertex[] cut = Clip(source, 3, 2, 6);
        Assert.True(cut.Length > 6);
        for (int i = 0; i < cut.Length; i += 3)
        {
            float area = SignedArea(cut[i], cut[i + 1], cut[i + 2]);
            Assert.True(area * sign >= 0, "triangle " + i / 3 + " flipped");
        }
        Assert.Contains(Enumerable.Range(0, cut.Length / 3), t => SignedArea(cut[3 * t], cut[3 * t + 1], cut[3 * t + 2]) * sign > 1e-3f);
    }
}
