// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>An effect sprite's lit colour (<c>0x0057F3D0</c>, flat <c>0x0057FB80</c>): its tint plus the radial
/// term (<c>0x0057C080</c>, <see cref="ClassicPointLight.RadialAt"/>) of each light in its tile's table at the
/// sprite's point (the item's <c>+0x1C</c>, before any lift or screen offset), clamped at 255 a channel (Q609,
/// ADR-0665). Which sprites are lit is <see cref="IsLit"/>.</summary>
public static class SpriteLighting
{
    /// <summary>Whether a sprite of art <paramref name="artType"/> takes light. <c>RenderEngine_LoadMaterial</c>
    /// (<c>0x0059A6A0</c>) gives type 1 no material flag, type 2 (alpha) flag 4 and type 3 (additive) flag 0x10;
    /// <c>Material_Construct</c> (<c>0x0057C420</c>) starts every state with bit 0x40 and only flags 0x10 and 0x40
    /// clear it; the prepares (<c>0x0057F7E0</c>, <c>0x0057FF30</c>) take the tile's table and the ambient only
    /// under bit 0x40. An item flag 2 or 4 (render info <c>+0x5B</c>, an element's copied from its effect's
    /// <c>+0x81</c>) clears it too: only the hover cage's edge bits (<c>0x0043CC30</c>) set one, which ours
    /// does not mirror.</summary>
    public static bool IsLit(byte artType) => artType is 1 or 2;

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
