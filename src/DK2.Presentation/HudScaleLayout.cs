// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>Custom HUD scaling with a window-fit cap and bottom-docked inverse hit coordinates.</summary>
public readonly record struct HudScaleLayout(int Width, int Height, int RequestedPercent)
{
    public float Factor => Math.Min(Math.Clamp(RequestedPercent, 50, 200) / 100f,
        Math.Max(1, Width * 1920f / (Math.Max(1, Height) * 2560f)));
    public float EffectivePercent => Factor * 100;
    public float LogicalWidth => Width / Factor;
    public float BottomOffset => Height / Factor - Height;
    public Vector2 ToLocalTop(Vector2 screenTop, bool bottomDocked) => screenTop / Factor - new Vector2(0, bottomDocked ? BottomOffset : 0);
    public Vector2 ToScreenTop(Vector2 local, bool bottomDocked) => (local + new Vector2(0, bottomDocked ? BottomOffset : 0)) * Factor;
}
