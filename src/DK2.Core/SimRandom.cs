// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// The simulation's only source of randomness. Explicitly seeded and
/// platform-independent. The generator is pluggable (ADR-0275): the default is
/// <see cref="Xoshiro256StarStar"/>; <see cref="Dk2Lcg"/> is the original's.
/// </summary>
public sealed class SimRandom
{
    private readonly IRandomAlgorithm _algorithm;

    public SimRandom(ulong seed) : this(new Xoshiro256StarStar(seed))
    {
    }

    public SimRandom(IRandomAlgorithm algorithm)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
    }

    public IRandomAlgorithm Algorithm => _algorithm;

    public ulong NextUInt64()
    {
        return Xoshiro().NextUInt64();
    }

    /// <summary>Integer in [0, exclusiveMax), reduced as the algorithm defines.</summary>
    public int NextInt(int exclusiveMax)
    {
        return _algorithm.NextInt(exclusiveMax);
    }

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int NextInt(int min, int maxInclusive)
    {
        if (maxInclusive < min) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
        return min + NextInt(maxInclusive - min + 1);
    }

    public void HashInto(ref Hash64 hash)
    {
        _algorithm.HashInto(ref hash);
    }

    /// <summary>
    /// The xoshiro generator's full state. Another algorithm refuses rather than
    /// hand back a state that restores as the wrong generator; the snapshot
    /// tags the algorithm and saves <see cref="Dk2Lcg.Seed"/> itself (ADR-0564).
    /// </summary>
    public (ulong S0, ulong S1, ulong S2, ulong S3) SaveState()
    {
        return Xoshiro().SaveState();
    }

    public static SimRandom Restore((ulong S0, ulong S1, ulong S2, ulong S3) state)
    {
        return new(Xoshiro256StarStar.Restore(state));
    }

    private Xoshiro256StarStar Xoshiro()
    {
        return _algorithm as Xoshiro256StarStar
        ?? throw new NotSupportedException(
            $"{_algorithm.GetType().Name} has no 64-bit draw and no xoshiro state (ADR-0275).");
    }
}
