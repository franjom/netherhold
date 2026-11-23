// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary><see cref="HudImageVariant.Gauge"/>'s mask as a rank per pixel, for a renderer that masks on the GPU: pixel i
/// is lit exactly when its rank is below <see cref="LitCount"/>, which is <see cref="HudGaugeFraction.Contains"/> at
/// that pixel (<see cref="HudImagePixels.GaugeOffset"/>). A rank is the place of the pixel's angle among the image's
/// distinct angles, so the GPU compares two small integers and never recomputes an angle.</summary>
public sealed class HudGaugeMask
{
    private readonly double[] _angles;

    public HudGaugeMask(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height;
        var angle = new double[width * height];
        for (int row = 0; row < height; row++)
            for (int x = 0; x < width; x++)
                angle[row * width + x] = HudGaugeFraction.Angle(HudImagePixels.GaugeOffset(x, row, width, height));
        double[] sorted = (double[])angle.Clone();
        Array.Sort(sorted);
        int distinct = 0;
        for (int i = 0; i < sorted.Length; i++)
            if (distinct == 0 || sorted[i] != sorted[distinct - 1]) sorted[distinct++] = sorted[i];
        _angles = new double[distinct];
        Array.Copy(sorted, _angles, distinct);
        Ranks = new float[angle.Length];
        for (int i = 0; i < angle.Length; i++) Ranks[i] = Array.BinarySearch(_angles, angle[i]);
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Each pixel's rank, top row first.</summary>
    public float[] Ranks { get; }

    /// <summary>How many of the lowest ranks the fraction lights.</summary>
    public int LitCount(HudGaugeFraction fraction)
    {
        if (fraction.Maximum <= 0 || fraction.Value <= 0) return 0;
        if (fraction.Value >= fraction.Maximum) return _angles.Length;
        double limit = fraction.Limit;
        int low = 0, high = _angles.Length;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (_angles[mid] <= limit) low = mid + 1; else high = mid;
        }
        return low;
    }
}
