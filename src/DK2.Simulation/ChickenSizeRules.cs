// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>Chicken creation size on the world stream, Object.cpp:63.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/chicken-birth-clock.md")]
public static class ChickenSizeRules
{
    public static void Initialize(ObjectState thing, SimRandom rng)
    {
        if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId)
            thing.ChickenScale = new Fp32(Fp32.One + rng.NextInt(0x800));
    }
}
