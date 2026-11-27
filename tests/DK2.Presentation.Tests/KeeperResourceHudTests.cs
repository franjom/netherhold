// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free resource strip (ADR-0805). DK2 lays it out in floored screen pixels, so the
/// expectations are pixels: at 1080 lines, V(c) = floor(c * 1080 / 1920).</summary>
public sealed class KeeperResourceHudTests
{
    private static readonly HudScreen FullHd = new HudScreen(1920, 1080);

    private static KeeperResourceSnapshot Snapshot(int gold = 0, int mana = 0, int income = 0, int upkeep = 0,
        HudGaugeFraction heart = default)
    {
        return new KeeperResourceSnapshot(new GoldAmount(gold), new ManaAmount(mana), new ManaPerSecond(income), new ManaPerSecond(upkeep), heart);
    }

    private static HudDraw[] Build(KeeperResourceHud hud, KeeperResourceSnapshot snapshot, HudScreen? screen = null)
    {
        var frame = new HudFrame();
        hud.Build(screen ?? FullHd, snapshot, 0, KeeperResourceFlash.None, frame);
        return frame.Draws.ToArray();
    }

    /// <summary>A rect back in the screen's local pixels, rounded off the float round trip.</summary>
    private static (int X, int Y, int W, int H) Pixels(HudRect r, HudScreen? screen = null)
    {
        float s = (screen ?? FullHd).Scale;
        return ((int)Math.Round(r.X * s), (int)Math.Round(r.Y * s), (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
    }

    [Fact]
    public void ThePanelsSitOnFlooredScreenPixels()
    {
        HudDraw[] draws = Build(new KeeperResourceHud(), Snapshot());
        // dx = y = 6, height = 69 - 6, mana width 126, gold from 290 and 416 - 290 wide.
        Assert.Equal((24, 6, 126, 63), Pixels(draws[0].Rect));
        Assert.Equal((150, 6, 126, 63), Pixels(draws[1].Rect));
        Assert.Equal((314, 6, 126, 63), Pixels(draws[2].Rect));
        Assert.Equal((440, 6, 126, 63), Pixels(draws[3].Rect));
        HudDraw heart = draws.Single(d => d.Image.Path == "GUI\\Windows\\heart-bg");
        Assert.Equal((0, 3, 90, 90), Pixels(heart.Rect));
    }

    [Fact]
    public void TheGaugeRingDrawsUnderItsFrameAndCarriesTheFraction()
    {
        var health = new HudGaugeFraction(3, 4);
        HudDraw[] draws = Build(new KeeperResourceHud(), Snapshot(heart: health));
        int ring = Array.FindIndex(draws, d => d.Image.Path == "GUI\\Windows\\heart-hp");
        Assert.True(ring < Array.FindIndex(draws, d => d.Image.Path == "GUI\\Windows\\heart-bg"));
        Assert.Equal(HudImageVariant.Gauge, draws[ring].Image.Variant);
        Assert.Equal(health, draws[ring].Image.Gauge);
        Assert.Equal(new HudImage("GUI\\Windows\\heart-hp", HudImageVariant.Gauge), draws[ring].Image.Slot);
    }

    [Fact]
    public void ManaIsRightAlignedInSevenSlotsInTheLargeFamily()
    {
        HudDraw[] mana = [.. Build(new KeeperResourceHud(), Snapshot(mana: 1234)).Where(d => d.Tint == new HudColor(0, 252, 156))];
        Assert.Equal(["GUI\\Icons\\i-1", "GUI\\Icons\\i-2", "GUI\\Icons\\i-3", "GUI\\Icons\\i-4"], mana.Select(d => d.Image.Path));
        // V(172) = 96, three empty slots of V(40) = 22.
        Assert.Equal((162, 18, 27, 27), Pixels(mana[0].Rect));
        Assert.Equal(184, Pixels(mana[1].Rect).X);
    }

    [Fact]
    public void ANegativeGoldNumberUsesTheMinusSignGlyph()
    {
        HudDraw[] gold = [.. Build(new KeeperResourceHud(), Snapshot(gold: -5)).Where(d => d.Tint == new HudColor(238, 182, 0))];
        Assert.Equal([KeeperResourceHud.MinusSign, "GUI\\Icons\\i-5"], gold.Select(d => d.Image.Path));
    }

    [Fact]
    public void TheRatesCarryTheirIconAndSignAndNeedAScreenWiderThan600()
    {
        HudDraw[] income = [.. Build(new KeeperResourceHud(), Snapshot(income: 12)).Where(d => d.Tint == new HudColor(50, 255, 50))];
        Assert.Equal(["GUI\\Icons\\Mana\\s-in", "GUI\\Icons\\Mana\\s-+", "GUI\\Icons\\Mana\\s-1", "GUI\\Icons\\Mana\\s-2"],
            income.Select(d => d.Image.Path));

        var narrow = new HudScreen(600, 450);
        HudDraw[] small = Build(new KeeperResourceHud(), Snapshot(mana: 7, income: 12), narrow);
        Assert.DoesNotContain(small, d => d.Image.Path!.StartsWith(KeeperResourceHud.RateDigits));
        Assert.Contains(small, d => d.Image.Path == KeeperResourceHud.SmallDigits + "7");
    }

    [Fact]
    public void EveryImageTheStripDrawsIsPreloadable()
    {
        var preload = KeeperResourceHud.Images().ToHashSet();
        HudDraw[] draws = Build(new KeeperResourceHud(), Snapshot(gold: -1234567890, mana: 1234567890, income: -98, upkeep: 76,
            heart: new HudGaugeFraction(1, 2)));
        // A live gauge is preloaded by its slot; the fraction is filled on the first frame.
        Assert.All(draws, d => Assert.Contains(d.Image.Slot, preload));
    }

    [Fact]
    public void AHiddenStripDrawsNothingAndClaimsNoPointer()
    {
        var hud = new KeeperResourceHud();
        Assert.True(hud.ContainsPointer(FullHd, 100, 100));
        hud.SetVisibility(new HudVisibility(ShowNormalHud: false, AllowOrdinaryInput: false));
        Assert.Empty(Build(hud, Snapshot()));
        Assert.False(hud.ContainsPointer(FullHd, 100, 100));
        Assert.Equal(KeeperResourceKind.None, hud.ResourceAt(FullHd, 100, 100));
    }

    [Fact]
    public void AGaugeLightsClockwiseFromTwelveAndBlacksOutTheRest()
    {
        byte[] source = [10, 20, 30, 200, 10, 20, 30, 201, 10, 20, 30, 202, 10, 20, 30, 203];
        byte[] quarter = HudImagePixels.Apply(source, 2, 2, new HudImage("g", HudImageVariant.Gauge, Gauge: new HudGaugeFraction(1, 4)));
        // Top-down: top-left, top-right, bottom-left, bottom-right. Only the top-right quarter is lit.
        Assert.Equal(new byte[] { 0, 0, 0, 200, 10, 20, 30, 201, 0, 0, 0, 202, 0, 0, 0, 203 }, quarter);
        Assert.Equal(source, HudImagePixels.Apply(source, 2, 2, new HudImage("g", HudImageVariant.Gauge, Gauge: new HudGaugeFraction(4, 4))));
    }
}
