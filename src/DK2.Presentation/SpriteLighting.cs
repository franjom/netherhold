// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>An effect sprite's lit colour (<c>0x0057F3D0</c>): its tint plus the radial term
/// (<c>0x0057C080</c>, <see cref="ClassicPointLight.RadialAt"/>) of each listed light at the sprite's
/// centre, clamped at 255 a channel. Used only for sprites with <see cref="ConstructionParticle.TakesLight"/>
/// (ADR-0493's amendment keeps the others at their tint, Q609).</summary>
public static class SpriteLighting
{
    public static (byte Red, byte Green, byte Blue) Lit(byte red, byte green, byte blue,
        ReadOnlySpan<ClassicPointLight> lights, Vector3 centre)
    {
        var sum = new Vector3(red / 255f, green / 255f, blue / 255f);
        foreach (ClassicPointLight light in lights) sum += light.RadialAt(centre);
        return (Channel(sum.X), Channel(sum.Y), Channel(sum.Z));
    }

    /// <summary>The vertex packers' conversion (<c>0x0058B440</c>, <c>0x0058B680</c>): <c>c − 0.49999</c>
    /// rounded to nearest (the 12582912 trick, ties to even), then capped at 255.</summary>
    private static byte Channel(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f - 0.49999f, MidpointRounding.ToEven), 0, 255);
}
