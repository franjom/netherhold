// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
namespace DK2.Presentation.Tests;

public sealed class ScreenColourTests
{
    [Theory]
    [InlineData(ScreenColourKind.Vampire)]
    [InlineData(ScreenColourKind.Damage)]
    [InlineData(ScreenColourKind.Death)]
    public void UnestablishedGammaCapabilityDoesNotApplyColourChanges(ScreenColourKind kind)
    {
        var colours = new PossessionColours();
        colours.Change.Start(kind, 0, 0);
        Assert.Equal(ScreenColourTables.Build(ScreenColourKind.Normal, 1), colours.Output);
        colours.Change.SetGamma(2);
        colours.Change.Start(kind, 0, 0);
        Assert.Equal(ScreenColourTables.Build(ScreenColourKind.Normal, 2), colours.Output);
    }

    [Fact]
    public void EnabledOutputUsesTheRunningRampAndCanBeDisabledWithoutDiscardingIt()
    {
        var colours = new PossessionColours { ApplyColourChanges = true };
        colours.Change.Start(ScreenColourKind.Damage, 0, 125);
        colours.Change.Queue(ScreenColourKind.Vampire, 250);
        colours.Step(60);
        Assert.Equal(colours.Change.Current, colours.Output);
        colours.ApplyColourChanges = false;
        Assert.Equal(ScreenColourTables.Build(ScreenColourKind.Normal, 1), colours.Output);
        Assert.True(colours.Change.Running);
        Assert.Equal(1, colours.Change.QueuedCount);
        colours.ApplyColourChanges = true;
        Assert.Equal(colours.Change.Current, colours.Output);
    }

    [Fact]
    public void ChangingGammaSynchronizesTheTargetUsedByUnknownKinds()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Damage, 0, 0);
        colours.SetGamma(2);
        ushort[] expected = ScreenColourTables.Build(ScreenColourKind.Normal, 2);
        Assert.Equal(expected, colours.Current);
        colours.Start((ScreenColourKind)16, 100, 0);
        Assert.Equal(expected, colours.Current);
    }
    [Fact]
    public void AnUnchangedGammaPreservesTheCurrentBlendAndQueue()
    {
        var colours = new ScreenColourChange();
        Assert.Equal(ScreenColourTables.Build(ScreenColourKind.Normal, 1), colours.Current);
        colours.Start(ScreenColourKind.Damage, 0, 100);
        colours.Queue(ScreenColourKind.Black, 100);
        colours.Step(50);
        ushort[] before = [.. colours.Current];
        colours.SetGamma(1);
        Assert.True(colours.Running);
        Assert.Equal(1, colours.QueuedCount);
        Assert.Equal(before, colours.Current);
        colours.Step(100);
        Assert.Equal(65535, colours.Current[128]);
        colours.Step(200);
        Assert.All(colours.Current, value => Assert.Equal(0, value));
    }
    [Theory]
    [InlineData(ScreenColourKind.Normal, 32767, 32767, 32767)]
    [InlineData(ScreenColourKind.Vampire, 32767, 10922, 10922)]
    [InlineData(ScreenColourKind.DarkElf, 32767, 10922, 32767)]
    [InlineData(ScreenColourKind.Skeleton, 16383, 16383, 49151)]
    [InlineData(ScreenColourKind.Salamander, 65535, 32767, 32767)]
    [InlineData(ScreenColourKind.DarkAngel, 32767, 32767, 65535)]
    [InlineData(ScreenColourKind.White, 65535, 65535, 65535)]
    [InlineData(ScreenColourKind.Black, 0, 0, 0)]
    [InlineData(ScreenColourKind.Damage, 65535, 0, 0)]
    [InlineData(ScreenColourKind.Death, 16383, 0, 0)]
    [InlineData(ScreenColourKind.Blue, 0, 0, 49151)]
    [InlineData(ScreenColourKind.Maiden, 32767, 45874, 0)]
    public void TableKindsAtMiddle(ScreenColourKind kind, int red, int green, int blue)
    {
        ushort[] table = ScreenColourTables.Build(kind, 1);
        Assert.Equal(red, table[128]);
        Assert.Equal(green, table[256 + 128]);
        Assert.Equal(blue, table[512 + 128]);
    }
    [Fact]
    public void GammaUses256AndTheSameCurveInEveryKind()
    {
        Assert.Equal(65279, ScreenColourTables.Build(ScreenColourKind.Normal, 1)[255]);
        Assert.Equal((ushort)(Math.Pow(0.5, 0.5) * 65535), ScreenColourTables.Build(ScreenColourKind.Normal, 2)[128]);
        Assert.Equal(ScreenColourTables.Build(ScreenColourKind.Salamander, 1),
            ScreenColourTables.Build(ScreenColourKind.RedLift, 1));
    }
    [Fact]
    public void FalseColourBandsUseBrightnessAfterGamma()
    {
        ushort[] three = ScreenColourTables.Build(ScreenColourKind.ThreeBands, 1);
        Assert.Equal(0, three[32]);
        Assert.Equal(0, three[256 + 32]);
        Assert.InRange(three[512 + 32], 60500, 60600);
        ushort[] four = ScreenColourTables.Build(ScreenColourKind.FourBands, 1);
        ushort[] swapped = ScreenColourTables.Build(ScreenColourKind.FourBandsSwapped, 1);
        Assert.Equal(65535, four[96]);
        Assert.Equal(0, four[256 + 96]);
        Assert.Equal(four[96], swapped[256 + 96]);
        Assert.Equal(four[256 + 96], swapped[96]);
    }
    [Fact]
    public void InterruptStartsAtCurrentTableAndDiscardsQueue()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Damage, 100, 100);
        colours.Queue(ScreenColourKind.Black, 100);
        colours.Step(150);
        ushort halfway = colours.Current[128];
        Assert.Equal((32767 + 65535) / 2, halfway);
        colours.Start(ScreenColourKind.Normal, 150, 100);
        Assert.Equal(0, colours.QueuedCount);
        colours.Step(200);
        Assert.Equal((halfway + 32767) / 2, colours.Current[128]);
        colours.Step(250);
        Assert.False(colours.Running);
        Assert.Equal(32767, colours.Current[128]);
    }
    [Fact]
    public void QueueDropsNinthAndStartsNextAtTheFrameClock()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Damage, 0, 125);
        for (int i = 0; i < 8; i++)
        {
            Assert.True(colours.Queue(ScreenColourKind.Black, 250));
        }

        Assert.False(colours.Queue(ScreenColourKind.White, 250));
        colours.Step(200);
        Assert.True(colours.Running);
        Assert.Equal(7, colours.QueuedCount);
        Assert.Equal(65535, colours.Current[128]);
        colours.Step(325);
        Assert.Equal(32767, colours.Current[128]);
    }
    [Fact]
    public void ZeroLengthStartAppliesImmediately()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Black, 100, 0);
        Assert.False(colours.Running);
        Assert.All(colours.Current, value => Assert.Equal(0, value));
    }
    [Fact]
    public void UnknownKindsRetainThePreviousTarget()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Black, 0, 100);
        colours.Start((ScreenColourKind)16, 0, 0);
        Assert.All(colours.Current, value => Assert.Equal(0, value));
    }
    [Fact]
    public void DeveloperOnlyQueuedZeroLengthMirrorsNativeDivisionFaultRatherThanSilentlyRepairingIt()
    {
        var colours = new ScreenColourChange();
        colours.Start(ScreenColourKind.Black, 0, 100);
        colours.Queue(ScreenColourKind.White, 0);
        colours.Step(100);
        Assert.Throws<DivideByZeroException>(() => colours.Step(101));
    }
    [Fact]
    public void SessionEventsSelectNativeDurationsAndKeepDeathQueue()
    {
        var driver = new PossessionColours();
        driver.Enter(ScreenColourKind.Vampire, 1000, 3000);
        driver.Step(2743);
        Assert.False(driver.Change.Running);
        driver.Step(2744);
        Assert.True(driver.Change.Running);
        driver.Step(3000);
        Assert.Equal(10922, driver.Change.Current[384]);
        driver.Damage(3100);
        driver.Step(3225);
        Assert.Equal(65535, driver.Change.Current[128]);
        driver.Step(3475);
        Assert.Equal(10922, driver.Change.Current[384]);
        driver.Thaw(3500);
        driver.Step(4000);
        Assert.False(driver.Change.Running);
        driver.Death(4100);
        driver.Step(5600);
        Assert.Equal(16383, driver.Change.Current[128]);
        driver.Step(7100);
        Assert.All(driver.Change.Current, value => Assert.Equal(0, value));
        driver.Leave(8000);
        driver.Step(8256);
        Assert.Equal(32767, driver.Change.Current[128]);
    }
}
