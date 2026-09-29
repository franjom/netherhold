// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// xoshiro256** (public domain, Blackman &amp; Vigna): the simulation's default
/// generator. Chosen for our determinism requirement, not to match DK2; the
/// original's generator is <see cref="Dk2Lcg"/> (ADR-0275).
/// </summary>
public sealed class Xoshiro256StarStar : IRandomAlgorithm
{
    private ulong _s0, _s1, _s2, _s3;

    public Xoshiro256StarStar(ulong seed)
    {
        // SplitMix64 expansion of the seed, per xoshiro authors' recommendation.
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1; // all-zero state is invalid
    }

    private Xoshiro256StarStar(ulong s0, ulong s1, ulong s2, ulong s3)
        => (_s0, _s1, _s2, _s3) = (s0, s1, s2, s3);

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong v, int k) => (v << k) | (v >> (64 - k));

    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [0, exclusiveMax). Rejection-sampled — no modulo bias.</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        ulong bound = (ulong)exclusiveMax;
        ulong threshold = (0UL - bound) % bound;
        while (true)
        {
            ulong r = NextUInt64();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(_s0);
        hash.Add(_s1);
        hash.Add(_s2);
        hash.Add(_s3);
    }

    public (ulong S0, ulong S1, ulong S2, ulong S3) SaveState() => (_s0, _s1, _s2, _s3);

    public static Xoshiro256StarStar Restore((ulong S0, ulong S1, ulong S2, ulong S3) state)
        => new(state.S0, state.S1, state.S2, state.S3);
}
