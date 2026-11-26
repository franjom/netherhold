// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class KeyScriptTests
{
    [Fact]
    public void TapsKeepTheirFramesAndNamesAndMayShareAFrame()
    {
        KeyScript script = KeyScript.Parse("30:H; 30:P;90:Escape")!;
        Assert.Equal([new KeyScriptKey(30, "H"), new KeyScriptKey(30, "P"), new KeyScriptKey(90, "Escape")], script.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("30")]
    [InlineData("30:")]
    [InlineData("30:H:x")]
    [InlineData("-1:H")]
    [InlineData("30:H;20:P")]
    [InlineData("30:Key-H")]
    [InlineData("x:H")]
    public void AMalformedScriptIsNone(string? text) => Assert.Null(KeyScript.Parse(text));

    [Fact]
    public void TheFirstTapAtOrAfterAFrameWalksForwardFromTheLastAnswer()
    {
        KeyScript script = KeyScript.Parse("10:A;10:B;20:C")!;
        Assert.Equal(0, script.FirstAtOrAfter(5));
        Assert.Equal(0, script.FirstAtOrAfter(10));
        Assert.Equal(2, script.FirstAtOrAfter(11, start: 1));
        Assert.Equal(3, script.FirstAtOrAfter(21, start: 2));
    }
}
