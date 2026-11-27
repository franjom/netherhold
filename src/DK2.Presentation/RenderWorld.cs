// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The renderers' world frame and DK2's: a point at DK2 <c>(x, y)</c> and height <c>h</c>, in tiles, is drawn
/// at <c>(x, h, −y)</c> (X east, Y up, Z north), so tile <c>(x, y)</c> centres at <c>(x + 0.5, 0, −(y + 0.5))</c>.</summary>
public static class RenderWorld
{
    public static Vector3 Ground(float x, float y, float height = 0f)
    {
        return new(x, height, -y);
    }

    /// <summary>A drawn point back in native position units (4096 a tile), each axis truncated: <c>(x, −z, y)</c>.</summary>
    public static FpPosition3 ToNative(Vector3 render)
    {
        return new(new Fp32((int)(render.X * Fp32.One)),
        new Fp32((int)(-render.Z * Fp32.One)), new Fp32((int)(render.Y * Fp32.One)));
    }
}
