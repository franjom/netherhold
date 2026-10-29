// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
namespace DK2.Presentation;

public enum ClassicHudAction : byte
{
    None, ToggleGui, ToggleOptions, ToggleFullMap, NavigateHeart, SelectQuery, SelectSell, ZoomIn, ZoomOut,
}

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-controls.md")]
public sealed class ClassicHudControls
{
    public SoundRequests Sounds { get; } = new();
    public bool GuiVisible { get; private set; } = true;
    public bool OptionsVisible { get; private set; }
    public bool FullMapVisible { get; private set; }
    public bool InputSuppressed { get; private set; }

    public void SetGuiVisible(bool visible) => GuiVisible = visible;

    [Dk2Behavior(Confidence.Confirmed, "docs/research/hud-visibility.md#result-and-special-player-state-binary-2026-10-24")]
    public void UpdateResult(HudResultState result, CameraModeId camera = default)
    {
        if (camera != CameraModeId.ScriptedPath) InputSuppressed = false;
        if (result.HasResult && !result.Continued)
        {
            InputSuppressed = true;
            GuiVisible = false;
        }
        if (result.RestoreRequested && result.Continued)
        {
            InputSuppressed = false;
            GuiVisible = true;
        }
    }

    public void Apply(ClassicHudAction action)
    {
        if (InputSuppressed) return;
        if (FullMapVisible && action != ClassicHudAction.ToggleFullMap) return;
        if (OptionsVisible && action != ClassicHudAction.ToggleOptions) return;
        switch (action)
        {
            case ClassicHudAction.ToggleGui: GuiVisible = !GuiVisible; break;
            case ClassicHudAction.ToggleOptions:
                OptionsVisible = !OptionsVisible; GuiVisible = true;
                Sounds.Add(new SoundRequest(SoundCategory.None, new SoundId(OptionsVisible ? 0x139u : 0x13au)));
                break;
            case ClassicHudAction.ToggleFullMap: FullMapVisible = !FullMapVisible; break;
        }
    }
    public HudVisibility Visibility(HudVisibility camera) =>
        new(camera.ShowNormalHud && GuiVisible && !FullMapVisible, camera.AllowOrdinaryInput && !InputSuppressed);
}

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-controls.md")]
public static class ClassicHudControlLayout
{
    public static ClassicHudAction ActionAt(Vector2 point, bool secondary)
    {
        if (MinimapControlLayout.IsMagnifier(point)) return secondary ? ClassicHudAction.ZoomOut : ClassicHudAction.ZoomIn;
        if (secondary) return ClassicHudAction.None;
        if (In(point, 8, 584, 40, 616)) return ClassicHudAction.SelectQuery;
        if (In(point, 155.2f, 584, 187.2f, 616)) return ClassicHudAction.SelectSell;
        if (In(point, 8, 728, 40, 760)) return ClassicHudAction.ToggleOptions;
        return ClassicHudAction.None;
    }
    private static bool In(Vector2 p,float left,float top,float right,float bottom) =>
        p.X >= left && p.X < right && p.Y >= top && p.Y < bottom;
}
