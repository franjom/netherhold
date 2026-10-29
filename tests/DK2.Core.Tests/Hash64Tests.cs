// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

public class Hash64Tests
{
    [Fact]
    public void SameInput_ProducesSameHash() =>
        Assert.Equal(HashOf("dungeon", 42), HashOf("dungeon", 42));

    [Fact]
    public void DifferentInput_ProducesDifferentHash()
    {
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("dungeon", 43));
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("keeper", 42));
    }

    [Fact]
    public void OrderMatters()
    {
        Hash64 a = Hash64.Start(); a.Add(1); a.Add(2);
        Hash64 b = Hash64.Start(); b.Add(2); b.Add(1);
        Assert.NotEqual(a.Value, b.Value);
    }

    /// <summary>A round that left zero at zero would make a run of zeros invisible.
    /// The round's added constant prevents that fixed point.</summary>
    [Fact]
    public void TheLengthOfARunOfZeros_Matters()
    {
        Hash64 two = Hash64.Start(); two.Add(0UL); two.Add(0UL);
        Hash64 three = Hash64.Start(); three.Add(0UL); three.Add(0UL); three.Add(0UL);
        Assert.NotEqual(two.Value, three.Value);
    }

    [Fact]
    public void NullString_DiffersFromEmptyString()
    {
        Hash64 a = Hash64.Start(); a.Add((string?)null);
        Hash64 b = Hash64.Start(); b.Add(string.Empty);
        Assert.NotEqual(a.Value, b.Value);
    }

    /// <summary>Pins the algorithm, as Fnv1a64Tests pins FNV: a change here moves every state hash
    /// and needs a HashVersion bump.</summary>
    [Fact]
    public void KnownVector_IsPinned()
    {
        Hash64 h = Hash64.Start();
        h.Add(0x0123456789ABCDEFUL);
        h.Add((byte)'a');
        Assert.Equal(0x66DCD29D88BC18A6UL, h.Value);
    }

    private static ulong HashOf(string s, int n)
    {
        Hash64 h = Hash64.Start(); h.Add(s); h.Add(n);
        return h.Value;
    }
}
