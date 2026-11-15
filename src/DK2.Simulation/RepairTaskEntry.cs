// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>One keeper's kind-16 (wall repair) pool entry: the holder of each side's slot 0
/// (<c>Worker[side*3]</c>, <c>0x00508110</c> / <c>0x00508160</c>), indexed as the sides are
/// numbered by the reinforce handler (<c>0x00698A60</c>: north, east, south, west), 0 for none.
/// A holder outlives its own repair: the leave hook <c>0x004647D0</c> has no case for states 23
/// and 24 and state 24's removal tests the entry's type against 6 where it is 16, so the entry ends on a dig tag
/// (<c>0x00507BB0</c>) or an unreachable free side (<c>0x0046B3B6</c>), and no other removal was found
/// (docs/research/functions/00466710.md; suspected DK2 defect, built as DK2 does it, ADR-0783).</summary>
public sealed class RepairTaskEntry
{
    public int[] Holders { get; } = new int[4];
}
