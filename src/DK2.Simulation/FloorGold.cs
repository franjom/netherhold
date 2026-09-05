// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Gold lying on the floor: the fourth of the four places a keeper's gold can
/// be, and the one <see cref="GoldStore"/> has been refusing to model since it
/// was written.
///
/// <para><b>Read out of <c>DKII.EXE</c></b> on 2026-09-05 —
/// <c>docs/research/gold-on-the-floor.md</c> has the evidence. Every number
/// here is the original's.</para>
///
/// <para><b>A pile is an object.</b> Object <b>1</b>, named "Gold" in
/// <c>Objects.kwd</c>, carrying the amount in the object union's first field
/// (<see cref="ObjectState.GoldHeld"/>). The original creates one of these
/// wherever gold has to leave a player's books and land in the world: a
/// treasury that could not take a deposit, a treasury tile that stopped being
/// one, a creature that was destroyed while carrying.</para>
///
/// <para><b>Piles do not merge here, because they do not merge there.</b> Every
/// path below creates a <i>new</i> object rather than adding to whatever stands
/// on the tile — which is how a creature carrying 3000 leaves three separate
/// piles. The one place the original does merge is an imp choosing where to put
/// a load down, and that is a rule about which slot an imp walks to rather than
/// about the pile; it arrives with the imp task. <see cref="MaxPile"/> is the
/// variable that rule reads, and it is exposed here <b>unenforced</b> for the
/// same reason: nothing this class does may clamp to it, because the original
/// makes piles bigger than it on two of the three paths below.</para>
///
/// <para><b>Sub-tile position is not modelled</b>, and the original has a great
/// deal of it — a ring of eight quarter-points for an overflowing treasury, a
/// uniform draw across the whole tile for a creature's drop, a twelve-slot
/// layout for an imp. <see cref="ObjectState.TileIndex"/> is a tile, so all
/// three collapse to "on this tile", and the draws that pick between them are
/// not made. That is a deliberate loss of a presentation detail rather than of
/// behaviour — but it is also why <b>this class takes no
/// <see cref="SimRandom"/></b>: reproducing the original's RNG position needs
/// its generator, and it is Q3, not this.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public static class FloorGold
{
    /// <summary>
    /// The object a pile of gold on the floor is: <b>1, "Gold"</b>.
    ///
    /// <para>Named by id because the original names it by id — every one of its
    /// creation sites passes the literal 1 — and because a type flag cannot
    /// tell the three gold objects apart. <see cref="IsPile"/> still asks the
    /// flag, for the reason objects.md §3 gives.</para>
    /// </summary>
    public const byte FloorGoldObjectId = 1;

    /// <summary>
    /// The object a treasury tile's gold is in the original: <b>3, "Gold Pile
    /// 1"</b> — one per tile, created on first deposit.
    ///
    /// <para><b>Ours is not this.</b> Treasury gold lives in
    /// <see cref="TileState.GoldHeld"/> (ADR-0015), which is the same number in
    /// a different place, and no rule here creates object 3. The constant is
    /// recorded so that the difference is written down at the one place a reader
    /// would look for it, rather than inferred from its absence.</para>
    /// </summary>
    public const byte TreasuryPileObjectId = 3;

    /// <summary>
    /// The gold a creature carries splits into <b>one pile below 1000, two
    /// below 3000, three at 3000 and above</b> — the original's exact
    /// thresholds.
    /// </summary>
    public const int TwoPilesAbove = 1000;

    /// <inheritdoc cref="TwoPilesAbove"/>
    public const int ThreePilesAbove = 3000;

    /// <summary>Is this object a pile of gold?</summary>
    public static bool IsPile(GameDatabase database, ObjectState instance)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        return database.ObjectById(instance.ObjectTypeId) is { IsGold: true };
    }

    /// <summary>
    /// <c>MaxGoldPileOutsideTreasury</c>, shipped as <b>2000</b>.
    ///
    /// <para><b>It is a target rule, not a limit</b>, and that distinction is
    /// the whole finding. Its three readers in the binary are all imp code
    /// choosing where to put a load down: a candidate spot holding a pile
    /// <i>strictly greater</i> than this is not chosen. Nothing clamps a pile to
    /// it — a sold treasury tile makes a pile of up to
    /// <c>MaxGoldPerTreasuryTile</c> (3000), and an overflowing deposit makes
    /// one of whatever was left over.</para>
    ///
    /// <para>Exposed and not applied, exactly as
    /// <c>MaxFreeRangeChickensPerPlayer</c> was — and for the reason that one
    /// turned out to be right: applying a plausible cap is unfalsifiable once
    /// it is in, because every test then agrees with it.</para>
    /// </summary>
    public static int MaxPile(GameDatabase database) =>
        database is null
            ? throw new ArgumentNullException(nameof(database))
            : database[VariableTypeId.MaxGoldPileOutsideTreasury];

    /// <summary>
    /// Whether an imp looking for somewhere to put gold down would accept this
    /// pile — <see cref="MaxPile"/>'s only meaning. Strictly greater is what
    /// the original tests, so a pile sitting exactly on the limit is still a
    /// valid target.
    /// </summary>
    public static bool AcceptsMore(GameDatabase database, ObjectState pile)
    {
        if (pile is null) throw new ArgumentNullException(nameof(pile));
        return IsPile(database, pile) && pile.GoldHeld <= MaxPile(database);
    }

    /// <summary>Every pile standing on one tile, appended to <paramref name="into"/>.</summary>
    public static void PilesOn(
        WorldState world, GameDatabase database, int tileIndex, List<ObjectState> into)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (into is null) throw new ArgumentNullException(nameof(into));

        foreach (ObjectState instance in world.Objects)
        {
            if (instance.TileIndex == tileIndex && IsPile(database, instance))
                into.Add(instance);
        }
    }

    /// <summary>How much gold is lying on one tile, across however many piles.</summary>
    public static int OnTile(WorldState world, GameDatabase database, int tileIndex)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        int total = 0;
        foreach (ObjectState instance in world.Objects)
        {
            if (instance.TileIndex == tileIndex && IsPile(database, instance))
                total += instance.GoldHeld;
        }
        return total;
    }

    /// <summary>
    /// How much gold is on the floor anywhere in the world.
    ///
    /// <para><b>This is not part of any player's total</b>, and that is the
    /// original's own accounting rather than a simplification: when a treasury
    /// tile becomes a floor pile the amount is subtracted from the player's
    /// gold in the same breath. Gold on the floor belongs to whoever picks it
    /// up. <see cref="GoldStore.Total"/> counts tiles and therefore already
    /// excludes it.</para>
    /// </summary>
    public static int Total(WorldState world, GameDatabase database)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        int total = 0;
        foreach (ObjectState instance in world.Objects)
        {
            if (IsPile(database, instance)) total += instance.GoldHeld;
        }
        return total;
    }

    /// <summary>
    /// Puts <paramref name="amount"/> of gold on the floor of one tile as a new
    /// pile, and returns it. Nothing is merged and nothing is capped — see the
    /// class remarks.
    ///
    /// <para>Zero or negative is refused with <see langword="null"/> rather than
    /// spawning an empty pile, which the original would never have on the floor:
    /// a pile that reaches zero is destroyed.</para>
    /// </summary>
    public static ObjectState? Drop(
        WorldState world, GameDatabase database, int tileIndex, int amount, byte ownerPlayerId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (amount <= 0) return null;

        ObjectState pile = world.SpawnObject(database, FloorGoldObjectId, ownerPlayerId, tileIndex);
        pile.GoldHeld = amount;
        return pile;
    }

    /// <summary>
    /// The same, for the callers that have no reason to name an owner — every
    /// one of them so far, because nothing yet reads a floor pile's owner.
    ///
    /// <para><b>The owner is a guess and is marked as one.</b> The original
    /// gives every floor pile a player id that the level fixes once at load and
    /// that is emphatically <i>not</i> the room's owner or the dead creature's
    /// keeper — the same global on all three paths. Which id that is has not
    /// been read out (Q136). <see cref="PlayerIds.Neutral"/> is used here
    /// because it is what an unclaimed thing already belongs to in this project
    /// (ADR-0043), and because nothing depends on it yet: the day an imp is
    /// allowed to fetch a rival keeper's dropped gold, this is the field that
    /// decides it, and the question has to be answered first.</para>
    /// </summary>
    public static ObjectState? Drop(
        WorldState world, GameDatabase database, int tileIndex, int amount) =>
        Drop(world, database, tileIndex, amount, PlayerIds.Neutral);

    /// <summary>
    /// Takes up to <paramref name="amount"/> off a pile and returns how much
    /// came off, destroying the pile when it empties.
    /// </summary>
    public static int Take(
        WorldState world, GameDatabase database, ObjectState pile, int amount)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (pile is null) throw new ArgumentNullException(nameof(pile));
        if (!IsPile(database, pile)) return 0;
        if (amount <= 0) return 0;

        int taken = Math.Min(amount, pile.GoldHeld);
        pile.GoldHeld -= taken;
        if (pile.GoldHeld <= 0) world.RemoveObject(pile.Id);
        return taken;
    }

    /// <summary>
    /// How many piles a creature carrying <paramref name="gold"/> leaves behind
    /// when it is destroyed: <b>one below 1000, two below 3000, three at 3000
    /// and above</b>, and none at all with empty hands.
    /// </summary>
    public static int PileCount(int gold) =>
        gold <= 0 ? 0
        : gold < TwoPilesAbove ? 1
        : gold < ThreePilesAbove ? 2
        : 3;

    /// <summary>
    /// Drops a creature's gold on the tile it is standing on, empties its hands,
    /// and returns how much actually reached the floor.
    ///
    /// <para><b>Less than it was carrying, sometimes.</b> The original divides
    /// the gold by the number of piles with integer division and gives every
    /// pile that share, so <b>the remainder is destroyed</b> — 2999 carried
    /// becomes two piles of 1499 and one gold ceases to exist. Reproduced rather
    /// than corrected, per the brief: a quirk is behaviour.</para>
    ///
    /// <para>Called when a creature <b>is destroyed</b>. In the original that is
    /// one function on the teardown path, so it covers every way of leaving the
    /// world rather than dying specifically; ours calls it from death only,
    /// because death is the only teardown this project has built (Q137).</para>
    /// </summary>
    public static int DropFrom(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (creature is null) throw new ArgumentNullException(nameof(creature));

        int piles = PileCount(creature.GoldHeld);
        if (piles == 0) return 0;

        int each = creature.GoldHeld / piles;
        creature.GoldHeld = 0;
        if (each <= 0) return 0;

        int tileIndex = creature.Tile.Y * world.Width + creature.Tile.X;
        for (int i = 0; i < piles; i++) Drop(world, database, tileIndex, each);
        return each * piles;
    }
}
