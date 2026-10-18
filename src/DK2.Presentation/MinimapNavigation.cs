// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

public readonly record struct MinimapNavigationView(
    int Width, int Height, MinimapZoom Zoom, FpPosition3 Focus, Dk2Angle Heading);

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-rendering.md")]
public static class MinimapNavigation
{
    public static bool TryResolve(Vector2 referencePoint, MinimapNavigationView view, out TilePos target)
    {
        target = default;
        if (view.Width <= 0 || view.Height <= 0 || !MinimapControlLayout.IsMap(referencePoint)) return false;
        int u = unchecked((short)((int)(referencePoint.X * 2.5f) - 244));
        int v = unchecked((short)((int)(referencePoint.Y * 2.5f) - 1680));
        if (!view.Zoom.IsWholeMap)
        {
            u = unchecked((short)(int)(u * 1.0993103981018066f));
            v = unchecked((short)(int)(v * 1.0993103981018066f));
        }
        int sine = TrapRules.Sine(view.Heading.Value), cosine = TrapRules.Cosine(view.Heading.Value);
        int rx = (unchecked(u * cosine - v * sine) / 65536) / 4;
        int ry = (unchecked(v * cosine + u * sine) / 65536) / 4;
        int x, y;
        if (view.Zoom.IsWholeMap)
        {
            MinimapMapBounds fit = MinimapMapBounds.Fit(view.Width, view.Height);
            if (fit.Width == 0 || fit.Height == 0) return false;
            x = view.Width / 2 + unchecked(view.Width * rx) / fit.Width;
            y = view.Height / 2 + unchecked(view.Height * ry) / fit.Height;
        }
        else
        {
            x = unchecked(view.Focus.X.Raw + (rx << 12) / view.Zoom.PixelsPerTile) >> 12;
            y = unchecked(view.Focus.Y.Raw + (ry << 12) / view.Zoom.PixelsPerTile) >> 12;
        }
        if ((uint)x >= (uint)view.Width || (uint)y >= (uint)view.Height) return false;
        target = new(x, y);
        return true;
    }
}
