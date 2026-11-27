// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class LightningTests
{
    [Fact]
    public void ThePresentationCrtStreamHasTheNativeFifteenBitOutputs()
    {
        var random = new LightningRandom(1);
        Assert.Equal(new[] { 41, 18467, 6334, 26500 }, Enumerable.Range(0, 4).Select(_ => random.Next15()));
    }

    [Fact]
    public void SubdivisionStopsAtPointZeroSixBeforeThinningAndKeepsTheEndpoints()
    {
        var lightning = new LightningBolts(1);
        var start = new Vector3(1, 2, 3);
        var end = new Vector3(1, 2, 8);
        IReadOnlyList<LightningPath> paths = lightning.Build(start, end);
        Assert.NotEmpty(paths);
        LightningPath main = paths[^1];
        Assert.Equal(start, main.Nodes[0]);
        Assert.Equal(end, main.Nodes[^1]);
        Assert.All(paths, p =>
        {
            Assert.InRange(p.Nodes.Count, 2, 32);
            Assert.InRange(p.SubdivisionMaximumSegmentLength, 0, .060001f);
        });
        Assert.True(main.SubdivisionNodeCount > 32);
    }

    [Fact]
    public void SeededBoltsAreRepeatableAndTheNextFrameHasAFreshShape()
    {
        var first = new LightningBolts(7);
        var second = new LightningBolts(7);
        Vector3[] a = [.. first.Build(Vector3.Zero, Vector3.UnitZ * 4).SelectMany(p => p.Nodes)];
        Vector3[] b = [.. second.Build(Vector3.Zero, Vector3.UnitZ * 4).SelectMany(p => p.Nodes)];
        Assert.Equal(a, b);
        Assert.NotEqual(a, [.. first.Build(Vector3.Zero, Vector3.UnitZ * 4).SelectMany(p => p.Nodes)]);
    }

    [Fact]
    public void VeryShortBoltsNeedOnlyTheirEndpoints()
    {
        LightningPath path = Assert.Single(new LightningBolts(1).Build(Vector3.Zero, Vector3.UnitX * .05f));
        Assert.Equal(2, path.Nodes.Count);
        Assert.Equal(2, path.SubdivisionNodeCount);
    }

    [Fact]
    public void AShotLinkUsesTargetBaseAndCasterXyButKeepsTheShotHeight()
    {
        var link = new LightningLink(ProceduralArtKind.TripleLightning);
        LightningEndpoints? endpoints = link.Resolve(Point(1, 2, 9), new LightningTarget(true, false, Point(4, 5, 1)), new LightningCaster(true, Point(6, 7, 3)));
        Assert.Equal(new LightningEndpoints(Point(6, 7, 9), Point(4, 5, 1), 3), endpoints);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ADeadOrRoomTargetPermanentlyClearsTheRenderLink(bool exists, bool room)
    {
        var link = new LightningLink(ProceduralArtKind.Lightning);
        Assert.Null(link.Resolve(Point(0, 0, 9), new LightningTarget(exists, room, Point(4, 5, 1))));
        Assert.Null(link.Resolve(Point(0, 0, 9), new LightningTarget(true, false, Point(4, 5, 1))));
    }

    [Fact]
    public void ALostCasterLinkCannotBeReattachedWhenTheCreatureRevives()
    {
        var link = new LightningLink(ProceduralArtKind.Lightning);
        var target = new LightningTarget(true, false, Point(4, 5, 1));
        link.Resolve(Point(1, 2, 9), target, new LightningCaster(true, Point(6, 7, 3)));
        Assert.Equal(Point(1, 2, 9), link.Resolve(Point(1, 2, 9), target, new LightningCaster(true, null))!.Value.Start);
        Assert.Equal(Point(1, 2, 9), link.Resolve(Point(1, 2, 9), target, new LightningCaster(true, Point(6, 7, 3)))!.Value.Start);
    }

    [Fact]
    public void KindEightEndsAtAbsoluteOneTileUp()
    {
        var link = new LightningLink(ProceduralArtKind.FixedLightning);
        Assert.Equal(new LightningEndpoints(Point(1, 2, 9), Point(1, 2, 1), 1),
            link.Resolve(Point(1, 2, 9), default));
    }

    [Fact]
    public void CrackleIsClearedEveryChooserTickAndOnlyAnInstallSetsItWhileTheTimerRuns()
    {
        var crackle = new CreatureLightningVisual();
        crackle.Ignite(new GameTick(10));
        Assert.True(crackle.Choose(new GameTick(11), animationInstalled: true));
        Assert.True(crackle.Choose(new GameTick(11), animationInstalled: false));
        Assert.False(crackle.Choose(new GameTick(12), animationInstalled: false));
        Assert.True(crackle.Choose(new GameTick(13), animationInstalled: true));
        Assert.False(crackle.Choose(new GameTick(30), animationInstalled: true));
    }

    [Fact]
    public void ANewLightningHitDoesNotCarryThePreviousTicksCrackleBitIntoTheNextChooser()
    {
        var crackle = new CreatureLightningVisual();
        crackle.Ignite(new GameTick(10));
        Assert.True(crackle.Choose(new GameTick(11), animationInstalled: true));
        crackle.Ignite(new GameTick(12));
        Assert.False(crackle.Choose(new GameTick(12), animationInstalled: false));
        Assert.True(crackle.Choose(new GameTick(31), animationInstalled: true));
        Assert.False(crackle.Choose(new GameTick(32), animationInstalled: true));
    }

    [Fact]
    public void CrackleSkipsConsecutiveIdRunsAndUsesEightTimesTheOuterIndex()
    {
        var random = new PairDraws();
        var pairs = new LightningCracklePairs(random);
        LightningControlPoint[] points = [new LightningControlPoint(1000, Vector3.Zero), new LightningControlPoint(1001, Vector3.UnitX),
            new LightningControlPoint(1010, Vector3.UnitY), new LightningControlPoint(1011, Vector3.UnitZ)];
        LightningSegment[] links = [.. pairs.Choose(points)];
        Assert.Equal(new[] { 24, 24, 16, 16 }, random.Bounds);
        Assert.Equal(4, links.Length);
        Assert.Equal(new LightningSegment(Vector3.UnitX, Vector3.UnitZ), links[0]);
        Assert.All(links, link => Assert.True(link.End.Y == 1 || link.End.Z == 1));
    }

    private sealed class PairDraws : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax) { Bounds.Add(exclusiveMax); return 1; }
        public void HashInto(ref Hash64 hash) { }
    }

    [Fact]
    public void TheRibbonHasZeroWidthEndsAndDoubleSidedQuads()
    {
        Vector3[] path = [Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2];
        var ribbon = LightningRibbon.Build(path);
        Assert.Equal(8, ribbon.Vertices.Count);
        Assert.Equal(ribbon.Vertices[0], ribbon.Vertices[1]);
        Assert.Equal(ribbon.Vertices[6], ribbon.Vertices[7]);
        Assert.InRange(Vector3.Distance(ribbon.Vertices[2], ribbon.Vertices[3]), .05999f, .06001f);
        Assert.Equal(24, ribbon.Triangles.Count);
        Assert.Equal(Vector2.Zero, ribbon.Uvs[0]);
        Assert.Equal(Vector2.UnitX, ribbon.Uvs[6]);
    }

    [Fact]
    public void RibbonOffsetsStayInViewXyWithDepthKeptAtEveryNode()
    {
        Vector3[] nodes = [new Vector3(0, 0, 1), new Vector3(1, 0, 2), new Vector3(2, 1, 4)];
        var ribbon = LightningRibbon.Build(nodes);
        Assert.Equal(2f, ribbon.Vertices[2].Z);
        Assert.Equal(2f, ribbon.Vertices[3].Z);
    }

    [Fact]
    public void AnAlmostReversedRibbonUsesThePreviousNormalInsteadOfCollapsing()
    {
        Vector3[] nodes = [new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0, 0, 1)];
        var ribbon = LightningRibbon.Build(nodes);
        Assert.InRange(Vector3.Distance(ribbon.Vertices[2], ribbon.Vertices[3]), .05999f, .06001f);
    }

    [InstallFact]
    public void TheShippedProceduralKindsAreDecodedFromTheTypeBlock()
    {
        GameDatabase database = DK2.Data.GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Assert.Equal(ProceduralArtKind.Lightning, database.Shots[21].MeshResource.ProceduralKind);
        Assert.Equal(0, database.Shots[21].Mass);
        Assert.Equal(ProceduralArtKind.Lightning, database.Shots[55].MeshResource.ProceduralKind);
        foreach (byte id in new byte[] { 30, 59, 63 })
        {
            Assert.Equal(ProceduralArtKind.TripleLightning, database.Shots[id].MeshResource.ProceduralKind);
        }

        Assert.Equal(ProceduralArtKind.FixedLightning, database.Effects[4].ArtResource.ProceduralKind);
        Assert.Equal(ProceduralArtKind.IceRing, database.Effects[28].ArtResource.ProceduralKind);
        using var art = DK2.Art.InstallArtProvider.Open(Dk2InstallGate.Path, database);
        RgbaImage lightning = art.LoadTexture("Lightning");
        Assert.InRange(lightning.Width, 1, 4096);
        Assert.InRange(lightning.Height, 1, 4096);
        Assert.Equal(lightning.Width * lightning.Height * 4, lightning.Pixels.Length);
    }

    [Fact]
    public void ProceduralEffectsRemainDrawableUntilTheirNormalTeardown()
    {
        byte[] data = new byte[12]; data[0] = 8;
        var effect = new EffectDef([])
        {
            Name = "bolt", EffectId = 4, ArtResource = new KwdArtResource("bolt", 0, data, 7, 0, 0, 0),
            Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = [], MinHp = 2, MaxHp = 2,
        };
        var model = new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables(), effects: [effect]),
            1, () => default, floorHeight: _ => default);
        model.Request(new ViewEffectRequest(new EffectId(4), PlayerIds.FirstKeeper, Point(2, 3, 5)));
        var drawn = new List<ProceduralEffectView>();
        model.CollectProceduralEffects(drawn);
        Assert.Equal(ProceduralArtKind.FixedLightning, Assert.Single(drawn).Kind);
        model.AdvanceTo(new EffectTime(51));
        model.CollectProceduralEffects(drawn);
        Assert.Single(drawn);
        model.AdvanceTo(new EffectTime(101));
        model.CollectProceduralEffects(drawn);
        Assert.Empty(drawn);
    }

    [Fact]
    public void LightningEventMetadataArmsOnlyTheRequestedCreaturesViewTimer()
    {
        var model = new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables()), 1, () => default);
        model.Emit(new SimEvent(new GameTick(10), "status", "lightning", "") { ElectrifiedCreature = new CreatureId(5) });
        Assert.True(model.ChooseCreatureCrackle(new CreatureId(5), new GameTick(11), animationInstalled: true));
        Assert.False(model.ChooseCreatureCrackle(new CreatureId(6), new GameTick(11), animationInstalled: true));
        Assert.False(model.ChooseCreatureCrackle(new CreatureId(5), new GameTick(12), animationInstalled: false));
    }

    private static FpPosition3 Point(int x, int y, int height)
    {
        return new FpPosition3(new Fp32(x * Fp32.One), new Fp32(y * Fp32.One), new Fp32(height * Fp32.One));
    }
}
