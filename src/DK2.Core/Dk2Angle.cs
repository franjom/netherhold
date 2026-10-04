// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>A DK2 11-bit angle as carried on the wire; <see cref="Value"/> masks it to 0x7FF.</summary>
public readonly record struct Dk2Angle(ushort Raw)
{
    public int Value => Raw & 0x7FF;
}
