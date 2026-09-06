// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Picks a creature up into the keeper's hand.
///
/// <para>The fourth, fifth and sixth player commands in this project arrive
/// together — this, <see cref="DropCommand"/> and
/// <see cref="SlapCreatureCommand"/> — because the Hand of Evil is one verb
/// with three halves and building any one of them alone would leave a creature
/// stuck in a hand with no way out.</para>
///
/// <para><b>By creature id, not by position.</b> Which creature is under the
/// cursor is a UI question with a UI answer (DK2 picks the thing its mouse ray
/// hits), and resolving it here would put a camera in the simulation. The
/// simulation is told which creature; deciding that is the presentation
/// layer's job, exactly as it is for <see cref="TagTilesCommand"/>'s
/// rectangle.</para>
///
/// <para>Refused rather than faulted when the creature cannot be held — it is
/// gone, is somebody else's, is already in a hand, is down, or the hand is full
/// (<see cref="HandRules.CanPickUp"/>). A refused command emits nothing, in
/// keeping with every other command here.</para>
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
/// an object, whichever came last.
///
/// <para><b>No id, because the hand chooses.</b> DK2's drop handler reads back
/// the last thing picked up and takes no argument but a position, so a keeper
/// holding three things and dropping twice puts down the third and then the
/// second. **Last in, first out, CONFIRMED by the owner** (2026-09-06) on top
/// of the original's handler reading back the slot it is about to free. That
/// ordering is behaviour, and naming a creature here would quietly discard
/// it. See <see cref="PlayerState.Hand"/>.</para>
///
/// <para><b>It was <c>DropCreatureCommand</c> until ADR-0075</b>, when the hand
/// learned to hold a chicken. One gesture, one command: DK2 has a single drop
/// handler that branches on the kind of the thing it read back.</para>
///
/// <para>What a drop does beyond moving the creature is the interesting half:
/// a worker put down beside work the keeper has marked takes the job <i>and</i>
/// works it 12.5% faster (<see cref="CreatureState.JustDropped"/>). Dropping it
/// on empty ground is worth nothing at all.</para>
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
/// a special.
///
/// <para><b>CONFIRMED by the owner</b> (2026-09-06): <i>"I can pick up a
/// chicken and drop it in the prison, or anywhere in the dungeon, on my owned
/// tiles"</i>, with a screenshot of four held at once as four separate slots.
/// Until then the hand held creature ids only, which left the prison eating
/// built at ADR-0074 complete and unreachable in play (Q196).</para>
///
/// <para><b>By object id, for the reason
/// <see cref="PickUpCreatureCommand"/> gives</b>: which thing is under the
/// cursor is a UI question. DK2 answers it in one function, and the answer is
/// worth knowing because it is a rule rather than a ray-cast — it sweeps the
/// 3x3 tiles around the point, measures with an octagonal approximation
/// (<c>max + min/2</c>) rather than a true distance, allows an object within a
/// <b>quarter tile</b> where a creature gets <b>half</b>, and <b>returns a
/// creature in preference to an object</b> whenever both are in reach. None of
/// that is here, because none of it is simulation.</para>
///
/// <para>Refused when the object is gone, is already held, is in an imp's
/// arms, or the hand is full — and above all when its definition does not carry
/// <see cref="Data.Model.ObjectDef.CanBePickedUp"/>, which is 26 of the 145
/// records and excludes spell books, crates and beds
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
/// Slaps a creature.
///
/// <para>The oldest verb in the series, and the one the balance table has the
/// most to say about: it hurts (<c>SlapDamage</c>), it angers
/// (<c>AngerSlap</c>, +3000 for a Goblin and <b>−5000 for Jack</b>, whose whole
/// record runs backwards), and it makes a worker work faster for 25 seconds
/// (<see cref="EfficiencyRules.SlapBonusSeconds"/>).</para>
///
/// <para><b>Slapping faster does not slap harder.</b> A second slap inside one
/// game second is refused outright (<see cref="HandRules.SlapCooldownTicks"/>),
/// and the work bonus is rewritten rather than added to — which is why the
/// owner could count six slaps to a sulking Salamander (Q114) and why an imp
/// slapped ten times digs exactly as fast as one slapped once.</para>
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
