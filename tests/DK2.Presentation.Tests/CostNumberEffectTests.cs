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

    /// <summary>Owner-confirmed presentation, not DK2's draw (ADR-0663): the digits' RGB is 0,0,0 and the
    /// owner sees them in their art's own colour, so ours draws the art untinted.</summary>
    [Fact]
    public void TheDigitsAreDrawnUntintedAsTheOwnerSeesThem()
    {
        var model = Model();
        model.Request(Amount(42));
        model.AdvanceTo(new(51));
        Assert.All(model.Particles, d => Assert.Equal((255, 255, 255), (d.Red, d.Green, d.Blue)));
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
