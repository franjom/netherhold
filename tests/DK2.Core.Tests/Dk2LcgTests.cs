// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

/// <summary>
/// Known answers for DK2's generator (docs/research/dk2-random.md), computed by
/// an independent Python implementation of the formula read from the binary.
/// </summary>
public class Dk2LcgTests
{
    [Fact]
    public void FromLevelStart_SeedAdvancesByTheLcg()
    {
        var rng = new Dk2Lcg();
        uint[] expected = [0x3C88596C, 0x5E8885DB, 0x8116017E, 0xB4733AC5, 0x0CF06D60];
        foreach (uint seed in expected)
        {
            rng.NextInt(100);
            Assert.Equal(seed, rng.Seed);
        }
    }

    [Fact]
    public void FromLevelStart_DrawsRotateThenReduce()
    {
        var rng = new Dk2Lcg();
        int[] draws = [.. Enumerable.Range(0, 8).Select(_ => rng.NextInt(100))];
        Assert.Equal([74, 16, 84, 25, 11, 6, 58, 58], draws);
    }

    [Fact]
    public void FromLevelStart_CoinFlipsReadBit13()
    {
        var rng = new Dk2Lcg();
        int[] flips = [.. Enumerable.Range(0, 16).Select(_ => rng.NextInt(2))];
        Assert.Equal([0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 1, 0, 1, 1], flips);
    }

    [Fact]
    public void AThousandDraws_LandOnTheKnownSeed()
    {
        var rng = new Dk2Lcg();
        for (int i = 0; i < 1000; i++) rng.NextInt(7);
        Assert.Equal(0x26799AA9u, rng.Seed);
    }

    [Fact]
    public void ZeroRange_ReturnsZeroWithoutAdvancing()
    {
        var rng = new Dk2Lcg(12345);
        Assert.Equal(0, rng.NextInt(0));
        Assert.Equal(12345u, rng.Seed);
    }

    [Fact]
    public void NegativeRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dk2Lcg().NextInt(-1));
    }

    [Fact]
    public void StepsBetween_CountsDraws()
    {
        uint from = Dk2Lcg.LevelStartSeed;
        uint to = from;
        for (int i = 0; i < 37; i++) to = Dk2Lcg.Step(to);
        Assert.Equal(37, Dk2Lcg.StepsBetween(from, to, 100));
        Assert.Equal(0, Dk2Lcg.StepsBetween(from, from, 100));
        Assert.Equal(-1, Dk2Lcg.StepsBetween(from, to, 36));
    }

    [Fact]
    public void ThroughSimRandom_DrawsTheSameSequence()
    {
        var direct = new Dk2Lcg();
        var wrapped = new SimRandom(new Dk2Lcg());
        for (int i = 0; i < 100; i++)
            Assert.Equal(direct.NextInt(1 + i), wrapped.NextInt(1 + i));
    }

    [Fact]
    public void ThroughSimRandom_RefusesTheXoshiroOnlySnapshot()
    {
        var rng = new SimRandom(new Dk2Lcg());
        Assert.Throws<NotSupportedException>(() => rng.SaveState());
        Assert.Throws<NotSupportedException>(() => rng.NextUInt64());
    }

    [Fact]
    public void Default_IsStillXoshiro()
    {
        // The default must stay bit-identical to the pre-ADR-0275 generator,
        // or every recorded state hash moves.
        var viaSeed = new SimRandom(12345);
        var viaAlgorithm = new SimRandom(new Xoshiro256StarStar(12345));
        Assert.IsType<Xoshiro256StarStar>(viaSeed.Algorithm);
        for (int i = 0; i < 100; i++)
            Assert.Equal(viaAlgorithm.NextInt(1000), viaSeed.NextInt(1000));
    }
}
