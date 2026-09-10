// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>One of DK2's 281 creature states: the five fields of the nine-field record that
/// are read at all.
/// <para>The three unread pointers and the record's own copy of its id are dead in the
/// original (Q184) and absent here. Handler addresses are not carried either — nothing in
/// this project calls them, so they stay in `creature-state-table.md` §4 as the reading
/// list.</para></summary>
public readonly struct CreatureStateDef
{
    public CreatureStateDef(
        short id,
        short labelStringId,
        byte group,
        CreatureStateCategory category,
        CreatureStateFlags flags)
    {
        Id = id;
        LabelStringId = labelStringId;
        Group = group;
        Category = category;
        Flags = flags;
    }

    /// <summary>The state id, which equals the row's index in the table.</summary>
    public short Id { get; }

    /// <summary>The state's label as an id into the install's own <c>Text.str</c> — the
    /// "current activity" line on the flower above a creature's head.
    /// <para><b>0 means the state has no label</b>, not "string 0", which is an unrelated
    /// string; six states carry 0. The string itself is never held here: it is a shipped
    /// asset and resolves at run time from the user's install
    /// (`creature-state-table.md` §3).</para></summary>
    public short LabelStringId { get; }

    /// <summary>Record <c>+0x16</c>, the group tag.
    /// <para><b>What the groups are is not known</b> — only how the transition rule uses
    /// them, which is equality between two states and nothing else. It indexes nothing,
    /// which is why it holds 50 and 100 alongside 0-13 without being out of range. Group 7
    /// is the one value with a settled meaning: the fourteen states that <i>are</i> a need,
    /// which the needs routine reads as "do not ask" (`eating.md` §3).</para></summary>
    public byte Group { get; }

    /// <summary>Record <c>+0x17</c>: which of the 32 hook records this state runs under.</summary>
    public CreatureStateCategory Category { get; }

    /// <summary>Record <c>+0x18</c>, three of whose bits are understood.</summary>
    public CreatureStateFlags Flags { get; }

    /// <summary>Whether every bit of <paramref name="flags"/> is set on this state.</summary>
    public bool Has(CreatureStateFlags flags) => (Flags & flags) == flags;

    /// <summary>Whether this state saves and resumes its caller's state.</summary>
    public bool IsUtility => Category == CreatureStateCategory.Movement;
}
