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
    /// <summary>A bottom-docked panel's top in whole screen pixels, <see cref="BottomOffset"/> scaled and rounded half
    /// up, for a renderer that places panels itself (the Godot adapter; UI Toolkit docks Unity's).</summary>
    public float BottomOriginPixels => MathF.Floor(BottomOffset * Factor + .5f);
    /// <summary>The surface a panel built from a <see cref="HudScreen"/> lays out on: the logical width in whole local
    /// pixels (halves to even, as Unity's <c>Mathf.RoundToInt</c>) over the full screen height.</summary>
    public HudScreen Screen => new((int)Math.Round(LogicalWidth), Height);
    public Vector2 ToLocalTop(Vector2 screenTop, bool bottomDocked) => screenTop / Factor - new Vector2(0, bottomDocked ? BottomOffset : 0);
    public Vector2 ToScreenTop(Vector2 local, bool bottomDocked) => (local + new Vector2(0, bottomDocked ? BottomOffset : 0)) * Factor;
}
