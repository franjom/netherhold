// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// One object standing in the world: a bed, a chicken, a pile of gold, a
/// tombstone. The instance, where <see cref="Data.Model.ObjectDef"/> is the
/// definition.
///
/// <para><b>One type for every kind, because that is what DK2 has.</b> The
/// original's object is a common header followed by an <b>18-byte union</b> of
/// per-kind state, tagged by the object's own definition id — the game's debug
/// dump prints the header <c>Union Data:</c> in as many words and then one of
/// twenty-three blocks, every one of them reading from the same offset. A
/// <c>Lair</c> block carries a creature id; a <c>Gold</c> block an amount; a
/// <c>Chicken</c> block a pecking timer and a scatter countdown. See
/// <c>docs/research/objects.md</c> §7, and Q130 for the decision.</para>
///
/// <para><b>Ours is flat rather than a union</b>, and that is deliberate. A
/// union in C# would either be unsafe bytes or a class hierarchy, and both cost
/// more than they buy: the fidelity requirement is on <i>behaviour</i>, not on
/// layout (brief item 5), and the behavioural content of a union is that at most
/// one kind's state is live at a time — which a flat record with a documented
/// owner per field satisfies exactly. The union's real consequence is followed
/// instead: <b>every field says which kind may write it, and every other kind
/// leaves it zero.</b> Hashing all of them unconditionally is what enforces
/// that; a kind-dependent hash would let a stray write hide.</para>
///
/// <para><b>Fields arrive with the mechanic that reads them</b>, the same rule
/// <see cref="TileState"/> follows. DK2's header has eight more — a room node,
/// a <c>StateId</c> and <c>StateAge</c>, <c>AutoPlaced</c>, an
/// <c>InHand</c>/<c>BeingCarried</c>/<c>Possessed</c> bit trio, a whole
/// <c>MoveTo</c> block for objects that walk — and none of them is here,
/// because nothing reads them yet. They are listed in the research doc so that
/// the next mechanic knows where to look rather than inventing something.</para>
/// </summary>
public sealed class ObjectState
{
    /// <summary>
    /// Unique within a level, handed out in spawn order and never reused —
    /// exactly like <see cref="CreatureState.Id"/>, and for the same reason: an
    /// id that came back would let a stale reference silently point at a
    /// different thing.
    /// </summary>
    public int Id;

    /// <summary>
    /// Which of the 145 definitions this is — and, in DK2, <b>the union's
    /// tag</b>: the original branches on this single byte with hardcoded
    /// literals (4 is the spell book, 9 the chicken, 74 the boulder).
    ///
    /// <para>Rules here should still prefer to ask the definition's type flag,
    /// because the original does too and gets more from it: the
    /// <c>Lair</c> branch is chosen by the flag bit rather than by id, which is
    /// how one branch serves all twelve bed objects. Flags beat ids in the
    /// original's own dispatch order (objects.md §7).</para>
    /// </summary>
    public byte ObjectTypeId;

    /// <summary>
    /// Whose object it is. A bed belongs to the keeper whose creature sleeps in
    /// it; DK2 also carries <c>WhoGetsThisFromADrop</c> separately, for the
    /// question of who owns a thing a keeper drops, which nothing asks yet.
    /// </summary>
    public byte OwnerPlayerId;

    /// <summary>
    /// The tile it stands on, as an index into <see cref="WorldState"/>'s tiles.
    ///
    /// <para><b>A tile rather than a position</b>, for now. DK2 gives an object
    /// a sub-tile position in its base class and a <c>MoveTo</c> block to change
    /// it with, because chickens wander and boulders roll. Every object that
    /// exists today sits at the centre of one tile, so the finer position
    /// arrives with the first mechanic that moves one.</para>
    /// </summary>
    public int TileIndex;

    /// <summary>
    /// <b>Union member: <c>Lair</c>.</b> Which creature owns this bed, or 0 for
    /// nobody's.
    ///
    /// <para>This is DK2's own <c>Lair.CreatureId</c>, a u16 at the union's
    /// first offset. It is the answer to the ownership clash Q130 opened:
    /// <see cref="LairRules"/> used to keep the relation on the creature, as a
    /// tile index, and the original keeps it here. <b>Two copies of one fact is
    /// the shape that has already cost this project three long-lived
    /// defects</b>, so there is now exactly one, and it is this one — see
    /// objects.md §7 for why the object's end wins.</para>
    ///
    /// <para>Zero on every object that is not a bed.</para>
    /// </summary>
    public int CreatureId;

    /// <summary>
    /// <b>Union member: <c>Gold</c>.</b> How much gold this pile is. Zero on
    /// every object that is not gold.
    ///
    /// <para>DK2's <c>Gold</c> member is a single dword at the union's first
    /// offset — the same four bytes the <c>Lair</c> member reads a creature id
    /// out of, which is what a union means and why a rule must ask the
    /// definition what it is holding before it believes either field. The
    /// game's own object dump prints this one as <c>GoldHeld</c>.</para>
    ///
    /// <para><b>Both a treasury's gold and a floor pile are this field.</b> The
    /// original stores a treasury tile's contents as an object of type 3 sitting
    /// on the tile and gold on the floor as an object of type 1; ours keeps
    /// treasury gold in <see cref="TileState.GoldHeld"/> (ADR-0015) and only
    /// floor piles here. See <c>docs/research/gold-on-the-floor.md</c> §2 for
    /// the difference and why it was not worth closing.</para>
    /// </summary>
    public int GoldHeld;

    /// <summary>
    /// The id of the worker carrying this object, or -1 for none — the other
    /// end of <see cref="CreatureState.CarriedObjectId"/>.
    ///
    /// <para>Both ends are stored for the same reason the creature-carry
    /// relation stores both (ADR-0063): the carrier is asked what it is holding
    /// on arrival, and the object is asked whether anybody already has it, so
    /// that two imps do not cross the dungeon for one book.</para>
    /// </summary>
    public int CarriedByCreatureId = -1;

    /// <summary>
    /// A per-object countdown, in ticks. Today only an <b>egg</b> uses it, to
    /// count the two seconds before it hatches.
    ///
    /// <para><b>DK2 keeps this in the object's union</b>, which is where a
    /// per-kind timer belongs (<c>objects.md</c> §7) — the same eighteen bytes
    /// that hold a bed's creature id and a pile's gold. This project spends a
    /// field on it instead, because a union member read by the wrong rule is
    /// the mistake that union is best at causing.</para>
    /// </summary>
    public int TimerTicks;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(ObjectTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(TileIndex);
        hash.Add(CreatureId);
        hash.Add(GoldHeld);
        hash.Add(CarriedByCreatureId);
        hash.Add(TimerTicks);
    }
}
