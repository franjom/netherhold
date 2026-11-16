// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The display settings' rules. Brightness is a gamma curve over the finished image, <c>pow(c, 30 / Gamma)</c>,
/// our stand-in for DK2's display gamma ramp. Black and white stay put, so no end of the slider clips (a multiply
/// clipped highlights from about Gamma 40).</summary>
public static class DisplaySettings
{
    public const int DefaultGamma = 30;

    /// <summary>The curve's exponent, <c>30 / Gamma</c>; null at the default, where nothing is drawn.</summary>
    public static float? BrightnessExponent(int gamma) => gamma == DefaultGamma ? null : DefaultGamma / (float)gamma;
}
