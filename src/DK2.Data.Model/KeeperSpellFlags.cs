// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>The keeper spell descriptor's flag word (file <c>0xDC</c>, runtime <c>+0x54</c>). The
/// computer keeper's battle step (<c>0x00503060</c>) reads three bits: an offensive spell is cast at
/// the fighter's enemy, a supportive one at a wounded own creature, and a spell with bit 4 set is
/// never chosen. Shipped: Thunderbolt, Inferno and Chicken are offensive, Heal is supportive, and
/// every spell that makes or keeps a thing carries bit 4 (computer-keeper-management.md §4).</summary>
[Flags]
public enum KeeperSpellFlags : uint
{
    None = 0,
    Offensive = 1u << 2,
    Supportive = 1u << 3,
    ExcludedFromBattle = 1u << 4,
}
