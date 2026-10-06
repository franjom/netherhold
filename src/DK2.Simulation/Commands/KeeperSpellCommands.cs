// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Packet action 54: a keeper spell cast at a place. <paramref name="X"/> and
/// <paramref name="Y"/> are DK2's position units, 4096 a tile, as the packet carries them.
/// Only Call to Arms (spell 3) is read end to end; other ids are refused (ADR-0279).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/call-to-arms.md")]
public sealed record CastSpellAtPlaceCommand(PlayerId PlayerId, byte SpellId, int X, int Y, ushort Flags = 0)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(SpellId);
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Flags);
    }
}

/// <summary>Packet action 63: the slap that takes a keeper's own Call to Arms flag down
/// (<c>0x005136E0</c> through the slap action 6 and <c>0x0049C2A0</c>). Refused when the
/// keeper has no standing flag or it is already going (ADR-0279).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/call-to-arms.md")]
public sealed record RemoveCallToArmsCommand(PlayerId PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

/// <summary>Packet action 55: a keeper spell cast at a creature. Thunderbolt (6, ADR-0260) and
/// Heal (10, ADR-0287) are read end to end; other ids and nonzero flags are refused.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-thunderbolt.md")]
public sealed record CastSpellOnCreatureCommand(PlayerId PlayerId, byte SpellId, int CreatureId, ushort Flags = 0)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(SpellId);
        hash.Add(CreatureId);
        hash.Add(Flags);
    }
}
