// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-rendering.md")]
public static class MinimapNorth
{
    public static Vector2 SpriteOriginPixels(Dk2Angle heading, int screenHeight)
    {
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        // DK2 maps x by width/2560 and y by height/1920 (0x0052C5A0); the two agree only at
        // 4:3. The HUD here is height-scaled (ADR-0497), so x uses the height as well.
        int centreX = (int)(244L * screenHeight / 1920);
        int centreY = (int)(1680L * screenHeight / 1920);
        int radius = (int)(140L * screenHeight / 1920);
        int inverseHeading = -heading.Value & 2047;
        int dx = (int)((long)TrapRules.Sine(inverseHeading) * radius / 65536);
        int dy = (int)((long)TrapRules.Cosine(inverseHeading) * radius / 65536);
        return new Vector2(centreX + dx - 3, centreY - dy - 6);
    }
}
