"""Regenerate the two creature-state tables from their research notes.

    python tools/gen-creature-state-tables.py

Both outputs are generated, never hand-edited: correct a row in the note and run
this. Their sources are committed notes, not the gitignored Ghidra dumps, so the
workflow is reproducible by anyone with the repository:

    docs/research/creature-state-table.md  section 4   -> CreatureStateTable.cs
    docs/research/state-categories.md      section 7   -> CreatureStateCategoryTable.cs

Neither output carries handler or hook addresses - nothing in this project calls
them - nor the states' display labels, which are shipped-asset text and stay out
of src/ (creature-state-table.md section 3). Only Text.str ids cross.

The script refuses to write anything unless the parse reproduces the checks that
established each table, so a mangled note fails here rather than downstream.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

STATE_NOTE = os.path.join(ROOT, "docs", "research", "creature-state-table.md")
CATEGORY_NOTE = os.path.join(ROOT, "docs", "research", "state-categories.md")
MODEL = os.path.join(ROOT, "src", "DK2.Data.Model")

def category_names():
    """The category names live in the enum, not here: a second copy is a second thing
    that can go stale, and it already did once."""
    src = read(os.path.join(MODEL, "CreatureStateCategory.cs"))
    found = dict((int(v), n) for (n, v) in
                 re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+),", src, re.M))
    assert sorted(found) == list(range(32)),         "CreatureStateCategory.cs does not declare exactly 32 members 0..31"
    return found

FLAG_NAME = {
    0: "Unread0", 1: "SuppressNeedAnger", 2: "Unread2",
    3: "SuppressHungerClock", 4: "SuppressTirednessClock", 6: "Unread6",
}

LICENCE = """﻿// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
"""


def read(path):
    with open(path, "rb") as f:
        return f.read().decode("utf-8")


def write(path, text):
    with open(path, "wb") as f:
        f.write(text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))


# --------------------------------------------------------------- state table
STATE_CELL = re.compile(
    r"\|\s*(\d+)\s*`0x[0-9a-f]+`\s*\|([^|]*)\|\s*`(0x[0-9a-f]{8})`\s*\|([^|]*)\|"
    r"\s*(\d+)\s*.\s*(\d+)\s*.\s*(\d+)\s*"
)


def parse_states():
    states = {}
    for line in read(STATE_NOTE).split("\n"):
        if not line.startswith("|"):
            continue
        for m in STATE_CELL.finditer(line):
            label = m.group(4).strip()
            states[int(m.group(1))] = dict(
                text=0 if label in ("&mdash;", "-", "") else int(label),
                grp=int(m.group(5)), cat=int(m.group(6)), flag=int(m.group(7)))
    return states


def flags_expr(value):
    if value == 0:
        return "Flag.None"
    return " | ".join("Flag." + FLAG_NAME[b] for b in range(8) if value & (1 << b))


def emit_state_table(states, CATEGORY_NAME):
    assert len(states) == 281, "expected 281 states, parsed %d" % len(states)
    assert sorted(states) == list(range(281)), "state ids are not contiguous 0..280"
    for i, s in states.items():
        assert 0 <= s["cat"] <= 31, "state %d: category out of 0..31" % i
        assert not (s["flag"] & 0b10100000), \
            "state %d: flag bit 5 or 7 is set and the flags enum omits them" % i

    rows = []
    for i in range(281):
        s = states[i]
        rows.append("        new CreatureStateDef(%3d, %5d, %3d, %-20s %s),"
                    % (i, s["text"], s["grp"], "Cat." + CATEGORY_NAME[s['cat']] + ",",
                       flags_expr(s["flag"])))

    body = LICENCE + '''
using System;
using DK2.Core;

namespace DK2.Data.Model;

using Cat = CreatureStateCategory;
using Flag = CreatureStateFlags;

/// <summary>DK2's 281 creature states, as data. Nothing here decides behaviour: it is the
/// map a mechanic is read against, and the input to the transition rule.
/// <para><b>Generated</b> by <c>tools/gen-creature-state-tables.py</c> from
/// <c>docs/research/creature-state-table.md</c> §4, whose six independent checks establish
/// the frame and the count. Correct a row in the note and regenerate; an edit made here is
/// lost on the next run.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-state-table.md")]
public static class CreatureStateTable
{
    /// <summary>Records in the table. An earlier reading said 239; that was a scan window
    /// that ended before the data did (ADR-0072).</summary>
    public const int Count = 281;

    /// <summary>Entries in DK2's category hook table, each <c>0x40</c> bytes. One of the 32
    /// (<see cref="Cat.Unused"/>) is selected by no state.</summary>
    public const int CategoryCount = 32;

    //                             id  label  grp  category             flags
    private static readonly CreatureStateDef[] Rows =
    {
''' + "\n".join(rows) + '''
    };

    /// <summary>Every state, in id order.</summary>
    public static ReadOnlySpan<CreatureStateDef> All => Rows;

    /// <summary>The state with this id. Ids run 0 to 280 and equal the row index.</summary>
    public static ref readonly CreatureStateDef Get(int stateId)
    {
        if ((uint)stateId >= (uint)Rows.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stateId), stateId, $"DK2 has {Rows.Length} creature states.");
        }

        return ref Rows[stateId];
    }

    /// <summary>Whether this state saves and resumes its caller's state.</summary>
    public static bool IsUtility(int stateId) => Get(stateId).IsUtility;

    /// <summary>Every state id in one category, ascending.</summary>
    public static int[] InCategory(CreatureStateCategory category)
    {
        var found = new System.Collections.Generic.List<int>();
        for (int i = 0; i < Count; i++)
        {
            if (Rows[i].Category == category)
            {
                found.Add(i);
            }
        }

        return found.ToArray();
    }
}
'''
    write(os.path.join(MODEL, "CreatureStateTable.cs"), body)
    return len(rows)


# ------------------------------------------------------------ category table
CAT_ROW = re.compile(
    r"^\|\s*(\d+)\s*\|\s*([A-Za-z0-9]+)\s*\|\s*`(0x[0-9A-Fa-f]+|0)`\s*\|"
    r"\s*`(0x[0-9A-Fa-f]+)`\s*\|\s*([Y.])\s*\|\s*([Y.])\s*\|\s*([Y.])\s*\|\s*(\d+)\s*\|"
)


def parse_categories():
    cats = {}
    for line in read(CATEGORY_NOTE).split("\n"):
        m = CAT_ROW.match(line.strip())
        if m:
            cats[int(m.group(1))] = dict(
                name=m.group(2), bit=int(m.group(3), 16), mask=int(m.group(4), 16),
                enter=m.group(5) == "Y", update=m.group(6) == "Y",
                leave=m.group(7) == "Y", x3c=int(m.group(8)))
    return cats


def emit_category_table(cats, CATEGORY_NAME):
    assert len(cats) == 32, "expected 32 categories, parsed %d" % len(cats)
    assert sorted(cats) == list(range(32)), "category indices are not contiguous 0..31"
    for i, c in cats.items():
        assert c["name"] == CATEGORY_NAME[i], \
            "category %d: the note calls it %s, this script %s" % (i, c["name"], CATEGORY_NAME[i])
        want = 0 if i == 0 else (1 << i)
        assert c["bit"] == want, "category %d: bit is 0x%X, expected 0x%X" % (i, c["bit"], want)

    rows = []
    for i in range(32):
        c = cats[i]
        rows.append(
            "        new CreatureStateCategoryDef(Cat.%-14s 0x%08Xu, 0x%08Xu, %-6s %-6s %-6s %3d),"
            % (c["name"] + ",", c["bit"], c["mask"],
               ("true," if c["enter"] else "false,"),
               ("true," if c["update"] else "false,"),
               ("true," if c["leave"] else "false,"), c["x3c"]))

    body = LICENCE + '''
using System;
using DK2.Core;

namespace DK2.Data.Model;

using Cat = CreatureStateCategory;

/// <summary>DK2's 32 state-category records: the half of the transition rule the state
/// table cannot answer, plus which of the three per-category hooks exist.
/// <para><b>Generated</b> by <c>tools/gen-creature-state-tables.py</c> from
/// <c>docs/research/state-categories.md</c> §7. Hook <i>addresses</i> are not carried:
/// nothing here calls them. An edit made here is lost on the next run.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-categories.md")]
public static class CreatureStateCategoryTable
{
    /// <summary>Records in the table, each <c>0x40</c> bytes.</summary>
    public const int Count = 32;

    //                              category        bit          entry mask   enter   update  leave  +3c
    private static readonly CreatureStateCategoryDef[] Rows =
    {
''' + "\n".join(rows) + '''
    };

    /// <summary>Every category record, in index order.</summary>
    public static ReadOnlySpan<CreatureStateCategoryDef> All => Rows;

    /// <summary>The record for one category.</summary>
    public static ref readonly CreatureStateCategoryDef Get(CreatureStateCategory category)
    {
        if ((uint)category >= (uint)Rows.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category), category, $"DK2 has {Rows.Length} state categories.");
        }

        return ref Rows[(int)category];
    }
}
'''
    write(os.path.join(MODEL, "CreatureStateCategoryTable.cs"), body)
    return len(rows)


if __name__ == "__main__":
    names = category_names()
    n = emit_state_table(parse_states(), names)
    m = emit_category_table(parse_categories(), names)
    sys.stdout.write("CreatureStateTable.cs: %d states\n" % n)
    sys.stdout.write("CreatureStateCategoryTable.cs: %d categories\n" % m)
