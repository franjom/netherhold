// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Art.Textures;

/// <summary>Front-end label alpha dilation and native weighted blur (0x00552770/0x00552990).</summary>
public static class FrontEndLabelGlow
{
    // Radius3 selects the ten-wide table at0x006BB770. Exclusive sample bounds consume at most64 coefficients.
    private static readonly double[] Kernel =
    {
        0.003679, 0.005428, 0.007165, 0.008465, 0.008949, 0.008465, 0.007165, 0.005428,
        0.003679, 0.002231, 0.005428, 0.008008, 0.010571, 0.012489, 0.013202, 0.012489,
        0.010571, 0.008008, 0.005428, 0.003292, 0.007165, 0.010571, 0.013956, 0.016488,
        0.017429, 0.016488, 0.013956, 0.010571, 0.007165, 0.004346, 0.008465, 0.012489,
        0.016488, 0.019478, 0.020590, 0.019478, 0.016488, 0.012489, 0.008465, 0.005134,
        0.008949, 0.013202, 0.017429, 0.020590, 0.021767, 0.020590, 0.017429, 0.013202,
        0.008949, 0.005428, 0.008465, 0.012489, 0.016488, 0.019478, 0.020590, 0.019478,
        0.016488, 0.012489, 0.008465, 0.005134, 0.007165, 0.010571, 0.013956, 0.016488,
    };

    public static RgbaImage Create(RgbaImage source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        int width = source.Width, height = source.Height;
        if (width <= 0 || height <= 0 || source.Pixels.Length != checked(width * height * 4))
            throw new ArgumentException("A label needs a complete RGBA surface.", nameof(source));
        var dilated = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte maximum = 0;
                for (int sy = Math.Max(0, y - 3); sy <= Math.Min(height - 1, y + 3); sy++)
                    for (int sx = Math.Max(0, x - 3); sx <= Math.Min(width - 1, x + 3); sx++)
                        maximum = Math.Max(maximum, source.Pixels[(sy * width + sx) * 4 + 3]);
                dilated[y * width + x] = maximum;
            }
        var result = new byte[source.Pixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double alpha = 0;
                int coefficient = 0;
                for (int sy = Math.Max(0, y - 4); sy < Math.Min(height - 1, y + 4); sy++)
                    for (int sx = Math.Max(0, x - 4); sx < Math.Min(width - 1, x + 4); sx++)
                        alpha += dilated[sy * width + sx] * Kernel[coefficient++];
                int at = (y * width + x) * 4;
                result[at] = 255; result[at + 1] = 168; result[at + 2] = 0;
                result[at + 3] = (byte)Math.Min(255, (int)alpha);
            }
        return new(width, height, result);
    }
}
