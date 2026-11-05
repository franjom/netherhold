// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Generation type 14 (<c>0x00439480</c>) and the viewer test of <c>0x00441060</c>
/// (floating-cost-numbers.md sections 1 and 4), on the shipped shape of effect 22 and its digits.</summary>
public class CostNumberEffectTests
{
    private const int DigitWidth = 1228;
    private static readonly PlayerId Viewer = PlayerIds.FirstKeeper;

    [Fact]
    public void EachDigitIsAnElementUnitsFirstSpacedOneDigitWidthLeftward()
    {
        var model = Model();
        model.Request(Amount(4800));
        model.AdvanceTo(new(51));
        ConstructionParticle[] digits = [.. model.Particles];
        Assert.Equal([27, 27, 35, 31], digits.Select(d => (int)d.Definition.EffectElementId));
        Assert.Equal([2 * DigitWidth, DigitWidth, 0, -DigitWidth], digits.Select(d => d.ScreenOffsetXRaw));
        Assert.All(digits, d => Assert.Equal((10000, 12000, 0), (d.XRaw, d.YRaw, d.HeadingRaw)));
    }

    /// <summary>The digits keep their RGB 0,0,0 (0x00435F20) and take their tile's lights, so they show
    /// only where lit, as the owner sees them (2026-11-05); other elements stay at their tint.</summary>
    [Fact]
    public void TheDigitsAreBlackTintedAndTakeLight()
    {
        var model = Model();
        model.Request(Amount(42));
        model.AdvanceTo(new(51));
        Assert.All(model.Particles, d =>
        {
            Assert.Equal((0, 0, 0), (d.Red, d.Green, d.Blue));
            Assert.True(d.TakesLight);
        });
    }

    [Fact]
    public void ALitSpriteIsItsTintPlusEachLightsRadialTermCappedPerChannel()
    {
        ClassicPointLight torch = ClassicPointLight.FromTerrain(new TilePos(2, 3), new Fp32(4096),
            new TerrainLightColour(200, 120, 40));
        Assert.Equal(((byte)200, (byte)120, (byte)40),
            SpriteLighting.Lit(0, 0, 0, [torch], torch.Position));
        Assert.Equal(((byte)255, (byte)240, (byte)80),
            SpriteLighting.Lit(0, 0, 0, [torch, torch], torch.Position));
        Assert.Equal(((byte)0, (byte)0, (byte)0),
            SpriteLighting.Lit(0, 0, 0, [torch], torch.Position + new System.Numerics.Vector3(5, 0, 0)));
        Assert.Equal(((byte)10, (byte)20, (byte)30), SpriteLighting.Lit(10, 20, 30, [], default));
    }

    [Fact]
    public void OtherEffectElementsDoNotTakeLight()
    {
        var wave = new EffectDef([])
        {
            Name = "wave", EffectId = 10, ArtResource = EmptyArt, Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0),
            GenerateIds = [20], GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1,
        };
        var spark = new EffectElementDef([])
        {
            Name = "spark", EffectElementId = 20, ArtResource = new("spark", 0, new byte[12], 2, 0, 0, 0),
            MinHp = 20, MaxHp = 20, MinScale = 4096, MaxScale = 4096,
        };
        var model = new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables(),
            effects: [wave], effectElements: [spark]), 7, () => default, floorHeight: _ => default);
        model.Request(new ViewEffectRequest(new(10), Viewer, new FpPosition3(new(10000), new(12000), new(0))));
        model.AdvanceTo(new(51));
        Assert.False(Assert.Single(model.Particles).TakesLight);
    }

    [Fact]
    public void AnAmountOffCameraIsNotShown()
    {
        var model = Model(cameraVisible: _ => false);
        model.Request(Amount(300));
        model.AdvanceTo(new(51));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void WithoutAViewerEveryPlayersAmountIsShown()
    {
        var model = Model(viewer: null);
        model.Request(Amount(300) with { Owner = new PlayerId(4) });
        model.AdvanceTo(new(51));
        Assert.Equal(3, model.Particles.Count);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(7, 1, 0)]
    [InlineData(150, 3, DigitWidth)]
    [InlineData(18750, 5, 2 * DigitWidth)]
    public void ZeroHasOneDigitAndTheUnitsDigitSitsHalfTheCountRight(int amount, int count, int unitsOffset)
    {
        var model = Model();
        model.Request(Amount(amount));
        model.AdvanceTo(new(51));
        Assert.Equal(count, model.Particles.Count);
        Assert.Equal(unitsOffset, model.Particles[0].ScreenOffsetXRaw);
    }

    [Fact]
    public void AnotherPlayersAmountIsNotShownToTheViewer()
    {
        var model = Model();
        model.Request(Amount(300) with { Owner = new PlayerId(4) });
        model.AdvanceTo(new(51));
        Assert.Empty(model.Particles);
    }

    [Fact]
    public void TheDigitsRiseAndVanishWholeAfterFortyEightUpdates()
    {
        var model = Model();
        model.Request(Amount(5));
        model.AdvanceTo(new(51));
        ConstructionParticle digit = Assert.Single(model.Particles);
        int born = digit.HeightRaw;
        // One 50 ms update at a time: an update moves once whatever quanta it covers (0x00436430).
        for (int update = 1; update <= 47; update++) model.AdvanceTo(new(51 + update * 50));
        Assert.Same(digit, Assert.Single(model.Particles));
        Assert.InRange(digit.HeightRaw - born, (int)(1.3 * 4096), (int)(1.45 * 4096));
        model.AdvanceTo(new(51 + 48 * 50));
        Assert.Empty(model.Particles);
    }

    private static ViewEffectRequest Amount(int amount) => new(new(CostNumberRules.GoldEffectId), Viewer,
        new FpPosition3(new(10000), new(12000), new(0))) { Amount = amount };

    private static readonly KwdArtResource EmptyArt = new("", 0, new byte[12], 0, 0, 0, 0);

    private static RoomConstructionParticles Model(Func<ViewEffectRequest, bool>? cameraVisible = null) =>
        Model(Viewer, cameraVisible);

    private static RoomConstructionParticles Model(PlayerId? viewer, Func<ViewEffectRequest, bool>? cameraVisible = null)
    {
        var effect = new EffectDef([])
        {
            Name = "Gold Cost", EffectId = CostNumberRules.GoldEffectId, ArtResource = EmptyArt,
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = [27], GenerationType = 14, Flags = 0x480,
            MinHp = 1, MaxHp = 1,
        };
        EffectElementDef[] digits = [.. Enumerable.Range(0, 10).Select(d => new EffectElementDef([])
        {
            Name = $"InGameDigit{d}", EffectElementId = (ushort)(27 + d),
            ArtResource = new($"InGameDigit{d}", 0, new byte[12], 2, 0, 0, 0),
            MinHp = 48, MaxHp = 48, MinScale = DigitWidth, MaxScale = DigitWidth,
            MinSpeedYz = 8192, MaxSpeedYz = 8192, AirFriction = 327,
        })];
        return new(new GameDatabase([], [], [], new GlobalVariables(), effects: [effect], effectElements: digits),
            7, () => default, floorHeight: _ => default, cameraVisible: cameraVisible, viewer: viewer);
    }
}
