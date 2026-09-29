// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// DK2's own generator, CONFIRMED from the binary (docs/research/dk2-random.md):
/// a 32-bit LCG whose output is rotated right 13 bits, then reduced modulo n.
/// Built but not the default (ADR-0275); select it with
/// <c>new SimRandom(new Dk2Lcg())</c>.
/// </summary>
public sealed class Dk2Lcg : IRandomAlgorithm
{
    /// <summary>DK2 sets the world seed to 1 when a level starts.</summary>
    public const uint LevelStartSeed = 1;

    private const uint Multiplier = 0x0019660D;
    private const uint Increment = 0x3C6EF35F;

    public Dk2Lcg(uint seed = LevelStartSeed)
    {
        Seed = seed;
    }

    /// <summary>The raw 32-bit state: the value DK2 keeps in its world record.</summary>
    public uint Seed { get; private set; }

    public static uint Step(uint seed)
    {
        return unchecked(seed * Multiplier + Increment);
    }

    /// <summary>
    /// DK2 treats 0 as a caller error: it logs, returns 0 and does not advance
    /// the seed, so that is reproduced. DK2's argument is unsigned; a negative
    /// one here is our bug, not a port of a wrapped subtraction, so it throws.
    /// </summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax < 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        if (exclusiveMax == 0) return 0;
        Seed = Step(Seed);
        uint rotated = (Seed >> 13) | (Seed << 19);
        return (int)(rotated % (uint)exclusiveMax);
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Seed);
    }

    /// <summary>
    /// How many draws take <paramref name="from"/> to <paramref name="to"/>, or -1
    /// if more than <paramref name="maxSteps"/>. Reads a capture's per-tick seeds
    /// as a count of the original's draws per tick.
    /// </summary>
    public static int StepsBetween(uint from, uint to, int maxSteps)
    {
        uint s = from;
        for (int i = 0; i <= maxSteps; i++)
        {
            if (s == to) return i;
            s = Step(s);
        }
        return -1;
    }
}
