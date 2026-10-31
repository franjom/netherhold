// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public readonly record struct CameraModeId(ushort Value)
{
    public static CameraModeId Dungeon => new(3);
    public static CameraModeId Possession => new(10);
    public static CameraModeId ScriptedPath => new(18);

    public bool IsPossession => Value is 2 or 10 or 11;
    public bool BlocksScriptedMove => Value == 1 || IsPossession;
    public bool BlocksOrdinaryInput => Value == 11 || this == ScriptedPath;

    internal int BlendClass => Value switch
    {
        1 or 2 or 10 or 11 => 1,
        3 or 4 or 5 or 9 or 13 or 14 or 15 => 2,
        7 => 3,
        18 => 4,
        _ => 0,
    };

    internal double ViewportBlendMillisecondsTo(CameraModeId target) =>
        Value == 7 && target.Value != 7 ? 0 : BlendClass == target.BlendClass ? 500 : 2000;
}

public readonly record struct HudVisibility(bool ShowNormalHud, bool AllowOrdinaryInput);

/// <summary>The result, continue flag and local restore request read by 0x004039A0.</summary>
public readonly record struct HudResultState(
    bool HasResult = false, bool Continued = false, bool RestoreRequested = false);

[Dk2Behavior(Confidence.Confirmed, "docs/research/hud-visibility.md")]
public static class HudVisibilityRules
{
    public static HudVisibility ForCamera(CameraModeId mode, int letterboxRequests, bool cameraInputBlocked = false)
    {
        bool letterboxAllowsHud = letterboxRequests == 0 || mode.IsPossession;
        return new(mode != CameraModeId.ScriptedPath && letterboxAllowsHud,
            !cameraInputBlocked && !mode.BlocksOrdinaryInput && letterboxAllowsHud);
    }
}
