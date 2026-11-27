// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary><see cref="TileHoverOutline.Build"/> against a copy of the Unity <c>ClassicTileHoverOutline.Update</c> it came
/// from (plan step 3e), float for float, and its own rules.</summary>
public sealed class TileHoverOutlineBuildTests
{
    [Fact]
    public void TheRibbonIsUnitysToTheBit()
    {
        var random = new Random(3);
        var ribbon = new TileHoverRibbon();
        var legacy = new LegacyOutline(null);
        long ms = 0;
        for (int round = 0; round < 200; round++)
        {
            int x0 = random.Next(0, 40), y0 = random.Next(0, 40);
            var area = new TileRectangle(x0, y0, x0 + random.Next(1, 7), y0 + random.Next(1, 7));
            var eye = new Vector3((float)random.NextDouble() * 60, 3 + (float)random.NextDouble() * 10, -(float)random.NextDouble() * 60);
            bool blue = random.Next(2) == 0;
            ms += random.Next(0, 90);
            TileHoverOutline.Build(ribbon, area, blue, eye, null, ms);
            legacy.Update(area, blue, eye, ms);
            AssertSame(legacy, ribbon);
        }
    }

    [InstallFact]
    public void TheBentRibbonIsUnitysToTheBit()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        var world = WorldState.FromMap(db, map, [0, 1]);
        var lattice = new TerrainLattice(db, world, PlayerIds.FirstKeeper) { HighWalls = true };
        var ribbon = new TileHoverRibbon();
        var legacy = new LegacyOutline(lattice);
        var random = new Random(5);
        for (int round = 0; round < 60; round++)
        {
            int x0 = random.Next(1, world.Width - 8), y0 = random.Next(1, world.Height - 8);
            var area = new TileRectangle(x0, y0, x0 + random.Next(1, 6), y0 + random.Next(1, 6));
            var eye = new Vector3(x0 + 2, 8, -(y0 + 9));
            TileHoverOutline.Build(ribbon, area, round % 2 == 0, eye, lattice, 1000 + round * 33);
            legacy.Update(area, round % 2 == 0, eye, 1000 + round * 33);
            AssertSame(legacy, ribbon);
        }
    }

    [Fact]
    public void OnlyBoundaryTilesDrawAndEachKeepsItsEdges()
    {
        var ribbon = new TileHoverRibbon();
        var eye = new Vector3(5, 10, -20);
        TileHoverOutline.Build(ribbon, new TileRectangle(2, 3, 3, 4), false, eye, null, 0);
        Assert.Equal(24 * 4, ribbon.VertexCount);
        Assert.Equal(24 * 6, ribbon.IndexCount);
        // A 3 x 3 area: the centre tile has no outer edge and draws nothing; every other tile draws its mask's segments.
        TileHoverOutline.Build(ribbon, new TileRectangle(0, 0, 3, 3), false, eye, null, 0);
        Span<int> scratch = stackalloc int[48];
        int expected = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                TileHoverEdges mask = TileHoverEdges.Lower | (y == 0 ? TileHoverEdges.North : 0) | (y == 2 ? TileHoverEdges.South : 0)
                    | (x == 0 ? TileHoverEdges.West : 0) | (x == 2 ? TileHoverEdges.East : 0);
                if (mask != TileHoverEdges.Lower)
                {
                    expected += TileHoverOutline.Indices(mask, scratch) / 2;
                }
            }
        }

        Assert.Equal(expected * 4, ribbon.VertexCount);
#pragma warning disable IDE0300, IDE0305 // Preserve the test's existing collection construction and binding.
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, ribbon.Indices[..6].ToArray());
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) }, ribbon.Uvs[..4].ToArray());
#pragma warning restore IDE0300, IDE0305
    }

    [Fact]
    public void TheColourIsThePulseBeforeItAdvances()
    {
        var ribbon = new TileHoverRibbon();
        var area = new TileRectangle(0, 0, 1, 1);
        var eye = new Vector3(0, 5, -5);
        TileHoverOutline.Build(ribbon, area, false, eye, null, 1000);
        Assert.Equal(Vector3.Zero, ribbon.Colour);
        // 125 ms advance the pulse by 0.5, shown on the next build.
        TileHoverOutline.Build(ribbon, area, false, eye, null, 1125);
        Assert.Equal(Vector3.Zero, ribbon.Colour);
        TileHoverOutline.Build(ribbon, area, true, eye, null, 1250);
        Assert.Equal(new Vector3(0, 0, .5f * TileHoverOutline.BluePeak), ribbon.Colour);
        TileHoverOutline.Build(ribbon, area, false, eye, null, 1250);
        Assert.Equal(new Vector3(1, 0, 0), ribbon.Colour);
        // A clock going back advances nothing.
        TileHoverOutline.Build(ribbon, area, false, eye, null, 900);
        Assert.Equal(new Vector3(1, 0, 0), ribbon.Colour);
        TileHoverOutline.Build(ribbon, area, false, eye, null, 900);
        Assert.Equal(new Vector3(1, 0, 0), ribbon.Colour);
    }

    [Fact]
    public void ARibbonOnTheEyesLineHasNoWidthRatherThanNaN()
    {
        var ribbon = new TileHoverRibbon();
        // The west post of tile (0, 0): its first segment runs from (0, 2.05, 0) to (0, 1.55, 0) in the renderers' frame,
        // its second on to (0, 1.05, 0); an eye on their line.
        TileHoverOutline.Build(ribbon, new TileRectangle(0, 0, 1, 1), false, new Vector3(0, 9, 0), null, 0);
        foreach (Vector3 p in ribbon.Positions)
        {
            Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
        }
    }

    [Theory]
    [InlineData(1e-5f, false)]
    [InlineData(1e-3f, true)]
    public void ARibbonCutsItsWidthBelowACrossLengthOf1EMinus5(float offset, bool wide)
    {
        var ribbon = new TileHoverRibbon();
        // The post's first segment is 0.5 long, so an eye this far off its line makes a cross product of offset / 2:
        // 5e-6 is under Unity's 1e-5 cut, 5e-4 over it.
        TileHoverOutline.Build(ribbon, new TileRectangle(0, 0, 1, 1), false, new Vector3(0, 9, offset), null, 0);
        Assert.Equal(2.05f, ribbon.Positions[64].Y, 4);
        Assert.Equal(1.55f, ribbon.Positions[66].Y, 4);
        Assert.Equal(wide, ribbon.Positions[64] != ribbon.Positions[65]);
    }

    [Fact]
    public void ARebuildAllocatesNothing()
    {
        var ribbon = new TileHoverRibbon();
        var area = new TileRectangle(4, 4, 9, 9);
        var eye = new Vector3(6, 8, -14);
        TileHoverOutline.Build(ribbon, area, true, eye, null, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            TileHoverOutline.Build(ribbon, area, i % 2 == 0, eye, null, 2 + i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void AssertSame(LegacyOutline legacy, TileHoverRibbon ribbon)
    {
        Assert.Equal(legacy.Vertices.Count, ribbon.VertexCount);
        for (int i = 0; i < ribbon.VertexCount; i++)
        {
            Assert.Equal(legacy.Vertices[i], ribbon.Positions[i]);
            Assert.Equal(legacy.Uvs[i], ribbon.Uvs[i]);
        }
        Assert.Equal(legacy.Triangles, ribbon.Indices.ToArray());
        Assert.Equal(legacy.Colour, ribbon.Colour);
    }

    /// <summary>Unity's <c>ClassicTileHoverOutline.Update</c> and <c>AddTile</c> before the move (its seconds to milliseconds
    /// step stays in Unity), with Unity's vector
    /// arithmetic (<c>Cross</c>, <c>normalized</c> through a double square root and its 1e-5 cut) spelt out.</summary>
    private sealed class LegacyOutline
    {
        private readonly TerrainLattice? _lattice;

        public LegacyOutline(TerrainLattice? lattice)
        {
            _lattice = lattice;
        }

        private readonly Vector3[] _grid = new Vector3[27];
        private readonly Vector3[] _points = new Vector3[27];
        private readonly int[] _endpoints = new int[48];
        private readonly TileHoverPulse _pulse = new TileHoverPulse();
        private long _lastMilliseconds;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        public Vector3 Colour;

        public void Update(TileRectangle rect, bool blue, Vector3 eye, long milliseconds)
        {
            int delta = _lastMilliseconds == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, milliseconds - _lastMilliseconds));
            _lastMilliseconds = milliseconds;
            float level = _pulse.Level;
            _pulse.Advance(delta);
            Vertices.Clear(); Uvs.Clear(); Triangles.Clear();
            for (int y = rect.Y0; y < rect.Y1; y++)
            {
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    TileHoverEdges mask = TileHoverEdges.Lower
                        | (y == rect.Y0 ? TileHoverEdges.North : 0) | (y == rect.Y1 - 1 ? TileHoverEdges.South : 0)
                        | (x == rect.X0 ? TileHoverEdges.West : 0) | (x == rect.X1 - 1 ? TileHoverEdges.East : 0);
                    if (mask == TileHoverEdges.Lower)
                    {
                        continue;
                    }

                    AddTile(new TilePos(x, y), mask, eye);
                }
            }

            Colour = blue ? new Vector3(0, 0, level * TileHoverOutline.BluePeak) : new Vector3(level, 0, 0);
        }

        private void AddTile(TilePos at, TileHoverEdges mask, Vector3 eye)
        {
            TileHoverOutline.Grid(at, _grid);
            for (int i = 0; i < _grid.Length; i++)
            {
                Vector3 p = _grid[i];
                if (_lattice != null)
                {
                    p.Z += .05f;
                    p = _lattice.Deform(p, at.X, at.Y);
                    p.Z -= .05f;
                }
                _points[i] = new Vector3(p.X, -p.Z, -p.Y);
            }
            int count = TileHoverOutline.Indices(mask, _endpoints);
            for (int i = 0; i < count; i += 2)
            {
                Vector3 a = _points[_endpoints[i]], b = _points[_endpoints[i + 1]];
                Vector3 l = b - a, r = eye - (a + b) * .5f;
                var cross = new Vector3(l.Y * r.Z - l.Z * r.Y, l.Z * r.X - l.X * r.Z, l.X * r.Y - l.Y * r.X);
                float magnitude = (float)Math.Sqrt(cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z);
                Vector3 unit = magnitude > 1E-05f ? new Vector3(cross.X / magnitude, cross.Y / magnitude, cross.Z / magnitude) : Vector3.Zero;
                Vector3 side = unit * TileHoverOutline.Radius;
                int v = Vertices.Count;
                Vertices.Add(a - side); Vertices.Add(a + side); Vertices.Add(b - side); Vertices.Add(b + side);
                Uvs.Add(new Vector2(0, 0)); Uvs.Add(new Vector2(0, 1)); Uvs.Add(new Vector2(1, 0)); Uvs.Add(new Vector2(1, 1));
                Triangles.Add(v); Triangles.Add(v + 1); Triangles.Add(v + 2);
                Triangles.Add(v + 2); Triangles.Add(v + 1); Triangles.Add(v + 3);
            }
        }
    }
}
