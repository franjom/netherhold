// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Which of DK2's 32 state categories a state belongs to — record <c>+0x17</c>,
/// the index into the category table carrying the enter, update and leave hooks.
/// <para><b>These names are ours; DK2 numbers the categories and never names them.</b>
/// Six are settled by what their hooks do — <see cref="Research"/>,
/// <see cref="Manufacture"/>, <see cref="Training"/>, <see cref="Prison"/>,
/// <see cref="Torture"/>, <see cref="Combat"/> (ADR-0083). Three come from
/// <c>eating.md</c> §10: <see cref="Movement"/>, <see cref="Eating"/>,
/// <see cref="Sleeping"/>. The rest are named from the states they hold, which is
/// STRONGLY INFERRED, and where that is not unambiguous the member stays
/// <c>UnnamedNN</c> rather than carry a guess.</para>
/// <para>Which states a category owns is not repeated here — ask
/// <see cref="CreatureStateTable.InCategory"/>.</para></summary>
public enum CreatureStateCategory : byte
{
    /// <summary>Refused as a transition target when the group tags match, so its 48 states
    /// are reached by the differing-tags route. Names a transition role, not a mechanic.</summary>
    Refused = 0,

    Idle = 1,

    /// <summary>The <b>utility</b> states: they save the caller's state and resume it, and
    /// the transition rule special-cases this number (`creature-state-table.md` §2.2).
    /// Every mechanic routes through them, so nothing moves without a resume slot.</summary>
    Movement = 2,

    /// <summary>The worker states, and where most of this project's 21 activities live.</summary>
    WorkerJob = 3,

    Research = 4,

    Manufacture = 5,

    Lair = 6,

    /// <summary><b>Not named:</b> 29 states mixing joining, leaving, stunned and hazard.</summary>
    Unnamed7 = 7,

    Eating = 8,

    Sleeping = 9,

    Sulking = 10,

    GuardPost = 11,

    Temple = 12,

    Training = 13,

    Prison = 14,

    Torture = 15,

    CombatPit = 16,

    /// <summary>Eight of the fifteen states the room uses. <b>This category does not delimit
    /// the room:</b> the two clock-suppressing flag bits (ADR-0074) mark the contiguous run
    /// 145-159, and the other seven of those sit in <see cref="Refused"/> and
    /// <see cref="Unnamed18"/>, so a mechanic keyed on this category alone misses them.</summary>
    Casino = 17,

    /// <summary><b>Not named:</b> 34 states covering attacks on hearts, rooms and walls,
    /// theft, tunnelling and exploring. It reads as offensive party behaviour, and naming it
    /// that would be a guess about a cluster no handler has been read from.</summary>
    Unnamed18 = 18,

    /// <summary>Selected by no state in the shipped table. The hook record exists and
    /// nothing points at it.</summary>
    Unused = 19,

    Payday = 20,

    /// <summary><b>Not named:</b> one state, and its label is <c>idle</c>.</summary>
    Unnamed21 = 21,

    Combat = 22,

    /// <summary>With group tag 3, the passthrough the rule admits from any category <i>in
    /// that group</i>; crossing tags is decided by the entry mask (§2.2).</summary>
    Dying = 23,

    TrapDisarm = 24,

    /// <summary>The response to the keeper spell.</summary>
    CallToArms = 25,

    /// <summary>Both its states carry the <c>reporting danger</c> label and no other
    /// category holds one, which is what names it (`state-categories.md`).</summary>
    ReportingDanger = 26,

    /// <summary><b>Not named:</b> three states carrying the same label as three of
    /// <see cref="Combat"/>'s, yet in their own category; what separates them is unread.</summary>
    Unnamed27 = 27,

    /// <summary><b>Not named:</b> 15 states, every one carrying the idle label, with no room
    /// or mechanic attached and no handler read.</summary>
    Unnamed28 = 28,

    /// <summary><b>Not named:</b> two states, labelled <c>idle</c> and <c>hypnotised</c>.</summary>
    Unnamed29 = 29,

    Incapacitated = 30,

    /// <summary>Watching <see cref="CombatPit"/>'s fight.</summary>
    Spectating = 31,
}
