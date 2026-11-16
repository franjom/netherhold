// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
}
