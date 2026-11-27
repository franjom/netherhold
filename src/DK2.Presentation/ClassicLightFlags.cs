// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;

namespace DK2.Presentation;

[Flags]
public enum ClassicLightFlags : uint
{
    None = 0,
    AnimatedRadial = 0x04,
    InstancePoint = 0x100,
    Terrain = 0x64,
    CheapTorch = 0x74,
    NormalTorch = 0x1B2,
    NormalObject = 0x191,
    CheapObject = 0x95,
}

/// <summary>The renderer's cheap-lighting option as light flags: the torch flags a scene gives its torches, and the test
/// its object, effect and portal lights read back (migration plan step 2j).</summary>
public static class ClassicLightFlagsRules
{
    /// <summary>The torch flags of a scene with cheap lighting on or off.</summary>
    public static ClassicLightFlags Torch(bool cheapLighting)
    {
        return cheapLighting ? ClassicLightFlags.CheapTorch : ClassicLightFlags.NormalTorch;
    }

    /// <summary>Whether a scene's torch flags say cheap lighting; false for <see cref="ClassicLightFlags.None"/>, the
    /// flags of a scene without classic vertex lighting.</summary>
    public static bool IsCheapTorch(this ClassicLightFlags torchFlags)
    {
        return torchFlags == ClassicLightFlags.CheapTorch;
    }
}
