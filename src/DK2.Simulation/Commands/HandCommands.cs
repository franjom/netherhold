// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Picks a creature up into the keeper's hand.
///
/// <para>By creature id, not by position: which creature is under the cursor is
/// a UI question, and resolving it here would put a camera in the simulation —
/// as for <see cref="TagTilesCommand"/>'s rectangle.</para>
///
/// <para>Refused rather than faulted when the creature cannot be held — gone,
/// somebody else's, already in a hand, down, or the hand is full
/// (<see cref="HandRules.CanPickUp"/>). A refused command emits nothing, as
/// every other command here.</para>
/// </summary>
public sealed record PickUpCreatureCommand(byte PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}

/// <summary>
/// Puts down whatever the keeper is holding, on the given tile — a creature or
/// an object, whichever came last. One gesture, one command (ADR-0075), because
/// DK2 has a single drop handler branching on the kind it read back.
/// <para>No id, because the hand chooses: the drop handler reads back the last
/// thing picked up and takes no argument but a position. <b>Last in, first
/// out</b> — CONFIRMED (owner, 2026-09-06). That ordering is behaviour, so
/// naming a creature here would discard it (<see cref="PlayerState.Hand"/>).</para>
/// <para>A worker put down beside work the keeper has marked takes the job
/// <i>and</i> works it 12.5% faster (<see cref="CreatureState.JustDropped"/>);
/// dropping it on empty ground is worth nothing.</para>
/// </summary>
public sealed record DropCommand(byte PlayerId, TilePos Tile)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
    }
}

/// <summary>
/// Picks a loose object up into the keeper's hand — a chicken, a pile of gold,
/// a special. CONFIRMED (owner, Q196), with a screenshot of four held at once.
/// <para>By object id, for the reason <see cref="PickUpCreatureCommand"/> gives.
/// DK2's cursor search is a rule rather than a ray-cast — 3x3 tiles around the
/// point, an octagonal approximation (<c>max + min/2</c>), a quarter tile for an
/// object where a creature gets half, and a creature in preference to an object
/// — but none of it is simulation. Refused when the object is gone, already
/// held, in an imp's arms, the hand is full, or its definition does not carry
/// <see cref="Data.Model.ObjectDef.CanBePickedUp"/>, 26 of the 145 records
/// (<see cref="HandRules.CanPickUpObject"/>).</para>
/// </summary>
public sealed record PickUpObjectCommand(byte PlayerId, int ObjectId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(ObjectId);
    }
}

/// <summary>
/// Slaps a creature: it hurts (<c>SlapDamage</c>), it angers (<c>AngerSlap</c>,
/// +3000 for a Goblin and <b>−5000 for Jack</b>, whose whole record runs
/// backwards), and it makes a worker work faster for 25 seconds
/// (<see cref="EfficiencyRules.SlapBonusSeconds"/>).
/// <para><b>There is no cooldown</b>: every slap counts however fast they come,
/// because the original's <c>LastSlapTick</c> window guards only its knock-back
/// impulse (<see cref="HandRules.KnockBackWindowSeconds"/>). What does not
/// compound is the work bonus, which a second slap rewrites rather than adds to.
/// The owner's six slaps to a sulking Salamander (Q114) were six because a hand
/// is slow.</para>
/// </summary>
public sealed record SlapCreatureCommand(byte PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}
