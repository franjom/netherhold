// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The water and lava surfaces moved out of Unity's LiquidSurfaces (godot-step3-audit.md, 3c), pinned against
/// the old code (<see cref="Legacy"/>) vertex for vertex: positions exactly, and the layout (UVs, light anchors,
/// triangles) exactly. Unity recalculated the normals in the engine; the wave computes them from the faces.</summary>
public class LiquidSurfaceWaveTests
{
    private const int Width = 7, Height = 5;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(30)]
    [InlineData(1234)]
    [InlineData(600_000)]
    public void TheWaveMatchesTheOldSurfaces(int frame)
    {
        LiquidSurface[] kinds = Kinds();
        foreach (LiquidSurface kind in new[] { LiquidSurface.Water, LiquidSurface.Lava })
        {
            var wave = new LiquidSurfaceWave(kind, Width);
            wave.SetTiles(kinds);
            wave.Wave(frame);
            Legacy.Layer legacy = Legacy.Rebuild(kinds, kind);
            Legacy.Wave(legacy, kind, Width, frame);

            Assert.Equal(legacy.Tiles, wave.Tiles.ToArray());
            Assert.Equal(legacy.Positions, wave.Positions.AsSpan(0, wave.VertexCount).ToArray());
            Assert.Equal(legacy.Uvs, wave.Uvs.AsSpan(0, wave.VertexCount).ToArray());
            Assert.Equal(legacy.LightAnchors, wave.LightAnchors.AsSpan(0, wave.VertexCount).ToArray());
            Assert.Equal(legacy.Triangles, wave.Triangles.AsSpan(0, wave.IndexCount).ToArray());
        }
    }

    [Fact]
    public void TheRestHeightsAndAmplitudesAreDk2s()
    {
        Assert.Equal(0.9f, LiquidSurfaceWave.WaterRest);
        Assert.Equal(0.85f, LiquidSurfaceWave.LavaRest);
        Assert.Equal(0.9f + (0.03f * 1.5f), LiquidSurfaceWave.Height(LiquidSurface.Water, 1f, 0.5f));
        Assert.Equal(0.85f - (0.04f * 1.5f), LiquidSurfaceWave.Height(LiquidSurface.Lava, 1f, 0.5f));
        Assert.Equal("WaterSOLID0", new LiquidSurfaceWave(LiquidSurface.Water, 1).Texture);
        Assert.Equal("Lava0SOLID", new LiquidSurfaceWave(LiquidSurface.Lava, 1).Texture);
    }

    /// <summary>Hand-worked phases: water at tile 0, vertex 1, frame 0 is sin(0x8000 * pi / 65536) = sin(pi/2); lava at
    /// tile 1, vertex 0, frame 32 is sin(((0 - 64 + 32) &amp; 127) * pi / 64) = sin(96 pi / 64).</summary>
    [Fact]
    public void ThePhasesAreDk2sSines()
    {
        Assert.Equal(1f, LiquidSurfaceWave.Phase(LiquidSurface.Water, 0, 1, 0), 6);
        Assert.Equal(0f, LiquidSurfaceWave.Phase(LiquidSurface.Water, 1, 0, 0), 5);
        Assert.Equal(-1f, LiquidSurfaceWave.Phase(LiquidSurface.Lava, 1, 0, 32), 6);
        // Two tiles a period, and 32 frames a water quarter (4096 a frame of 0x20000).
        Assert.Equal(LiquidSurfaceWave.Phase(LiquidSurface.Water, 3, 2, 5), LiquidSurfaceWave.Phase(LiquidSurface.Water, 5, 2, 5));
        Assert.Equal(LiquidSurfaceWave.Phase(LiquidSurface.Water, 0, 1, 0), LiquidSurfaceWave.Phase(LiquidSurface.Water, 0, 0, 8));
        Assert.Equal(LiquidSurfaceWave.Phase(LiquidSurface.Lava, 3, 2, 5), LiquidSurfaceWave.Phase(LiquidSurface.Lava, 5, 2, 5));
    }

    /// <summary>The sine tables and integer phase indices a shader reads give the old phases to the bit, for every tile
    /// parity, vertex step and frame residue, including frames whose 4096*frame overflows.</summary>
    [Fact]
    public void ThePhaseTablesGiveTheOldPhases()
    {
        int[] frames = [0, 1, 7, 31, 32, 127, 128, 1234, 524_287, 524_288, 600_000, int.MaxValue - 200];
        foreach (LiquidSurface kind in new[] { LiquidSurface.Water, LiquidSurface.Lava })
        {
            Assert.True(LiquidSurfaceWave.Sines(kind).Length <= LiquidSurfaceWave.MaximumSines);
            Assert.Equal(LiquidSurfaceWave.PhaseMask(kind) + 1, LiquidSurfaceWave.Sines(kind).Length);
            foreach (int start in frames)
                for (int frame = start; frame < start + 130 && frame >= start; frame++)
                    for (int tile = 0; tile < 4; tile++)
                        for (int vertex = 0; vertex < 3; vertex++)
                            Assert.Equal(BitConverter.SingleToInt32Bits(Legacy.Phase(kind, tile, vertex, frame)),
                                BitConverter.SingleToInt32Bits(LiquidSurfaceWave.Phase(kind, tile, vertex, frame)));
        }
    }

    /// <summary>The signed amplitude a shader multiplies by gives <see cref="LiquidSurfaceWave.Height"/> to the bit for
    /// every pair of table sines.</summary>
    [Fact]
    public void TheSignedAmplitudeGivesTheHeights()
    {
        foreach (LiquidSurface kind in new[] { LiquidSurface.Water, LiquidSurface.Lava })
            foreach (float row in LiquidSurfaceWave.Sines(kind).ToArray())
                foreach (float column in LiquidSurfaceWave.Sines(kind).ToArray())
                    Assert.Equal(BitConverter.SingleToInt32Bits(LiquidSurfaceWave.Height(kind, row, column)),
                        BitConverter.SingleToInt32Bits(LiquidSurfaceWave.Rest(kind) + (LiquidSurfaceWave.SignedAmplitude(kind) * (row + column))));
    }

    /// <summary>Each vertex waved on its own from its grid point and the frame, as the Godot shader waves it, is the
    /// vertex <see cref="LiquidSurfaceWave.Wave"/> builds with the whole tile: position and normal to the bit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(30)]
    [InlineData(1234)]
    [InlineData(600_000)]
    public void EachVertexOnItsOwnIsTheWave(int frame)
    {
        LiquidSurface[] kinds = Kinds();
        foreach (LiquidSurface kind in new[] { LiquidSurface.Water, LiquidSurface.Lava })
        {
            var wave = new LiquidSurfaceWave(kind, Width);
            wave.SetTiles(kinds);
            wave.Wave(frame);
            for (int v = 0; v < wave.VertexCount; v++)
            {
                LiquidGridPoint point = wave.Points[v];
                int tile = wave.Tiles[v / LiquidSurfaceWave.VerticesPerTile];
                Assert.Equal(new TilePos(tile % Width, tile / Width), point.Tile);
                Assert.Equal(v % 3, point.Column);
                Assert.Equal(v % LiquidSurfaceWave.VerticesPerTile / 3, point.Row);
                (Vector3 position, Vector3 normal) = LiquidSurfaceWave.Vertex(kind, point, frame);
                Assert.Equal(wave.Positions[v], position);
                Assert.Equal(wave.Normals[v], normal);
            }
        }
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 30)]
    [InlineData(0.05f, 1)]
    [InlineData(2.0333f, 60)]
    public void TheFrameCounterRunsAtThirtyASecond(float seconds, int frame) =>
        Assert.Equal(frame, LiquidSurfaceWave.Frame(seconds));

    /// <summary>Each normal is the normalised sum of the unit face normals around it: up for a flat surface, and on a
    /// waved one tilted away from the higher neighbour.</summary>
    [Fact]
    public void TheNormalsComeFromTheFaces()
    {
        var wave = new LiquidSurfaceWave(LiquidSurface.Water, Width);
        wave.SetTiles(Kinds());
        for (int frame = 0; frame < 64; frame += 7)
        {
            wave.Wave(frame);
            for (int i = 0; i < wave.VertexCount; i++)
            {
                Vector3 n = wave.Normals[i];
                Assert.Equal(1f, n.Length(), 5);
                Assert.True(n.Y > 0.9f, $"normal {i} at frame {frame}: {n}");
            }
        }
        Vector3 expected = Vector3.Zero;
        int shared = 0;
        // Vertex 4, the first tile's centre, is in six of its eight triangles.
        for (int t = 0; t < LiquidSurfaceWave.IndicesPerTile; t += 3)
        {
            if (wave.Triangles[t] != 4 && wave.Triangles[t + 1] != 4 && wave.Triangles[t + 2] != 4) continue;
            Vector3 a = wave.Positions[wave.Triangles[t]], b = wave.Positions[wave.Triangles[t + 1]], c = wave.Positions[wave.Triangles[t + 2]];
            expected += Vector3.Normalize(Vector3.Cross(b - a, c - a));
            shared++;
        }
        Assert.Equal(6, shared);
        Assert.Equal(Vector3.Normalize(expected), wave.Normals[4]);
    }

    /// <summary>The field lays a layer out again only when a tile's surface changed, and a wave allocates nothing.</summary>
    [Fact]
    public void TheFieldRebuildsOnChangeAndWavesWithoutAllocating()
    {
        var field = new LiquidSurfaceField(Width, Height);
        field.Set(3, Surface(3, LiquidSurface.Water));
        field.Animate(0f);
        Assert.Equal(1, field.Water.TileCount);
        Assert.Equal(0, field.Lava.TileCount);
        int version = field.Water.Version;
        field.Set(3, Surface(3, LiquidSurface.Water));
        field.Animate(1f);
        Assert.Equal(version, field.Water.Version);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) field.Animate(i / 30f);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());

        int lavaVersion = field.Lava.Version;
        field.Set(4, Surface(4, LiquidSurface.Lava));
        field.Animate(2f);
        // Both adapters lay a layer out again only when its version moves.
        Assert.True(field.Lava.Version > lavaVersion);
        Assert.Equal(1, field.Lava.TileCount);
        Assert.Equal(4, field.Lava.Tiles[0]);
        Assert.Equal(LiquidSurfaceWave.Height(LiquidSurface.Lava,
            LiquidSurfaceWave.Phase(LiquidSurface.Lava, 0, 0, 60), LiquidSurfaceWave.Phase(LiquidSurface.Lava, 4, 0, 60)),
            field.Lava.Positions[0].Y);

        int waterVersion = field.Water.Version;
        field.Clear();
        Assert.Equal(0, field.Water.TileCount);
        Assert.True(field.Water.Version > waterVersion);
    }

    private static TileView Surface(int index, LiquidSurface surface) =>
        new(new TilePos(index % Width, index / Width), default, default, default, false, false, 0, 1000, surface: surface);

    private static LiquidSurface[] Kinds()
    {
        var kinds = new LiquidSurface[Width * Height];
        for (int i = 0; i < kinds.Length; i++) kinds[i] = (LiquidSurface)((i * 5 + i / 3) % 3);
        return kinds;
    }

    /// <summary>Unity's LiquidSurfaces.Rebuild layout and Wave at 0906dff6, with UnityEngine's Vector types as
    /// System.Numerics', Mathf.Sin as (float)Math.Sin and Mathf.PI as (float)Math.PI.</summary>
    private static class Legacy
    {
        private const float WaterRest = 0.9f, WaterAmplitude = 0.03f;
        private const float LavaRest = 0.85f, LavaAmplitude = 0.04f;
        private const float PI = (float)Math.PI;

        public sealed class Layer
        {
            public int[] Tiles = [];
            public Vector3[] Positions = [];
            public Vector2[] Uvs = [];
            public Vector2[] LightAnchors = [];
            public int[] Triangles = [];
        }

        public static Layer Rebuild(LiquidSurface[] kinds, LiquidSurface kind)
        {
            var tiles = new List<int>();
            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i] == kind) tiles.Add(i);
            int count = tiles.Count;
            var layer = new Layer
            {
                Tiles = tiles.ToArray(), Positions = new Vector3[count * 9], Uvs = new Vector2[count * 9],
                LightAnchors = new Vector2[count * 9], Triangles = new int[count * 24],
            };
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        int vertex = (k * 9) + (j * 3) + i;
                        layer.Uvs[vertex] = new Vector2(i * 0.5f, 1f - (j * 0.5f));
                        layer.LightAnchors[vertex] = new Vector2(tiles[k], 1);
                    }
                int n = k * 24;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int a = (k * 9) + (j * 3) + i, b = a + 1, c = a + 3, d = a + 4;
                        layer.Triangles[n++] = a; layer.Triangles[n++] = b; layer.Triangles[n++] = d;
                        layer.Triangles[n++] = a; layer.Triangles[n++] = d; layer.Triangles[n++] = c;
                    }
            }
            return layer;
        }

        public static void Wave(Layer layer, LiquidSurface kind, int width, int frame)
        {
            for (int k = 0; k < layer.Tiles.Length; k++)
            {
                int x = layer.Tiles[k] % width, y = layer.Tiles[k] / width;
                for (int j = 0; j < 3; j++)
                {
                    float row = Phase(kind, y, j, frame);
                    for (int i = 0; i < 3; i++)
                    {
                        float height = kind == LiquidSurface.Water
                            ? WaterRest + (WaterAmplitude * (row + Phase(kind, x, i, frame)))
                            : LavaRest - (LavaAmplitude * (row + Phase(kind, x, i, frame)));
                        layer.Positions[(k * 9) + (j * 3) + i] = new Vector3(x + (i * 0.5f), height, -(y + (j * 0.5f)));
                    }
                }
            }
        }

        public static float Phase(LiquidSurface kind, int tile, int vertex, int frame) =>
            kind == LiquidSurface.Water
                ? (float)Math.Sin(((tile * 0x10000) + (vertex * 0x8000) + (4096 * frame) & 0x1FFFF) * PI / 65536f)
                : (float)Math.Sin((((vertex * 32) - (tile * 64) + frame) & 127) * PI / 64f);
    }
}
