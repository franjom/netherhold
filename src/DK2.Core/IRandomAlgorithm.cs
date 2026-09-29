// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// A generator behind <see cref="SimRandom"/>. Each algorithm owns its own
/// reduction to a range, because that reduction is part of what makes two
/// generators' sequences differ (ADR-0275).
/// </summary>
public interface IRandomAlgorithm
{
    /// <summary>An integer in [0, exclusiveMax). What 0 does is the algorithm's to define.</summary>
    int NextInt(int exclusiveMax);

    /// <summary>Folds the generator's position into a state hash.</summary>
    void HashInto(ref Hash64 hash);
}
