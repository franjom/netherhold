// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Camera_SetMode's non-class-6 mode-11 target, 0x0044BA14..0x0044BA62.</summary>
public static class PossessionObjectDeathView
{
    public static CameraFlightState Target(in CameraFlightState current, CameraThing preset, Fp32 floorHeight) =>
        new(current.X, current.Y, floorHeight.Raw + 0x100, preset.Zoom, preset.Lens, 0,
            current.Yaw & 0x7FF, 400, 200);
}
