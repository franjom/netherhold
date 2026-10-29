// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;

namespace DK2.Data.Model;

/// <summary>Record <c>+0x18</c> of a creature state: a bitfield, four bits of which are
/// understood (Q185).
/// <para>Six of the eight bits are used. Bits 5 and 7 are <b>never set in any of the 281
/// shipped states</b>, which is asserted by a test rather than assumed — a bit nothing
/// sets is a bit whose meaning can never be recovered from this data. The two
/// <c>Unread</c> members are set by real states and have located readers or none at all;
/// they are named for what is known about them, which is their position.</para></summary>
[Flags]
public enum CreatureStateFlags : byte
{
    /// <summary>No flags. 60 states.</summary>
    None = 0,

    /// <summary>Carried by <b>195</b> of the 281 states, and not one of the seven
    /// instructions that read this byte tests it. The single most widespread unread fact
    /// about the table.</summary>
    Unread0 = 1 << 0,

    /// <summary>Suppresses the need-anger routine: a creature busy with something urgent,
    /// or not properly its keeper's, is charged no `AngerNoLair`, `AngerNoFood` or
    /// `AngerNoPay` (ADR-0079). 43 states, including all eleven captive ones.</summary>
    SuppressNeedAnger = 1 << 1,

    /// <summary>The creature is not attackable while in the state: <c>0x0045A8B0</c> clears
    /// <c>IsAttackable</c> (<c>TEST [state*25 + 0x6DA8C8], 4</c> at <c>0x0045A948</c>). 25 states:
    /// 75, 76, the lair and sleep run 79-84, the Casino's 153 and 155-159, joining (193), the
    /// Reaper's 235-237 and 242, and 269, 272, 273, 278-280 (ADR-0585).</summary>
    NotAttackable = 1 << 2,

    /// <summary>Stops the hunger clock <i>accumulating</i> — not the attempt to eat.
    /// Exactly the 15 Casino states, which is why the room makes a creature neither hungry
    /// nor tired (ADR-0074).</summary>
    SuppressHungerClock = 1 << 3,

    /// <summary>Stops the tiredness clock accumulating. The same 15 Casino states as
    /// <see cref="SuppressHungerClock"/>.</summary>
    SuppressTirednessClock = 1 << 4,

    /// <summary>The creature may not look for a fight: the first test of the may-look gate
    /// <c>0x0045A6D0</c> (<c>TEST [state*25 + 0x6DA8C8], 0x40</c> at <c>0x0045A71F</c>). 15 states:
    /// the stuns 209, 210, 255, 256, 263, 264, dancing, gambling, joining and three idles, but not
    /// 257, getting up (combat-acquisition.md §2).</summary>
    NoFightLookup = 1 << 6,
}
