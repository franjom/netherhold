// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The effects' draw list, moved from Unity's <c>ClassicConstructionEffects</c>: the styles, the stream and
/// light order, the sprite colour, the frame picks and the quads.</summary>
public sealed class EffectDrawListTests
{
    private static readonly GameDatabase Empty = new([], [], [], new GlobalVariables());

    [Fact]
    public void TheModelIsBuiltWithTheAdaptersStylesAndTheShotsTakeTheSeedsLow32Bits()
    {
        var list = new EffectDrawList(Empty, 7);
        Assert.Equal(TerrainDebrisStyle.Crumbling, list.Model.TerrainDebris);
        Assert.Equal(CaptureEffectStyle.Native, list.Model.CaptureEffects);
        Assert.Equal(HeartSparkleStyle.PillarHeight, list.Model.HeartSparkles);
        Assert.Equal(5u, EffectDrawList.ShotSeed(0x1_0000_0005UL));
    }

    [Fact]
    public void EffectsAreDrawnBeforeShotBodiesAndTheirLightsComeFirst()
    {
        var wave = new EffectDef([])
        {
            Name = "wave", EffectId = 10, ArtResource = new("", 0, new byte[12], 0, 0, 0, 0),
            Light = new(0, 0, 0, 4096, 1, 9, 9, 9, 255), GenerateIds = [20], GenerationType = 4, ElementsPerTurn = 1,
            MinHp = 10, MaxHp = 10,
        };
        var spark = new EffectElementDef([])
        {
            Name = "spark", EffectElementId = 20, ArtResource = new("spark", 0, new byte[12], 1, 0, 0, 0),
            MinHp = 20, MaxHp = 20, MinScale = 4096, MaxScale = 4096,
        };
        var arrow = new ShotDef([])
        {
            Name = "arrow", ShotId = 1, MeshResource = new("arrow", 0, new byte[12], 5, 0, 0, 0),
            Light = new(0, 0, 0, 4096, 1, 1, 2, 3, 255), SoundCategory = "",
        };
        var database = new GameDatabase([], [], [], new GlobalVariables(), effects: [wave], effectElements: [spark],
            shots: [arrow]);
        long now = 0;
        var list = new EffectDrawList(database, 7, clock: () => new EffectTime(now), floorHeight: _ => default);
        list.Model.Request(new ViewEffectRequest(new(10), PlayerIds.FirstKeeper, new FpPosition3(new(10000), new(12000), new(0))));
        list.CaptureShots([new ShotState { Id = 1, ShotTypeId = 1, Health = 2, PosX = 1234, PosY = 5678 }], 1);
        now = 51;

        IReadOnlyList<EffectLightView> lights = list.Advance();
        EffectParticleStream stream = list.Particles;

        Assert.Equal(2, stream.Count);
        Assert.Same(Assert.Single(list.Model.Particles), stream[0]);
        Assert.Same(Assert.Single(list.ShotBodies.Particles), stream[1]);
        Assert.Equal(2, lights.Count);
        Assert.True(lights[0].Id > 0);
        Assert.Equal(-1, lights[1].Id);
    }

    [Fact]
    public void AnUnlitSpriteDrawsItsTintAlone()
    {
        var list = new EffectDrawList(Empty, 1) { TileLights = Lit };
        Assert.Equal(new EffectColour(40, 50, 60), list.SpriteColour(Particle(takesLight: false), new Vector3(3.5f, 1f, -5.5f)));
    }

    [Fact]
    public void WithoutTablesALitSpriteKeepsItsTintAndABlackOneDrawsWhite()
    {
        var list = new EffectDrawList(Empty, 1);
        Assert.Equal(new EffectColour(40, 50, 60), list.SpriteColour(Particle(takesLight: true), Vector3.Zero));
        Assert.Equal(EffectColour.White, list.SpriteColour(Particle(takesLight: true, red: 0, green: 0, blue: 0), Vector3.Zero));
        Assert.Equal(new EffectColour(0, 0, 1), list.SpriteColour(Particle(takesLight: true, red: 0, green: 0, blue: 1), Vector3.Zero));
    }

    [Fact]
    public void ALitSpriteOnATileWithoutATableDrawsItsBaseColour()
    {
        TilePos asked = default;
        var list = new EffectDrawList(Empty, 1)
        {
            TileLights = (TilePos tile, out ClassicLightTable table) => { asked = tile; table = null!; return false; },
        };
        Assert.Equal(new EffectColour(40, 50, 60), list.SpriteColour(Particle(takesLight: true), Vector3.Zero));
        Assert.Equal(new TilePos(3, 5), asked);
    }

    [Fact]
    public void ALitSpriteTakesItsTilesRadialLightAtItsPoint()
    {
        var list = new EffectDrawList(Empty, 1) { TileLights = Lit };
        var at = new Vector3(3.5f, 1f, -5.5f);
        ClassicLightTable table = Table();
        var (red, green, blue) = SpriteLighting.Lit(40, 50, 60, table.Lights, at);
        EffectColour colour = list.SpriteColour(Particle(takesLight: true), at);
        Assert.Equal(new EffectColour(red, green, blue), colour);
        Assert.NotEqual(new EffectColour(40, 50, 60), colour);
        Assert.NotEqual(colour, list.SpriteColour(Particle(takesLight: true), new Vector3(9f, 1f, -9f)));
    }

    [Theory]
    [InlineData(5, 12, 7, 3)]
    [InlineData(5, 12, 2, 1)]
    [InlineData(13, 10, 4, 2)]
    public void AnAnimatedMeshLoopsFromBirthAtItsAuthoredRate(int updates, int framesPerSecond, int frames, int frame) =>
        Assert.Equal(frame, EffectDrawList.MeshFrame(new ConstructionParticle { Definition = null!, VisualUpdates = updates },
            frames, framesPerSecond));

    [Fact]
    public void AShotBodysMeshFrameIsItsPhase()
    {
        var body = new ConstructionParticle { Definition = null!, VisualUpdates = 13, MeshPhaseRaw = 2048 };
        Assert.Equal(2, EffectDrawList.MeshFrame(body, 5, 10));
        Assert.Equal(ShotBodyParticles.FrameIndex(new Fp32(2048), 60), EffectDrawList.MeshFrame(body, 60, 10));
    }

    [Theory]
    [InlineData(8, 1, 1)]
    [InlineData(8, 60, 60)]
    [InlineData(8, 61, 10)]
    [InlineData(8, 0, 10)]
    [InlineData(8, -5, 10)]
    [InlineData(4, 30, 10)]
    public void TheAuthoredRateIsTheSecondDwordWhenInRange(int length, int dword, int rate)
    {
        byte[] block = new byte[12];
        BitConverter.GetBytes(dword).CopyTo(block, 4);
        var definition = Element(type: 6, unknown: block.AsMemory(0, length));
        Assert.Equal(rate, EffectDrawList.AuthoredFramesPerSecond(definition));
    }

    [Theory]
    [InlineData(6, null, EffectGroupKind.AnimatedMesh)]
    [InlineData(6, "part", EffectGroupKind.Mesh)]
    [InlineData(5, null, EffectGroupKind.Mesh)]
    [InlineData(1, "part", EffectGroupKind.Mesh)]
    [InlineData(1, null, EffectGroupKind.Sprite)]
    [InlineData(3, null, EffectGroupKind.Sprite)]
    public void ArtTypesFiveAndSixAndMeshEntriesAreMeshes(byte type, string? entry, EffectGroupKind kind) =>
        Assert.Equal(kind, EffectDrawList.Classify(Element(type), entry));

    [Fact]
    public void TheBlendTable()
    {
        Assert.Equal(new EffectBlend(EffectBlendFactor.One, EffectBlendFactor.Zero), EffectDrawList.Blend(EffectSpriteBlend.Opaque));
        Assert.Equal(new EffectBlend(EffectBlendFactor.SourceAlpha, EffectBlendFactor.OneMinusSourceAlpha),
            EffectDrawList.Blend(EffectSpriteBlend.Alpha));
        Assert.Equal(new EffectBlend(EffectBlendFactor.One, EffectBlendFactor.One), EffectDrawList.Blend(EffectSpriteBlend.Additive));
    }

    [Theory]
    [InlineData(0x100u, true, false)]
    [InlineData(0x40u, false, true)]
    [InlineData(0x140u, true, true)]
    [InlineData(0xBFu, false, false)]
    public void Flag0x100IsFlatAndFlag0x40StandsOnItsPoint(uint flags, bool flat, bool standsOnPoint)
    {
        EffectSpriteShape shape = EffectSpriteShape.Of(Element(1, flags), new EffectSpriteResource(8192, 2048, 1, 1,
            EffectSpriteBlend.Alpha, 0));
        Assert.Equal(new EffectSpriteShape(flat, standsOnPoint, 2f, .5f), shape);
    }

    [Fact]
    public void AParticleIsDrawnBetweenItsUpdatesAtItsScaleOver4096()
    {
        var source = new ConstructionParticle
        {
            Definition = Element(1), PreviousXRaw = 4096, XRaw = 3 * 4096, PreviousYRaw = 2048, YRaw = 2048,
            PreviousHeightRaw = 0, HeightRaw = 4096, InitialScale = 6144,
        };
        Assert.Equal(new Vector3(2f, .5f, -.5f), EffectDrawList.Position(source, .5f));
        Assert.Equal(new Vector3(3f, 1f, -.5f), EffectDrawList.Position(source, 3f));
        Assert.Equal(1.5f, EffectDrawList.Size(source));
    }

    [Fact]
    public void ABillboardStandingOnItsPointIsLiftedHalfItsHeightAndADigitMovesRight()
    {
        var list = new EffectDrawList(Empty, 1);
        ConstructionParticle source = Particle(takesLight: false, scale: 4096);
        source.ScreenOffsetXRaw = 2048;
        var position = new Vector3(1f, 2f, 3f);
        Vector3 right = Vector3.UnitX, up = Vector3.UnitY;

        EffectBillboard standing = list.Billboard(source, position, new EffectSpriteShape(false, true, 2f, 1f), right, up);
        Assert.Equal(new EffectBillboard(new Vector3(1.5f, 2.5f, 3f), 2f, 1f, new EffectColour(40, 50, 60)), standing);

        source.ScreenOffsetXRaw = 0;
        EffectBillboard plain = list.Billboard(source, position, new EffectSpriteShape(false, false, 2f, 1f), right, up);
        Assert.Equal(position, plain.Centre);
    }

    [Fact]
    public void ABillboardsColourIsTakenBeforeTheLift()
    {
        var list = new EffectDrawList(Empty, 1) { TileLights = Lit };
        ConstructionParticle source = Particle(takesLight: true, scale: 4096 * 4);
        var position = new Vector3(3.5f, 1f, -5.5f);
        EffectBillboard billboard = list.Billboard(source, position, new EffectSpriteShape(false, true, 1f, 1f),
            Vector3.UnitX, Vector3.UnitY);
        Assert.Equal(list.SpriteColour(source, position), billboard.Colour);
        Assert.NotEqual(list.SpriteColour(source, billboard.Centre), billboard.Colour);
    }

    [Fact]
    public void ABillboardQuadSpansTheCameraAxesWithItsAgesFrame()
    {
        var quads = new EffectQuadBuffer();
        ConstructionParticle source = Particle(takesLight: false);
        source.TexturePhaseRawOverride = 2048;
        var billboard = new EffectBillboard(new Vector3(1f, 2f, 3f), 2f, 4f, new EffectColour(1, 2, 3));

        EffectDrawList.AddBillboardQuad(quads, source, billboard, Vector3.UnitX, Vector3.UnitY, 4);

        Assert.Equal([new(0f, 0f, 3f), new(2f, 0f, 3f), new(2f, 4f, 3f), new(0f, 4f, 3f)], quads.Vertices.ToArray());
        Assert.Equal([new(.5f, 1f), new(.75f, 1f), new(.75f, 0f), new(.5f, 0f)], quads.Uvs.ToArray());
        Assert.All(quads.Colours.ToArray(), colour => Assert.Equal(new EffectColour(1, 2, 3), colour));
        Assert.Equal([0, 1, 2, 0, 2, 3], quads.Indices.ToArray());
    }

    [Fact]
    public void AFlatQuadLiesInTheGroundPlaneTurnedCounterClockwiseByItsHeading()
    {
        var list = new EffectDrawList(Empty, 1);
        var quads = new EffectQuadBuffer();
        ConstructionParticle source = Particle(takesLight: false, scale: 1024);
        source.XRaw = source.PreviousXRaw = 3 * 4096;
        source.YRaw = source.PreviousYRaw = 5 * 4096;
        source.HeightRaw = source.PreviousHeightRaw = 4096;

        list.AddFlatQuad(quads, source, 1f, 1);
        Assert.Equal([new(2.75f, 1f, -4.75f), new(3.25f, 1f, -4.75f), new(3.25f, 1f, -5.25f), new(2.75f, 1f, -5.25f)],
            quads.Vertices.ToArray());
        Assert.Equal([new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)], quads.Uvs.ToArray());
        Assert.Equal([0, 1, 2, 0, 2, 3], quads.Indices.ToArray());

        quads.Clear();
        source.HeadingRaw = source.PreviousHeadingRaw = 512;
        list.AddFlatQuad(quads, source, 1f, 1);
        Vector3[] turned = quads.Vertices.ToArray();
        AssertNear(new Vector3(3.25f, 1f, -4.75f), turned[0]);
        AssertNear(new Vector3(3.25f, 1f, -5.25f), turned[1]);
        AssertNear(new Vector3(2.75f, 1f, -5.25f), turned[2]);
    }

    [Fact]
    public void AFlatQuadInterpolatesPositionHeightAndHeadingAndClampsTheFraction()
    {
        var list = new EffectDrawList(Empty, 1);
        var quads = new EffectQuadBuffer();
        ConstructionParticle source = Particle(takesLight: false, scale: 1024);
        (source.PreviousXRaw, source.XRaw) = (2 * 4096, 4 * 4096);
        (source.PreviousYRaw, source.YRaw) = (4 * 4096, 6 * 4096);
        (source.PreviousHeightRaw, source.HeightRaw) = (0, 2 * 4096);
        (source.PreviousHeadingRaw, source.HeadingRaw) = (0, 512);

        list.AddFlatQuad(quads, source, .5f, 1);   // (3, 5), height 1, turned 45 degrees
        Vector3[] half = quads.Vertices.ToArray();
        float diagonal = .25f * MathF.Sqrt(2f);
        AssertNear(new Vector3(3f, 1f, -5f + diagonal), half[0]);
        AssertNear(new Vector3(3f + diagonal, 1f, -5f), half[1]);

        quads.Clear();
        list.AddFlatQuad(quads, source, 2f, 1);    // clamped to the current pose: (4, 6), height 2, 90 degrees
        Vector3[] past = quads.Vertices.ToArray();
        AssertNear(new Vector3(4.25f, 2f, -5.75f), past[0]);
        AssertNear(new Vector3(4.25f, 2f, -6.25f), past[1]);
    }

    [Fact]
    public void TheFramePickTakesTheLastFrameAtTheEndOfLifeAndTreatsAnEmptySheetAsOneFrame()
    {
        ConstructionParticle source = Particle(takesLight: false);
        source.TexturePhaseRawOverride = 4096;
        Assert.Equal((.75f, 1f), EffectDrawList.FrameSpan(source, 4));
        Assert.Equal((0f, 1f), EffectDrawList.FrameSpan(source, 0));
    }

    [Fact]
    public void QuadsGrowAndKeepTheirIndicesPastTheFirstBuffer()
    {
        var quads = new EffectQuadBuffer();
        var billboard = new EffectBillboard(Vector3.Zero, 1f, 1f, EffectColour.White);
        for (int i = 0; i < 5; i++)
            EffectDrawList.AddBillboardQuad(quads, Particle(takesLight: false), billboard, Vector3.UnitX, Vector3.UnitY, 1);
        Assert.Equal(20, quads.VertexCount);
        Assert.Equal(30, quads.IndexCount);
        Assert.Equal([16, 17, 18, 16, 18, 19], quads.Indices[24..].ToArray());
        quads.Clear();
        Assert.Equal((0, 0), (quads.VertexCount, quads.IndexCount));
    }

    [Fact]
    public void TheGroupKeyIsTheParentsSpriteElseTheElementsArt()
    {
        var list = new EffectDrawList(Empty, 1);
        ConstructionParticle source = Particle(takesLight: false);
        Assert.Equal(AssetRef.ForEffectElement("spark"), list.GroupKey(source));
        Assert.Equal(AssetRef.ForEffectElement("spark"), list.GroupKey(source));
        AssetRef parent = AssetRef.ForEffect("wave");
        source.SpriteAsset = parent;
        Assert.Equal(parent, list.GroupKey(source));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private static EffectElementDef Element(byte type, uint flags = 0, ReadOnlyMemory<byte> unknown = default) => new([])
    {
        Name = "spark", ArtResource = new("spark", flags, unknown.IsEmpty ? new byte[12] : unknown, type, 0, 0, 0),
    };

    private static ConstructionParticle Particle(bool takesLight, byte red = 40, byte green = 50, byte blue = 60,
        int scale = 4096) => new()
    {
        Definition = Element(1), TakesLight = takesLight, Red = red, Green = green, Blue = blue,
        XRaw = 3 * 4096 + 100, YRaw = 5 * 4096 + 200, InitialScale = scale,
    };

    private static ClassicLightTable Table()
    {
        var table = new ClassicLightTable(new TilePos(3, 5));
        table.RebuildStatic([ClassicPointLight.FromTerrain(new TilePos(3, 5), new Fp32(4096), new TerrainLightColour(200, 100, 50))]);
        return table;
    }

    private static bool Lit(TilePos tile, out ClassicLightTable table)
    {
        table = Table();
        return true;
    }
}
