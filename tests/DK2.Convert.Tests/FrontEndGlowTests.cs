// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;

namespace DK2.Convert.Tests;

public class FrontEndGlowTests
{
    [Fact]
    public void NativeExclusiveBlurBoundsDoNotNormalizeTheUnusedKernelTail()
    {
        byte[] pixels = new byte[20 * 20 * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        RgbaImage glow = FrontEndLabelGlow.Create(new RgbaImage(20, 20, pixels));
        int centre = (10 * 20 + 10) * 4;
        Assert.Equal(189, glow.Pixels[centre + 3]);
        Assert.Equal(new byte[] { 255, 168, 0 }, glow.Pixels.Skip(centre).Take(3));
    }

    [Fact]
    public void AnEmptyLabelHasNoGlow()
    {
        RgbaImage glow = FrontEndLabelGlow.Create(new RgbaImage(20, 20, new byte[20 * 20 * 4]));
        Assert.All(Enumerable.Range(0, 400), i => Assert.Equal(0, glow.Pixels[i * 4 + 3]));
    }
}
