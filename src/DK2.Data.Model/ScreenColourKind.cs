// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>The sixteen whole-screen ramp builders (screen-colour-changes.md, Q745).</summary>
public enum ScreenColourKind : byte
{
    Normal, Vampire, DarkElf, Skeleton, Salamander, DarkAngel, White, Black,
    Damage, Death, ThreeBands, RedLift, FourBands, FourBandsSwapped, Blue, Maiden,
}
