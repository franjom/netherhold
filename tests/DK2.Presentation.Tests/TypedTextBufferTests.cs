// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class TypedTextBufferTests
{
    [Theory]
    [InlineData(TypedKey.Backspace, 0u, "\b")]
    [InlineData(TypedKey.Backspace, 8u, "\b")]
    [InlineData(TypedKey.Enter, 0u, "\n")]
    [InlineData(TypedKey.Enter, 13u, "\n")]
    [InlineData(TypedKey.Other, 0x41u, "A")]
    [InlineData(TypedKey.Other, 0x17Eu, "\u017E")]
    [InlineData(TypedKey.Other, 0x1F600u, "\uD83D\uDE00")]
    [InlineData(TypedKey.Other, 0u, "")]
    [InlineData(TypedKey.Other, 0xD800u, "")]
    [InlineData(TypedKey.Other, 0x110000u, "")]
    public void AKeyTypesItsCharacters(TypedKey key, uint codePoint, string expected)
    {
        var buffer = new TypedTextBuffer();
        Assert.True(buffer.Add(key, codePoint));
        Assert.Equal(expected, buffer.Text.ToString());
    }

    [Fact]
    public void BackspaceAndEnterOnTheKeypadOrMainBlockTypeTheirControlCharacters()
    {
        // Godot reports both Enter keys as TypedKey.Enter; neither carries a code point.
        var buffer = new TypedTextBuffer();
        buffer.Add(TypedKey.Other, 'a'); buffer.Add(TypedKey.Backspace, 0); buffer.Add(TypedKey.Enter, 0); buffer.Add(TypedKey.Enter, 0);
        Assert.Equal("a\b\n\n", buffer.Text.ToString());
    }

    [Fact]
    public void AFullFrameTakesNothingMoreUntilCleared()
    {
        var buffer = new TypedTextBuffer(3);
        Assert.True(buffer.Add(TypedKey.Other, 'a'));
        Assert.True(buffer.Add(TypedKey.Other, 'b'));
        Assert.True(buffer.Add(TypedKey.Other, 'c'));
        Assert.False(buffer.Add(TypedKey.Other, 'd'));
        Assert.True(buffer.Full);
        Assert.False(buffer.Add(TypedKey.Backspace, 0));
        Assert.Equal("abc", buffer.Text.ToString());
        buffer.Clear();
        Assert.False(buffer.Full);
        Assert.True(buffer.Add(TypedKey.Other, 'e'));
        Assert.Equal("e", buffer.Text.ToString());
    }

    [Fact]
    public void ASurrogatePairThatDoesNotFitLetsNoLaterCharacterIn()
    {
        var buffer = new TypedTextBuffer(3);
        buffer.Add(TypedKey.Other, 'a'); buffer.Add(TypedKey.Other, 'b');
        Assert.False(buffer.Add(TypedKey.Other, 0x1F600));
        Assert.False(buffer.Add(TypedKey.Other, 'c'));
        Assert.False(buffer.Add(TypedKey.Enter, 0));
        Assert.Equal("ab", buffer.Text.ToString());
    }

    [Fact]
    public void AFrameHoldsAtLeastASurrogatePair()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypedTextBuffer(1));
    }
}
