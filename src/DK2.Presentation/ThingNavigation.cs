// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One door or trap, in the order the cycle walks: its id, owner, kind (thing <c>+0xF6</c>)
/// and the tile the camera glides to (thing <c>+0x1A</c>).</summary>
public readonly record struct ThingNavigationEntry(int Id, PlayerId Owner, byte Kind, TilePos Focus);

/// <summary>A Workshop slot's right click (<c>0x00416070</c>): the doors' cycle <c>0x00416150</c>
/// or the traps' <c>0x004162A0</c>, one instance each, as DK2 keeps a last door at HUD
/// <c>+0x12C</c> and a last trap at <c>+0x12E</c>.</summary>
/// <remarks>While the last thing is still one of the viewer's, of any kind, the walk goes on from
/// the thing after it to the next of the requested kind; past the end, or with no such last thing,
/// it takes the first of that kind from the list's head. No match clears the last thing. The caller
/// passes things newest first, as DK2's list is (<c>Thing_LinkToOwner</c> <c>0x004B4C30</c> links at the
/// head): ours is descending id, DK2's is link order. Not mirrored: the action 0x72 each right click
/// posts first (<c>0x00422600</c>(4, kind)), whose consumer is unread.</remarks>
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

    private bool Matches(ThingNavigationEntry thing, byte kind) => thing.Owner == _viewer && thing.Kind == kind;
}
