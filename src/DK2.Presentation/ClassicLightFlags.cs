// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;

namespace DK2.Presentation;

[Flags]
public enum ClassicLightFlags : uint
{
    None = 0,
    AnimatedRadial = 0x04,
    InstancePoint = 0x100,
    Terrain = 0x64,
    CheapTorch = 0x74,
    NormalTorch = 0x1B2,
}
