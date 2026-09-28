// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>One object standing in the world: a bed, a chicken, a pile of gold, a
/// tombstone. The instance, where <see cref="Data.Model.ObjectDef"/> is the definition.
/// <para>One type for every kind, because DK2's object is a common header plus an
/// <b>18-byte union</b> of per-kind state tagged by the definition id (objects.md §7,
/// Q130). Ours is flat, and the union's behavioural consequence is followed instead:
/// <b>every field says which kind may write it, and every other kind leaves it zero</b>,
/// which <see cref="HashInto"/> enforces by hashing all of them unconditionally. Fields arrive with the mechanic that reads them, as in <see cref="TileState"/>; DK2's header has eight more, listed in the research doc.</para></summary>
public sealed class ObjectState
{
    /// <summary>Unique within a level, in spawn order, <b>never reused</b> (like
    /// <see cref="CreatureState.Id"/>): a recycled id lets a stale reference point silently at a different thing.</summary>
    public int Id;

    /// <summary>Which of the 145 definitions this is, and in DK2 <b>the union's tag</b>:
    /// the original branches on this byte with hardcoded literals (4 the spell book, 9
    /// the chicken, 74 the boulder). Rules here should still <b>ask the type flag, not
    /// the id</b>, as the original's own dispatch does — the <c>Lair</c> branch is chosen by flag bit, which is how one branch serves all twelve bed objects (objects.md §7).</summary>
    public byte ObjectTypeId;

    /// <summary>Whose object it is — a bed belongs to the keeper whose creature sleeps in
    /// it. DK2's <c>WhoGetsThisFromADrop</c> is not an owner but the creature a dropped chicken or
    /// pile is for, and is not carried: ours delivers in the drop's own tick (<see cref="DropDeliveryRules"/>).</summary>
    public byte OwnerPlayerId;

    /// <summary>The tile it stands on, as an index into <see cref="WorldState"/>'s tiles.
    /// <b>A tile rather than a position</b>, for now: DK2 gives an object a sub-tile
    /// position and a <c>MoveTo</c> block because chickens wander and boulders roll, so the finer position arrives with the first mechanic that moves one.</summary>
    public int TileIndex;

    /// <summary><b>Union member: <c>Lair</c>.</b> Which creature owns this bed, 0 for
    /// nobody's, and zero on every object that is not a bed. DK2's own
    /// <c>Lair.CreatureId</c> at the union's first offset, and the <b>only</b> copy of the bed-ownership fact — <see cref="LairRules"/> derives the rest (Q130, objects.md §7).</summary>
    public int CreatureId;

    /// <summary><b>Union member: <c>Gold</c>.</b> How much gold this pile is; zero on
    /// every object that is not gold. DK2's <c>Gold</c> member is a dword at the union's
    /// first offset — <i>the same four bytes</i> <c>Lair</c> reads a creature id out of,
    /// which is why a rule must ask what an object is before believing either field.
    /// Deviation: the original stores treasury gold as a type-3 object on the tile, where this keeps it in <see cref="TileState.GoldHeld"/> (ADR-0015) and only floor piles here (gold-on-the-floor.md §2).</summary>
    public int GoldHeld;

    /// <summary>The id of the worker carrying this object, or -1 — the other end of
    /// <see cref="CreatureState.CarriedObjectId"/>. Both ends are stored (ADR-0063): the
    /// carrier is asked what it holds on arrival, the object whether anybody already has it, so two imps do not cross the dungeon for one book.</summary>
    public int CarriedByCreatureId = -1;

    /// <summary>A per-object countdown, in ticks. Today only an <b>egg</b> uses it, for
    /// the two seconds before it hatches. DK2 keeps it in the object's union
    /// (objects.md §7); this spends a field, a union member read by the wrong rule being the mistake a union is best at causing.</summary>
    public int TimerTicks;

    /// <summary>In a keeper's hand, and therefore out of the dungeon — the object half of
    /// <see cref="CreatureState.InHand"/>, distinct from
    /// <see cref="CarriedByCreatureId"/> on purpose: an imp carrying a chicken walks
    /// through the dungeon with it, a keeper holding one has removed it from the world.
    /// <b>Nothing may find, eat or decay a held object</b>, and every search that could see it tests this first, because <see cref="TileIndex"/> stays where the object was lifted from. Which keeper holds it is in <see cref="PlayerState.Hand"/>.</summary>
    public bool InHand;

    /// <summary>Placed and owned by a room layout, rather than by a level record,
    /// creature or keeper. DK2 keeps this as a dword on the object instance and a
    /// separate linked list on the room (Q270).</summary>
    public bool AutoPlaced;

    /// <summary>A switch's position. Only the Casino's lever has one: 0 generous (Smiley), 1
    /// stingy ($). DK2 keeps it on the room (<c>room+0x4A</c>); here it rides on the lever, so a
    /// reshape that moves the lever resets it (ADR-0188).</summary>
    public byte Setting;

    /// <summary><b>Union member: the research book</b> (object 4, the Spell Book). The
    /// research kind the book holds, DK2's <c>+0x14A</c>: 1 room, 2 door, 3 trap, 4 spell,
    /// and zero on every object that is not a book. A book on a Library shelf is what
    /// keeps its item available (research-tree.md §4).</summary>
    public byte ResearchKind;

    /// <summary><b>Union member: the research book.</b> The type id within
    /// <see cref="ResearchKind"/>, DK2's <c>+0x14E</c>; zero on every object that is not a
    /// book.</summary>
    public byte ResearchTypeId;

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
        hash.Add(InHand);
        hash.Add(AutoPlaced);
        hash.Add(Setting);
        hash.Add(ResearchKind);
        hash.Add(ResearchTypeId);
    }
}
