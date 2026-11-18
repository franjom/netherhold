// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

public sealed class LaunchArgumentsTests
{
    [Fact]
    public void EachEngineReadsTheSameKeysUnderItsOwnPrefix()
    {
        var unity = LaunchArguments.Parse(["player.exe", "-level", "Level6a", "-seed", "42", "-dk2Install", "E:/DK2"], "-");
        Assert.Equal(new LaunchArguments("Level6a", 42UL, "E:/DK2"), unity);
        var godot = LaunchArguments.Parse(["--level", "Level2", "--seed", "7"], "--");
        Assert.Equal(new LaunchArguments("Level2", 7UL, null), godot);
        Assert.Equal(new LaunchArguments(null, null, null), LaunchArguments.Parse(["--level", "Level2"], "-"));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1e3")]
    [InlineData(" 5")]
    [InlineData("18446744073709551616")]
    public void ASeedThatIsNotAPlainDecimalLeavesTheHostsOwn(string text) =>
        Assert.Null(LaunchArguments.Parse(["-seed", text], "-").Seed);

    [Fact]
    public void AValueIsTheArgumentAfterTheFirstKey()
    {
        Assert.Equal("a", LaunchArguments.Value(["-x", "a", "-x", "b"], "-x"));
        Assert.Null(LaunchArguments.Value(["a", "-x"], "-x"));
        Assert.Null(LaunchArguments.Value([], "-x"));
    }
    [Fact]
    public void AGlideTargetIsTwoPlainDecimalsAfterTheKey()
    {
        Assert.Equal(new TilePos(12, 40), LaunchArguments.GlideTarget(["--glide-to", "12,40"], "--"));
        Assert.Equal(new TilePos(0, 7), LaunchArguments.GlideTarget(["-glide-to", "0,7", "-glide-to", "1,1"], "-"));
        Assert.Null(LaunchArguments.GlideTarget(["--glide-to", "12,40"], "-"));
        Assert.Null(LaunchArguments.GlideTarget(["--glide-to"], "--"));
        Assert.Null(LaunchArguments.GlideTarget([], "--"));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12,")]
    [InlineData(",40")]
    [InlineData("-1,4")]
    [InlineData("1,+4")]
    [InlineData("1, 4")]
    [InlineData("1,4,5")]
    [InlineData("1.5,4")]
    [InlineData("2147483648,4")]
    public void AGlideTargetThatIsNotTwoPlainDecimalsIsNone(string text) =>
        Assert.Null(LaunchArguments.GlideTarget(["--glide-to", text], "--"));

    [Fact]
    public void PossessFirstCreatureIsItsKeyUnderTheEnginesPrefix()
    {
        Assert.True(LaunchArguments.PossessFirstCreature(["--level", "Level1", "--possess-first-creature"], "--"));
        Assert.True(LaunchArguments.PossessFirstCreature(["-possess-first-creature"], "-"));
        Assert.False(LaunchArguments.PossessFirstCreature(["--possess-first-creature"], "-"));
        Assert.False(LaunchArguments.PossessFirstCreature(["--possess-first-creatures", "possess-first-creature"], "--"));
        Assert.False(LaunchArguments.PossessFirstCreature([], "--"));
    }

    [Fact]
    public void APointerPathIsItsKeysInFrameOrder()
    {
        PointerScript script = LaunchArguments.PointerPath(["--level", "Level1", "--pointer-path", "0:10,20; 30:110.5,220:L;31:110.5,220:lR;40:0,0:r"], "--")!;
        Assert.Equal(new[]
        {
            new PointerScriptKey(0, 10, 20, PointerScriptButtons.None),
            new PointerScriptKey(30, 110.5f, 220, PointerScriptButtons.PrimaryDown),
            new PointerScriptKey(31, 110.5f, 220, PointerScriptButtons.PrimaryUp | PointerScriptButtons.SecondaryDown),
            new PointerScriptKey(40, 0, 0, PointerScriptButtons.SecondaryUp),
        }, script.Keys);
        Assert.Null(LaunchArguments.PointerPath(["--pointer-path", "0:1,2"], "-"));
        Assert.Null(LaunchArguments.PointerPath(["--pointer-path"], "--"));
        Assert.Null(LaunchArguments.PointerPath([], "--"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0:1")]
    [InlineData("0:1,2,3")]
    [InlineData("0:1,2:")]
    [InlineData("0:1,2:X")]
    [InlineData("0:1,2:LL")]
    [InlineData("0:1,2:L:l")]
    [InlineData("-1:1,2")]
    [InlineData("+1:1,2")]
    [InlineData("5:1,2;5:3,4")]
    [InlineData("5:1,2;4:3,4")]
    [InlineData("0:-1,2")]
    [InlineData("0:1e3,2")]
    [InlineData("0:1,2;")]
    [InlineData("x:1,2")]
    [InlineData("0:1000000000000000000000000000000000000000000,2")]
    [InlineData("0:1,9999999999999999999999999999999999999999999999")]
    public void APointerPathWithAMalformedKeyIsNone(string text) => Assert.Null(LaunchArguments.ParsePointerPath(text));

    [Fact]
    public void AScriptedPointerMovesInLinesAndChangesButtonsOnItsFrames()
    {
        PointerScript script = LaunchArguments.ParsePointerPath("10:100,100;20:200,50:L;22:200,50:l;30:0,0:R")!;
        Assert.Equal(new PointerScriptSample(new(100, 100), PointerScriptButtons.None, false, false), script.At(0));
        Assert.Equal(new PointerScriptSample(new(150, 75), PointerScriptButtons.None, false, false), script.At(15));
        PointerScriptSample press = script.At(20);
        Assert.True(press.PrimaryDown && press.PrimaryHeld && !press.PrimaryUp);
        Assert.Equal(new Vector2(200, 50), press.Position);
        Assert.True(script.At(21).PrimaryHeld && script.At(21).Changes == PointerScriptButtons.None);
        PointerScriptSample release = script.At(22);
        Assert.True(release.PrimaryUp && !release.PrimaryHeld);
        PointerScriptSample secondary = script.At(30);
        Assert.True(secondary.SecondaryDown && secondary.SecondaryHeld && !secondary.SecondaryUp);
        Assert.Equal(new PointerScriptSample(Vector2.Zero, PointerScriptButtons.None, false, true), script.At(400));
        Assert.Throws<ArgumentException>(() => new PointerScript([new PointerScriptKey(3, 0, 0, 0), new PointerScriptKey(3, 1, 1, 0)]));
        Assert.Throws<ArgumentException>(() => new PointerScript([]));
    }

    [Fact]
    public void ADownAndUpOnOneFrameAreBothChangesAndTheDownIsTakenFirst()
    {
        PointerScript script = LaunchArguments.ParsePointerPath("0:1,1:Ll;5:1,1:lL;9:1,1:LR")!;
        PointerScriptSample click = script.At(0);
        Assert.Equal(PointerScriptButtons.PrimaryDown | PointerScriptButtons.PrimaryUp, click.Changes);
        Assert.False(click.PrimaryHeld);
        Assert.False(script.At(5).PrimaryHeld);
        PointerScriptSample both = script.At(9);
        Assert.True(both.PrimaryDown && both.SecondaryDown && both.PrimaryHeld && both.SecondaryHeld);
        Assert.True(script.At(10).PrimaryHeld && script.At(10).SecondaryHeld && script.At(10).Changes == PointerScriptButtons.None);
    }

    [Fact]
    public void ASampleAllocatesNothing()
    {
        PointerScript script = LaunchArguments.ParsePointerPath("10:100,100;20:200,50:L;22:200,50:l")!;
        script.At(15);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 40; frame++) script.At(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
