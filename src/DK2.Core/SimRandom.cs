// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace DK2.Core;

/// <summary>
/// The simulation's only source of randomness. Explicitly seeded, fully
/// serialisable, platform-independent.
///
/// Algorithm: xoshiro256** (public domain, Blackman &amp; Vigna). Chosen for our
/// determinism requirement; it deliberately does NOT try to match DK2's original
/// RNG. If behavioural fidelity work later requires reproducing the original
/// generator (UNKNOWN which one DK2 uses), that becomes a second implementation
/// behind this same type — see docs/research/open-questions.md.
/// </summary>
public sealed class SimRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public SimRandom(ulong seed)
    {
        // SplitMix64 expansion of the seed, per xoshiro authors' recommendation.
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1; // all-zero state is invalid
    }

    private SimRandom(ulong s0, ulong s1, ulong s2, ulong s3)
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

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int NextInt(int min, int maxInclusive)
    {
        if (maxInclusive < min) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
        return min + NextInt(maxInclusive - min + 1);
    }

    public bool NextBool() => (NextUInt64() & 1) != 0;

    /// <summary>Captures the full generator state for save/load.</summary>
    public (ulong S0, ulong S1, ulong S2, ulong S3) SaveState() => (_s0, _s1, _s2, _s3);

    public static SimRandom Restore((ulong S0, ulong S1, ulong S2, ulong S3) state)
        => new(state.S0, state.S1, state.S2, state.S3);
}
