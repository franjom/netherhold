// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>One of DK2's 32 state-category records — the fields that decide a transition,
/// and whether each of the three per-category hooks exists.</summary>
public readonly struct CreatureStateCategoryDef
{
    public CreatureStateCategoryDef(
        CreatureStateCategory category,
        uint bit,
        uint entryMask,
        bool hasEnterHook,
        bool hasUpdateHook,
        bool hasLeaveHook,
        int accessorDefault)
    {
        Category = category;
        Bit = bit;
        EntryMask = entryMask;
        HasEnterHook = hasEnterHook;
        HasUpdateHook = hasUpdateHook;
        HasLeaveHook = hasLeaveHook;
        AccessorDefault = accessorDefault;
    }

    public CreatureStateCategory Category { get; }

    /// <summary>Record <c>+0x00</c>: this category's own bit, <c>1 &lt;&lt; index</c>.
    /// <para><b><see cref="CreatureStateCategory.Refused"/> is the exception and its bit is
    /// zero</b> — the fill run stores every other dword of the table and never this one, and
    /// nothing else in the program writes it either. So a creature already in that category
    /// can never be admitted by <see cref="EntryMask"/>, and the mask bits that nominally
    /// name it can never match.</para></summary>
    public uint Bit { get; }

    /// <summary>Record <c>+0x34</c>: which categories may transition <b>into</b> this one,
    /// as a set of <see cref="Bit"/>s. It is the <i>target</i>'s mask that is tested, against
    /// the bit of the category the creature is leaving.
    /// <para><b>Zero means unrestricted, not closed</b> — the original admits the move
    /// outright when the mask is zero, which is the opposite of the natural reading.</para></summary>
    public uint EntryMask { get; }

    /// <summary>Record <c>+0x04</c>: whether entering a state of this category runs a hook.</summary>
    public bool HasEnterHook { get; }

    /// <summary>Record <c>+0x14</c>: whether this category runs a hook every tick, just
    /// before the state's own handler. Where the prison drain and the training award live,
    /// rather than in any state handler (ADR-0083).</summary>
    public bool HasUpdateHook { get; }

    /// <summary>Record <c>+0x24</c>: whether leaving a state of this category runs a hook.</summary>
    public bool HasLeaveHook { get; }

    /// <summary>Record <c>+0x3c</c>: a small id that two accessors return for a creature in
    /// this category, and the last field of the record whose <i>meaning</i> is open.
    /// <para>What is established: both accessors substitute the utility state's
    /// <c>UtilityNewState</c> before indexing, both let a field of the state object override
    /// the result, and both special-case the combat category. Their callers sit outside the
    /// creature units. Nothing here consumes it; it is carried so the record is complete and
    /// so a reader can find it (`state-categories.md` §4).</para></summary>
    public int AccessorDefault { get; }

    /// <summary>Whether a creature leaving <paramref name="from"/> may enter this category,
    /// by the mask alone. Equal categories are admitted by the rule's first test, not here.</summary>
    public bool AdmitsFrom(in CreatureStateCategoryDef from) =>
        EntryMask == 0 || (from.Bit & EntryMask) != 0;
}
