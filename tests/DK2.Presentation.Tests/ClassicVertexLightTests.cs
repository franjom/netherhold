// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using L = DK2.Presentation.Tests.LegacyClassicVertexLit;

namespace DK2.Presentation.Tests;

public class ClassicVertexLightTests
{
    private const int Width = 6, Height = 5;

    private static readonly ClassicLightFlags[] Flags =
    [
        ClassicLightFlags.Terrain, ClassicLightFlags.CheapTorch, ClassicLightFlags.NormalTorch, ClassicLightFlags.NormalObject,
        ClassicLightFlags.CheapObject, ClassicLightFlags.AnimatedRadial, ClassicLightFlags.InstancePoint, ClassicLightFlags.None,
        (ClassicLightFlags)0x60, (ClassicLightFlags)0xA0, (ClassicLightFlags)0x01, (ClassicLightFlags)0x1FF,
    ];

    [Fact]
    public void TheSumIsTheUnityShadersOnRandomTablesAndVertices()
    {
        var random = new Random(7);
        int nonZero = 0, skippedBounds = 0;
        for (int round = 0; round < 400; round++)
        {
            (ClassicTileLightSource[] sources, uint[] counts, L.TileSource[] legacy) = RandomGrid(random);
            for (int v = 0; v < 40; v++)
            {
                ClassicLitVertex vertex = RandomVertex(random);
                Vector3 expected = Legacy(vertex, legacy, counts);
                Vector3 actual = ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex);
                Assert.Equal(expected, actual);
                if (actual != Vector3.Zero)
                {
                    nonZero++;
                }

                if (vertex.LightKind.Y == 1)
                {
                    skippedBounds++;
                }
            }
        }
        // The cases reach the light: most sums are not zero.
        Assert.True(nonZero > 4000, nonZero.ToString());
        Assert.True(skippedBounds > 1000);
    }

    [Fact]
    public void ThePackedDiffuseIsTheUnityShadersOnRandomSums()
    {
        var random = new Random(11);
        for (int i = 0; i < 20000; i++)
        {
            var sum = new Vector3(Next(random, -0.5f, 3f), Next(random, -0.5f, 1.5f), random.Next(256) / 255f);
            L.Float3 legacy = L.DK2PackedDiffuse(new L.Float3(sum.X, sum.Y, sum.Z));
            Assert.Equal(new Vector3(legacy.X, legacy.Y, legacy.Z), ClassicVertexLight.PackedDiffuse(sum));
        }
    }

    [Fact]
    public void ThePackedDiffuseFloorsToBytesAndClamps()
    {
        Assert.Equal(new Vector3(127f / 255f, 0f, 1f), ClassicVertexLight.PackedDiffuse(new Vector3(0.5f, -2f, 7f)));
        // Each byte value comes back as itself: the nudge keeps k/255 from flooring to k - 1.
        for (int k = 0; k <= 255; k++)
        {
            Assert.Equal(k / 255f, ClassicVertexLight.PackedDiffuse(new Vector3(k / 255f)).X);
        }

        Assert.Equal(Vector3.Zero, ClassicVertexLight.PackedDiffuse(new Vector3(float.NaN)));
        Assert.Equal(254f / 255f, ClassicVertexLight.PackedDiffuse(new Vector3(254.9f / 255f)).X);
    }

    [Fact]
    public void TheTileIsTheAnchorsForTerrainAndTheOriginsOtherwise()
    {
        Assert.Equal(13, ClassicVertexLight.TableTile(new Vector2(13, 1), new Vector3(100, 0, 100), Width, Width * Height));
        Assert.Equal(13, ClassicVertexLight.TableTile(new Vector2(13.9f, 2), Vector3.Zero, Width, Width * Height));
        // Kind 0: the tile under the origin, Z running north.
        Assert.Equal(2 * Width + 3, ClassicVertexLight.TableTile(new Vector2(13, 0), new Vector3(3.9f, 7f, -2.1f), Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(Vector2.Zero, new Vector3(-0.1f, 0, -1), Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(Vector2.Zero, new Vector3(Width, 0, -1), Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(Vector2.Zero, new Vector3(1, 0, 0.5f), Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(Vector2.Zero, new Vector3(1, 0, -Height - 0.5f), Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(new Vector2(Width * Height, 1), Vector3.Zero, Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(new Vector2(-1, 1), Vector3.Zero, Width, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(new Vector2(0, 1), Vector3.Zero, 0, Width * Height));
        Assert.Equal(-1, ClassicVertexLight.TableTile(new Vector2(0, 1), Vector3.Zero, Width, 0));
    }

    [Fact]
    public void ATerrainVertexTakesThePointTermOnceOrTwiceByFlag()
    {
        ClassicPointLight light = Light(new Vector3(1.5f, 1f, -1.5f), ClassicLightFlags.None);
        Vector3 at = new(1.5f, 0f, -1.2f), up = Vector3.UnitY;
        Vector3 once = light.ContributionAt(at, up);
        Assert.NotEqual(Vector3.Zero, once);
        Assert.Equal(Vector3.Zero, Sum(light, ClassicLightFlags.None, Terrain(at, up)));
        Assert.Equal(once, Sum(light, (ClassicLightFlags)0x60, Terrain(at, up)));
        Assert.Equal(once, Sum(light, (ClassicLightFlags)0xA0, Terrain(at, up)));
        Assert.Equal(once, Sum(light, (ClassicLightFlags)0x01, Terrain(at, up)));
        // 0x64 and 0x74 meet only 0x60, 0x1B2 only 0xA0, 0x191 only 0x01; 0xE0 meets both.
        Assert.Equal(once, Sum(light, ClassicLightFlags.Terrain, Terrain(at, up)));
        Assert.Equal(once, Sum(light, ClassicLightFlags.CheapTorch, Terrain(at, up)));
        Assert.Equal(once, Sum(light, ClassicLightFlags.NormalTorch, Terrain(at, up)));
        Assert.Equal(once, Sum(light, ClassicLightFlags.NormalObject, Terrain(at, up)));
        Assert.Equal(once + once, Sum(light, (ClassicLightFlags)0xE0, Terrain(at, up)));
        Assert.Equal(once + once, Sum(light, (ClassicLightFlags)0x61, Terrain(at, up)));
        // An instance's flags light no terrain.
        Assert.Equal(Vector3.Zero, Sum(light, ClassicLightFlags.InstancePoint | ClassicLightFlags.AnimatedRadial, Terrain(at, up)));
    }

    [Fact]
    public void AnInstanceTakesTheRadialAndInstancePointTermsByFlag()
    {
        ClassicPointLight light = Light(new Vector3(2.4f, 1.2f, -1.5f), ClassicLightFlags.None);
        Matrix4x4 m = Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(1.6f, 0.2f, -1.4f);
        Vector3 local = new(0.1f, 0.3f, 0.05f), normal = Vector3.Normalize(new Vector3(1, 0.2f, 0));
        ClassicLitVertex vertex = Instance(local, normal, m, new Vector4(0, 0.25f, 0, 0.3f), boundsKind: 0);
        Vector3 radial = light.RadialAt(m.Translation);
        var centre = Vector3.TransformNormal(new Vector3(0, 0.25f, 0), m);
        var sampled = ClassicInstancePointLight.Sample(light, m.Translation + centre, m);
        Vector3 point = sampled.ContributionAt(local - centre, normal);
        Assert.NotEqual(Vector3.Zero, radial);
        Assert.NotEqual(Vector3.Zero, point);
        Assert.Equal(radial, Sum(light, ClassicLightFlags.AnimatedRadial, vertex));
        AssertNear(point, Sum(light, ClassicLightFlags.InstancePoint, vertex));
        AssertNear(radial + point, Sum(light, ClassicLightFlags.AnimatedRadial | ClassicLightFlags.InstancePoint, vertex));
        AssertNear(point, Sum(light, ClassicLightFlags.NormalObject, vertex));
        Assert.Equal(radial, Sum(light, ClassicLightFlags.CheapObject, vertex));
        // The terrain flags light no instance.
        Assert.Equal(Vector3.Zero, Sum(light, (ClassicLightFlags)0xFB, vertex));
    }

    [Fact]
    public void ABoundedVertexSkipsASourcePastItsRadiusPlusTheScaledSphere()
    {
        // Radius 1 light 1.5 from the sample: a bound of 0.4 (scale 1) misses it, of 0.3 x 2 reaches it.
        ClassicPointLight light = Light(new Vector3(1.5f + 1.5f, 0.5f, -1.5f), ClassicLightFlags.None, radius: 1f);
        Vector3 at = new(1.5f, 0.5f, -1.5f);
        ClassicLitVertex near = Instance(Vector3.Zero, Vector3.UnitX, Matrix4x4.CreateTranslation(at), new Vector4(0, 0, 0, 0.4f), 1)
            with { LightKind = new Vector2(0, 1), WorldPosition = at + new Vector3(0.6f, 0, 0), WorldNormal = Vector3.UnitX };
        Assert.Equal(Vector3.Zero, Sum(light, ClassicLightFlags.Terrain, near));
        ClassicLitVertex scaled = near with
        {
            LightBounds = new Vector4(0, 0, 0, 0.3f),
            ObjectToWorld = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(at),
        };
        Assert.NotEqual(Vector3.Zero, Sum(light, ClassicLightFlags.Terrain, scaled));
        // Without the bound kind the same vertex is lit, the skip being the sphere's alone.
        Assert.NotEqual(Vector3.Zero, Sum(light, ClassicLightFlags.Terrain, near with { LightKind = Vector2.Zero }));
    }

    [Fact]
    public void ATileReadsAtMostTwentyFourSourcesAndOnlyItsCount()
    {
        var sources = new ClassicTileLightSource[Width * Height * ClassicLightTable.MaximumSources];
        uint[] counts = new uint[Width * Height];
        ClassicPointLight light = Light(new Vector3(1.5f, 1f, -1.5f), ClassicLightFlags.None);
        int tile = 1 * Width + 1;
        for (int i = 0; i < ClassicLightTable.MaximumSources; i++)
        {
            sources[tile * ClassicLightTable.MaximumSources + i] = ClassicTileLightSource.Of(light) with
            {
                IntensityFlags = (uint)Math.Round(light.Intensity * 128f) | 0x60u << 8,
            };
        }

        ClassicLitVertex vertex = Terrain(new Vector3(1.5f, 0f, -1.2f), Vector3.UnitY) with { TileAnchor = new Vector2(tile, 1) };
        Vector3 one = light.ContributionAt(vertex.WorldPosition, Vector3.UnitY);
        counts[tile] = 3;
        AssertNear(one * 3, ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex));
        counts[tile] = 99;
        AssertNear(one * 24, ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex));
        counts[tile] = 0;
        Assert.Equal(Vector3.Zero, ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex));
    }

    [Fact]
    public void ZeroIncidenceInTheFirstBinAddsNothingAsTheShadersDoWhereDk2IsUnproven()
    {
        ClassicPointLight light = Light(new Vector3(1.5f, 1f, -1.5f), ClassicLightFlags.None);
        // Tangent to the light in the first bin: DK2's result there depends on the FPU exception mask and is not proven
        // (ClassicPointLight.ContributionAt throws); the shaders add nothing.
        ClassicLitVertex vertex = Terrain(new Vector3(1.6f, 1f, -1.5f), Vector3.UnitY);
        Assert.Equal(Vector3.Zero, Sum(light, ClassicLightFlags.NormalTorch, vertex));
        // Facing it in the first bin: the full colour, twice under 0xE0.
        ClassicLitVertex facing = vertex with { WorldNormal = -Vector3.UnitX };
        Assert.Equal(light.Colour * 2, Sum(light, (ClassicLightFlags)0xE0, facing));
    }

    [Fact]
    public void TheBinRoundsHalfToEvenAfterTheNudgeAndReachesTwoHundredAndFiftyFive()
    {
        // d = 0.25: 16 d² - 0.49999 = 0.50001, bin 1 (the first bin would give the full colour).
        Vector3 one = Edge(0.25f, radius: 2f);
        Assert.NotEqual(Vector3.Zero, one);
        Assert.True(one.X < 0.5f);
        // d = 0.43301198: 16 d² - 0.49999 is 2.5 exactly in float, bin 2 by round half to even.
        float a = 0.43301198f, d2 = a * a, factor = a / 128f * (0.25f + 16f / 2) * (4f - d2) / 4f;
        Assert.Equal(2.5f, d2 * 16f - 0.49999f);
        Assert.Equal(new Vector3(factor, factor * 0.5f, 0f), Edge(a, radius: 2f));
        // d = 3.996 under a radius of 4.1: bin 255, the last one lit.
        Assert.NotEqual(Vector3.Zero, Edge(3.996f, radius: 4.1f));
    }

    [Fact]
    public void ASourceExactlyAtTheBoundIsSkipped()
    {
        // Radius 1 plus a bound of 0.5 is 1.5, the sample's distance exactly: skipped (>=), not lit.
        var source = new ClassicTileLightSource(new Vector4(1.5f + 1.5f, 0.5f, -1.5f, 1f), new Vector3(1, 0.5f, 0), 128u | 0x60u << 8);
        var at = new Vector3(1.5f, 0.5f, -1.5f);
        var vertex = new ClassicLitVertex(Vector3.Zero, Vector3.UnitX, Vector2.Zero, new Vector4(0, 0, 0, 0.5f), new Vector2(0, 1),
            Matrix4x4.CreateTranslation(at), at + new Vector3(0.75f, 0, 0), Vector3.UnitX);
        Assert.Equal(Vector3.Zero, One(source, vertex));
        Assert.NotEqual(Vector3.Zero, One(source, vertex with { LightBounds = new Vector4(0, 0, 0, 0.5001f) }));
    }

    [Fact]
    public void ThePackingNudgeLiftsAChannelJustUnderAByte()
    {
        Assert.Equal(1f / 255f, ClassicVertexLight.PackedDiffuse(new Vector3((1f - 4e-6f) / 255f)).X);
    }

    /// <summary>A terrain vertex at the origin facing +X, lit by one source <paramref name="distance"/> along X of
    /// intensity 1/128 under 0x60 (once).</summary>
    private static Vector3 Edge(float distance, float radius)
    {
        var source = new ClassicTileLightSource(new Vector4(distance, 0, 0, radius * radius), new Vector3(1, 0.5f, 0), 1u | 0x60u << 8);
        var vertex = new ClassicLitVertex(Vector3.Zero, Vector3.UnitX, new Vector2(Width + 1, 1), Vector4.Zero, Vector2.Zero,
            Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        return One(source, vertex);
    }

    /// <summary>The sum from one source in the vertex's table, checked against the Unity shader's.</summary>
    private static Vector3 One(ClassicTileLightSource source, ClassicLitVertex vertex)
    {
        var sources = new ClassicTileLightSource[Width * Height * ClassicLightTable.MaximumSources];
        uint[] counts = new uint[Width * Height];
        int tile = ClassicVertexLight.TableTile(vertex.TileAnchor, vertex.ObjectToWorld.Translation, Width, Width * Height);
        Assert.True(tile >= 0);
        sources[tile * ClassicLightTable.MaximumSources] = source;
        counts[tile] = 1;
        Vector3 sum = ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex);
        Assert.Equal(Legacy(vertex, Convert(sources), counts), sum);
        return sum;
    }

    [Fact]
    public void ASourcePacksAsUnitysBufferDoes()
    {
        var random = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            ClassicPointLight light = Light(new Vector3(Next(random, 0, 6), Next(random, 0, 3), -Next(random, 0, 5)),
                Flags[random.Next(Flags.Length)], radius: Next(random, 0, 2.4f), intensity: random.Next(0, 256));
            L.TileSource legacy = L.Entry(light);
            var source = ClassicTileLightSource.Of(light);
            Assert.Equal(legacy.IntensityFlags, source.IntensityFlags);
            Assert.Equal(new Vector4(legacy.PositionRadiusSquared.X, legacy.PositionRadiusSquared.Y,
                legacy.PositionRadiusSquared.Z, legacy.PositionRadiusSquared.W), source.PositionRadiusSquared);
            Assert.Equal(new Vector3(legacy.Colour.X, legacy.Colour.Y, legacy.Colour.Z), source.Colour);
            Assert.Equal((uint)light.Flags, source.Flags);
        }
        Assert.Equal(1.5f, new ClassicTileLightSource(default, default, 192u | 7u << 8).Intensity);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange((expected - actual).Length(), 0f, 1e-5f);
    }

    private static Vector3 Sum(ClassicPointLight light, ClassicLightFlags flags, ClassicLitVertex vertex)
    {
        var sources = new ClassicTileLightSource[Width * Height * ClassicLightTable.MaximumSources];
        uint[] counts = new uint[Width * Height];
        int tile = ClassicVertexLight.TableTile(vertex.TileAnchor, vertex.ObjectToWorld.Translation, Width, Width * Height);
        Assert.True(tile >= 0);
        sources[tile * ClassicLightTable.MaximumSources] = ClassicTileLightSource.Of(light) with
        {
            IntensityFlags = (uint)Math.Round(light.Intensity * 128f) | (uint)flags << 8,
        };
        counts[tile] = 1;
        Vector3 sum = ClassicVertexLight.TableSum(new ClassicTileLightGrid(sources, counts, Width), vertex);
        Assert.Equal(Legacy(vertex, Convert(sources), counts), sum);
        return sum;
    }

    private static ClassicLitVertex Terrain(Vector3 at, Vector3 normal)
    {
        return new(at, normal, new Vector2(1 * Width + 1, 1), Vector4.Zero, Vector2.Zero, Matrix4x4.Identity, at, normal);
    }

    private static ClassicLitVertex Instance(Vector3 local, Vector3 normal, Matrix4x4 m, Vector4 bounds, int boundsKind)
    {
        return new(local, normal, Vector2.Zero, bounds, new Vector2(1, boundsKind), m, Vector3.Transform(local, m),
            Vector3.Normalize(Vector3.TransformNormal(normal, m)));
    }

    private static ClassicPointLight Light(Vector3 at, ClassicLightFlags flags, float radius = 2f, int intensity = 128)
    {
        return ClassicPointLight.FromTorch(new TorchView(0, default, (int)(at.X * Fp32.One), (int)(-at.Z * Fp32.One),
            (int)(at.Y * Fp32.One), 0, 255, 128, 64, (byte)intensity, (int)(radius * Fp32.One), default), flags);
    }

    private static float Next(Random random, float low, float high)
    {
        return low + (float)random.NextDouble() * (high - low);
    }

    private static (ClassicTileLightSource[], uint[], L.TileSource[]) RandomGrid(Random random)
    {
        var sources = new ClassicTileLightSource[Width * Height * ClassicLightTable.MaximumSources];
        uint[] counts = new uint[Width * Height];
        for (int tile = 0; tile < counts.Length; tile++)
        {
            counts[tile] = (uint)random.Next(0, 30);
            for (int slot = 0; slot < ClassicLightTable.MaximumSources; slot++)
            {
                int x = tile % Width, y = tile / Width;
                var at = new Vector3(x + Next(random, -1, 2), Next(random, -0.5f, 2), -y - Next(random, -1, 2));
                float radius = random.Next(8) == 0 ? 0 : Next(random, 0.2f, 2.47f);
                uint packed = (uint)random.Next(0, 256) | (uint)Flags[random.Next(Flags.Length)] << 8;
                sources[tile * ClassicLightTable.MaximumSources + slot] = new ClassicTileLightSource(
                    new Vector4(at, radius * radius), new Vector3(Next(random, 0, 2), Next(random, 0, 1), Next(random, 0, 1)), packed);
            }
        }
        return (sources, counts, Convert(sources));
    }

    private static ClassicLitVertex RandomVertex(Random random)
    {
        var position = new Vector3(Next(random, -0.6f, 0.6f), Next(random, -0.2f, 1.2f), Next(random, -0.6f, 0.6f));
        var normal = Vector3.Normalize(new Vector3(Next(random, -1, 1), Next(random, -1, 1), Next(random, -1, 1)) + new Vector3(0, 0.01f, 0));
        if (random.Next(10) == 0)
        {
            normal = -normal * 0f;
        }

        float scale = random.Next(3) == 0 ? Next(random, 0.5f, 2f) : 1f;
        Matrix4x4 m = Matrix4x4.CreateScale(scale, random.Next(4) == 0 ? scale * 1.5f : scale, scale)
            * Matrix4x4.CreateFromYawPitchRoll(Next(random, -3, 3), Next(random, -0.5f, 0.5f), Next(random, -0.5f, 0.5f))
            * Matrix4x4.CreateTranslation(Next(random, -0.5f, Width + 0.5f), Next(random, 0, 1), -Next(random, -0.5f, Height + 0.5f));
        float anchorKind = random.Next(4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 0.5f };
        var anchor = new Vector2(random.Next(-2, Width * Height + 2) + Next(random, 0, 0.99f), anchorKind);
        var bounds = new Vector4(Next(random, -0.3f, 0.3f), Next(random, 0, 0.6f), Next(random, -0.3f, 0.3f), Next(random, 0, 0.8f));
        var kind = new Vector2(random.Next(3) == 0 ? 0 : 1, random.Next(2) == 0 ? 0 : 1);
        if (random.Next(5) == 0)
        {
            kind = random.Next(2) == 0 ? new Vector2(0.5f, 2) : new Vector2(2, 1);
        }

        var world = Vector3.Transform(position, m);
        var worldNormal = Vector3.TransformNormal(normal, m);
        return new ClassicLitVertex(position, normal, anchor, bounds, kind, m, world, worldNormal);
    }

    private static L.TileSource[] Convert(ClassicTileLightSource[] sources)
    {
        var legacy = new L.TileSource[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            ClassicTileLightSource s = sources[i];
            legacy[i] = new L.TileSource
            {
                PositionRadiusSquared = new L.Float4(s.PositionRadiusSquared.X, s.PositionRadiusSquared.Y, s.PositionRadiusSquared.Z,
                    s.PositionRadiusSquared.W),
                Colour = new L.Float3(s.Colour.X, s.Colour.Y, s.Colour.Z),
                IntensityFlags = s.IntensityFlags,
            };
        }
        return legacy;
    }

    private static Vector3 Legacy(in ClassicLitVertex vertex, L.TileSource[] sources, uint[] counts)
    {
        Matrix4x4 m = vertex.ObjectToWorld;
        var input = new L.Input
        {
            PositionOS = F(vertex.Position), NormalOS = F(vertex.Normal), PositionWS = F(vertex.WorldPosition),
            NormalWS = F(vertex.WorldNormal), AnchorX = vertex.TileAnchor.X, AnchorY = vertex.TileAnchor.Y,
            LightBounds = new L.Float4(vertex.LightBounds.X, vertex.LightBounds.Y, vertex.LightBounds.Z, vertex.LightBounds.W),
            KindX = vertex.LightKind.X, KindY = vertex.LightKind.Y,
            // HLSL's column-vector basis by rows: row i is the numerics matrix's column i.
            Basis = new L.Float3x3(new L.Float3(m.M11, m.M21, m.M31), new L.Float3(m.M12, m.M22, m.M32), new L.Float3(m.M13, m.M23, m.M33)),
            Origin = F(m.Translation),
        };
        L.Float3 sum = L.TileLightContribution(input, sources, counts, Width, Width * Height);
        return new Vector3(sum.X, sum.Y, sum.Z);
    }

    private static L.Float3 F(Vector3 v)
    {
        return new(v.X, v.Y, v.Z);
    }
}
