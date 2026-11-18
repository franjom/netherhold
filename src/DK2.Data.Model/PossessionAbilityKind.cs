// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
namespace DK2.Data.Model;
/// <summary>The two definition bytes read by <c>0x00494700</c>. Unnamed values are preserved.</summary>
public enum PossessionAbilityKind : byte
{
    None = 0,
    Carry = 1,
    ToggleFlag11 = 3,
    Hypnosis = 5,
    Sneak = 6,
    Prayer = 7,
    Fear = 9,
}
