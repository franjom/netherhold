// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public readonly record struct CameraModeId(ushort Value)
{
    public static CameraModeId ScriptedPath => new(18);
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
        bool letterboxAllowsHud = letterboxRequests == 0 || mode.Value is 2 or 10 or 11;
        return new(mode != CameraModeId.ScriptedPath && letterboxAllowsHud,
            !cameraInputBlocked && mode.Value is not (11 or 18) && letterboxAllowsHud);
    }
}
