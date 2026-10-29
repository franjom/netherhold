// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

public class Fnv1a64Tests
{
    [Fact]
    public void SameInput_ProducesSameHash()
    {
        Assert.Equal(HashOf("dungeon", 42), HashOf("dungeon", 42));
    }

    [Fact]
    public void DifferentInput_ProducesDifferentHash()
    {
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("dungeon", 43));
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("keeper", 42));
    }

    [Fact]
    public void OrderMatters()
    {
        Fnv1a64 a = Fnv1a64.Start();
        a.Add(1);
        a.Add(2);

        Fnv1a64 b = Fnv1a64.Start();
        b.Add(2);
        b.Add(1);

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void KnownVector_IsPinned()
    {
        // FNV-1a over the ASCII bytes of "a". Pinning it proves this is the
        // published algorithm rather than something that merely looks stable
        // in one process. https://en.wikipedia.org/wiki/Fowler-Noll-Vo_hash_function
        Fnv1a64 h = Fnv1a64.Start();
        h.Add((byte)'a');
        Assert.Equal(0xaf63dc4c8601ec8cUL, h.Value);
    }

    [Fact]
    public void EmptyHash_IsTheOffsetBasis()
    {
        Assert.Equal(14695981039346656037UL, Fnv1a64.Start().Value);
    }

    [Fact]
    public void NullString_DiffersFromEmptyString()
    {
        Fnv1a64 a = Fnv1a64.Start();
        a.Add((string?)null);

        Fnv1a64 b = Fnv1a64.Start();
        b.Add(string.Empty);

        Assert.NotEqual(a.Value, b.Value);
    }

    private static ulong HashOf(string s, int n)
    {
        Fnv1a64 h = Fnv1a64.Start();
        h.Add(s);
        h.Add(n);
        return h.Value;
    }
}
