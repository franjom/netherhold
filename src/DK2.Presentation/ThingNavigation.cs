// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One door or trap, in the order the cycle walks: its id, owner, kind (thing <c>+0xF6</c>)
/// and the tile the camera glides to (thing <c>+0x1A</c>). For a trap, <paramref name="Alarm"/> is
/// its definition's shot 13 and <paramref name="Ringing"/> that the turn has not passed its recharge
/// deadline (<c>0x004AA2F0</c>): what the Battle tab's alarm reads.</summary>
public readonly record struct ThingNavigationEntry(int Id, PlayerId Owner, byte Kind, TilePos Focus,
    bool Alarm = false, bool Ringing = false);

/// <summary>A Workshop slot's right click (<c>0x00416070</c>): the doors' cycle <c>0x00416150</c>
/// or the traps' <c>0x004162A0</c>, one instance each, as DK2 keeps a last door at HUD
/// <c>+0x12C</c> and a last trap at <c>+0x12E</c>.</summary>
/// <remarks>While the last thing is still one of the viewer's, of any kind, the walk goes on from
/// the thing after it to the next of the requested kind; past the end, or with no such last thing,
/// it takes the first of that kind from the list's head. No match clears the last thing. The caller
/// passes things newest first, as DK2's list is (<c>Thing_LinkToOwner</c> <c>0x004B4C30</c> links at the
/// head): ours is descending id, DK2's is link order. The GUI button each right click posts first
/// (<c>0x00422600</c>, trap category) is <see cref="GuiButtonPairs.ForSecondary(BottomPanelChoice)"/>'s.</remarks>
public sealed class ThingNavigation
{
    private readonly PlayerId _viewer;
    private int _last;

    public ThingNavigation(PlayerId viewer) => _viewer = viewer;

    public TilePos? Next(IReadOnlyList<ThingNavigationEntry> things, byte kind)
    {
        int chosen = -1, previous = -1;
        if (_last != 0)
            for (int i = 0; i < things.Count; i++)
                if (things[i].Id == _last && things[i].Owner == _viewer) { previous = i; break; }
        if (previous >= 0)
            for (int i = previous + 1; i < things.Count; i++)
                if (Matches(things[i], kind)) { chosen = i; break; }
        if (chosen < 0)
            for (int i = 0; i < things.Count; i++)
                if (Matches(things[i], kind)) { chosen = i; break; }
        if (chosen < 0) { _last = 0; return null; }
        _last = things[chosen].Id;
        return things[chosen].Focus;
    }

    /// <summary>The Battle tab alarm's right click (<c>0x00426A00</c>), on the same last trap
    /// (HUD <c>+0x12E</c>) as the Workshop's trap cycle.</summary>
    /// <remarks>The alarm kind is the kind of the viewer's first trap whose shot is 13; with none
    /// (or kind 0), nothing changes. While the last trap is still one of the viewer's, the walk goes on after it to
    /// the next trap of that kind that is ringing. Past the end, or with no such last trap, only the
    /// first trap of that kind from the head is tried: when it is not ringing, a later ringing one is
    /// not found and the last trap clears (<c>0x00426B05..0x00426B2F</c> stops at the first match).</remarks>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/battle-tab.md#1-where-the-tab-lives--strongly-inferred")]
    public TilePos? NextAlarm(IReadOnlyList<ThingNavigationEntry> things)
    {
        int alarm = -1;
        for (int i = 0; i < things.Count; i++)
            if (things[i].Owner == _viewer && things[i].Alarm) { alarm = things[i].Kind; break; }
        if (alarm <= 0) return null;   // 0x00426A63 tests the kind byte: none found, or kind 0
        byte kind = (byte)alarm;
        int chosen = -1, previous = -1;
        if (_last != 0)
            for (int i = 0; i < things.Count; i++)
                if (things[i].Id == _last && things[i].Owner == _viewer) { previous = i; break; }
        if (previous >= 0)
            for (int i = previous + 1; i < things.Count; i++)
                if (Matches(things[i], kind) && things[i].Ringing) { chosen = i; break; }
        if (chosen < 0)
            for (int i = 0; i < things.Count; i++)
                if (Matches(things[i], kind)) { if (things[i].Ringing) chosen = i; break; }
        if (chosen < 0) { _last = 0; return null; }
        _last = things[chosen].Id;
        return things[chosen].Focus;
    }

    private bool Matches(ThingNavigationEntry thing, byte kind) => thing.Owner == _viewer && thing.Kind == kind;
}
