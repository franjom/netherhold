// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;

namespace DK2.Presentation.Tests;

/// <summary><see cref="TabletMesh"/> and <see cref="TabletDrawList"/>, the reward tablet's geometry moved out of Unity's
/// DungeonRunner.Finale: the sparkle points, the front's winding, the projection and the back-face cull.</summary>
public sealed class TabletDrawListTests
{
    private static KmfVertex V(float x, float y, float u = 0, float v = 0) => new(new Vector3(x, y, 0), Vector3.UnitZ, new Vector2(u, v));

    private static KmfMaterial Material(string name, params string[] textures) => new(name, textures, 0, 1, 0, "");

    /// <summary>A front with the item frames (one face toward the viewer, one away), a one-texture back, an untextured
    /// part and a back whose only face is turned away.</summary>
    private static TabletMesh Mesh(int extraPointCount = 0)
    {
        KmfVertex[] square = [V(0, 0, 0, 0), V(1, 0, 1, 0), V(0, 1, 0, 1)];
        var model = new KmfModel(2, KmfKind.Mesh,
            [Material("front", "Frame0", "Frame1"), Material("back", "Back"), Material("bare")],
            new KmfMesh("Tablet", Vector3.Zero, 1,
            [
                new KmfPrimitive(0, 1, square, [new byte[] { 0, 1, 2, 0, 2, 1 }]),
                new KmfPrimitive(1, 1, square, [new byte[] { 0, 1, 2, 0 }]),
                new KmfPrimitive(2, 1, square, [new byte[] { 0, 1, 2 }]),
                new KmfPrimitive(1, 1, square, [new byte[] { 0, 2, 1 }]),
            ]))
        {
            Geometry = [new Vector3(1, 2, 3), new Vector3(4, 5, 6)],
            ControlPoints =
            [
                new KmfControlPoint(1, 0), new KmfControlPoint(7, 0), new KmfControlPoint(0, 0),
                .. Enumerable.Repeat(new KmfControlPoint(0, 0), extraPointCount),
            ],
        };
        return new TabletMesh(model);
    }

    private static RewardTablet Tablet()
    {
        var tablet = new RewardTablet();
        tablet.Start(x: 10, y: 20, scale: 0.5f, targetX: 10, targetY: 20, startsAt: 0);
        return tablet;
    }

    [Fact]
    public void TheMeshKeepsItsControlPointsInRangeAndTheFrontsWinding()
    {
        TabletMesh mesh = Mesh();
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { new Vector3(4, 5, 6), new Vector3(1, 2, 3) }, mesh.Points);
#pragma warning restore IDE0300
        Assert.Equal(1, mesh.FrontWinding);
        Assert.Equal(6 + 3 + 3 + 3, mesh.VertexCapacity);
    }

    [Fact]
    public void FacesTurnedAwayAreCulledAndEveryTexturedPartKeepsItsBatch()
    {
        var draw = new TabletDrawList(Mesh());
        RewardTablet tablet = Tablet();
        Assert.Equal(0, tablet.Angle);
        draw.Build(tablet, itemId: 0, originX: 100, originY: 50, screenHeight: 768);

        Assert.Equal(new[]
        {
            new TabletBatch("Frame0", 0, 3), new TabletBatch("Back", 3, 3), new TabletBatch("Back", 6, 0),
        }, draw.Batches.ToArray());
        // At rest the turn is none: a point lands at the centre plus point × scale × 4096 reference units.
        float centreX = 100 + 10, centreY = 50 + 20, step = 0.5f * RewardTablet.PixelsPerUnit;
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[]
        {
#pragma warning restore IDE0300
            new TabletVertex(centreX, centreY, 0, 0), new TabletVertex(centreX + step, centreY, 1, 0),
            new TabletVertex(centreX, centreY + step, 0, 1),
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        }, draw.Vertices[..3].ToArray());
#pragma warning restore IDE0305
    }

    [Fact]
    public void TheItemPicksTheFrontsFrameAndTheScreenHeightSetsTheUnit()
    {
        var draw = new TabletDrawList(Mesh());
        RewardTablet tablet = Tablet();
        draw.Build(tablet, itemId: 40, originX: 0, originY: 0, screenHeight: 1536);

        Assert.Equal("Frame" + RewardTablet.TextureFrame(2, 40), draw.Batches[0].Texture);
        Assert.Equal(new TabletVertex(20 + 2 * 0.5f * RewardTablet.PixelsPerUnit, 40, 1, 0), draw.Vertices[1]);
    }

    [Fact]
    public void SparklesAreSquaresAboutTheOriginAsWideAsTheirFramesLeft()
    {
        TabletMesh mesh = Mesh();
        var draw = new TabletDrawList(mesh);
        RewardTablet tablet = Tablet();
        tablet.Spawn(0, mesh.Points, new Random(1));
        Assert.NotEqual(0, tablet.Sparkles.Length);

        draw.Build(tablet, itemId: 0, originX: 30, originY: 60, screenHeight: 1536);
        Assert.Equal(tablet.Sparkles.Length, draw.Sparkles.Length);
        for (int i = 0; i < draw.Sparkles.Length; i++)
        {
            RewardTablet.Sparkle s = tablet.Sparkles[i];
            Assert.Equal(new TabletSparkleQuad(30 + s.X * 2, 60 + s.Y * 2, s.Life * 2f / 2), draw.Sparkles[i]);
        }

        // A rebuild replaces the list rather than adding to it.
        draw.Build(new RewardTablet(), itemId: 0, originX: 0, originY: 0, screenHeight: 768);
        Assert.Equal(0, draw.Sparkles.Length);
        Assert.Equal(0, draw.Vertices.Length);
    }

    [Fact]
    public void AFullMeshSparkleSequenceDoesNotGrowItsBuffersWhileDrawing()
    {
        static int PlayFrames(WinScreen screen, TabletDrawList draw)
        {
            int maxSparkles = 0;
            screen.Start(0);
            for (int frame = 0; frame < RewardTablet.GrowFrames; frame++)
            {
                screen.Update(frame * 34, cameraOnPath: false, continueRequested: false);
                draw.Build(screen.Tablet, itemId: 1, originX: 30, originY: 60, screenHeight: 768);
                maxSparkles = Math.Max(maxSparkles, draw.Sparkles.Length);
            }
            return maxSparkles;
        }

        TabletMesh small = Mesh();
        PlayFrames(new WinScreen([1], _ => null, small.Points, new LongLivedSparkles()), new TabletDrawList(small));

        TabletMesh mesh = Mesh(extraPointCount: 14);
        var screen = new WinScreen([1], _ => null, mesh.Points, new LongLivedSparkles());
        var draw = new TabletDrawList(mesh);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int maxSparkles = PlayFrames(screen, draw);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(maxSparkles > 256);
        Assert.Equal(0, bytes);
    }

    private sealed class LongLivedSparkles : Random
    {
        public override int Next(int maxValue) => maxValue - 1;
    }
}
