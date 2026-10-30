// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Gold lying on the floor, the fourth place a keeper's gold can be.</summary>
/// <remarks>
/// <para>Every number is the original's, read out of <c>DKII.EXE</c>
/// (docs/research/gold-on-the-floor.md). A pile is object <b>1</b>, "Gold",
/// carrying the amount in <see cref="ObjectState.GoldHeld"/>, created wherever
/// gold leaves a player's books for the world.</para>
/// <para><see cref="PutDown"/> merges miner overflow; <see cref="FloorGoldLandingRules"/>
/// merges spell-created and Hand-dropped gold at their landing position. Other paths make new objects.
/// <see cref="MaxPile"/> remains unenforced; legacy piles use their tile/cell centre.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public static class FloorGold
{
    /// <summary>The object a pile of gold on the floor is: <b>1, "Gold"</b>. Named by id because
    /// the original names it by id — every creation site passes the literal 1 — and because a type
    /// flag cannot tell the three gold objects apart. <see cref="IsPile"/> still asks the flag, for
    /// the reason objects.md §3 gives.</summary>
    public const byte FloorGoldObjectId = 1;

    /// <summary>The object a treasury tile's gold is in the original: <b>3, "Gold Pile 1"</b>, one
    /// per tile, created on first deposit. Deviates: treasury gold lives in
    /// <see cref="TileState.GoldHeld"/> (ADR-0015), and object 3 exists only once it leaves a store
    /// for the hand: the counter withdrawal and the lift of a store pile (ADR-0534).</summary>
    public const byte TreasuryPileObjectId = 3;

    /// <summary>The gold a creature carries splits into <b>one pile below 1000, two below 3000,
    /// three at 3000 and above</b> — the original's exact thresholds.</summary>
    public const int TwoPilesAbove = 1000;

    /// <inheritdoc cref="TwoPilesAbove"/>
    public const int ThreePilesAbove = 3000;

    /// <summary>The range of each of <c>0x0048F930</c>'s two draws per pile, a tile in fixed point.</summary>
    private const int PileScatter = 0x1000;

    /// <summary>Is this object a pile of gold?</summary>
    public static bool IsPile(GameDatabase database, ObjectState instance)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        return database.ObjectById(instance.ObjectTypeId) is { IsGold: true };
    }

    /// <summary><c>MaxGoldPileOutsideTreasury</c>, shipped as <b>2000</b>. Trap: it is a target
    /// rule, not a limit. Its three readers in the binary are all imp code choosing where to put a
    /// load down — a candidate spot holding a pile <i>strictly greater</i> than this is not chosen
    /// — and nothing clamps a pile to it: a sold treasury tile makes a pile of up to
    /// <c>MaxGoldPerTreasuryTile</c> (3000), and an overflowing deposit one of whatever was left
    /// over.</summary>
    public static int MaxPile(GameDatabase database) =>
        database is null
            ? throw new ArgumentNullException(nameof(database))
            : database[VariableTypeId.MaxGoldPileOutsideTreasury];

    /// <summary>Whether an imp looking for somewhere to put gold down would accept this pile —
    /// <see cref="MaxPile"/>'s only meaning. Strictly greater is what the original tests, so a
    /// pile sitting exactly on the limit is still a valid target.</summary>
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

    /// <summary>How much gold is on the floor anywhere in the world. Not part of any player's
    /// total, which is the original's own accounting: when a treasury tile becomes a floor pile the
    /// amount is subtracted from the player's gold in the same breath. Floor gold belongs to
    /// whoever picks it up, and <see cref="GoldStore.Total"/> counts tiles so already excludes
    /// it.</summary>
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

    /// <summary>Puts <paramref name="amount"/> of gold on the floor of one tile as a new pile, and
    /// returns it. Nothing is merged and nothing is capped — see the class remarks. Zero or
    /// negative is refused with <see langword="null"/> rather than spawning an empty pile, which
    /// the original would never have on the floor: a pile that reaches zero is destroyed.</summary>
    public static ObjectState? Drop(
        WorldState world, GameDatabase database, int tileIndex, int amount, PlayerId ownerPlayerId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (amount <= 0) return null;

        ObjectState pile = world.SpawnObject(database, FloorGoldObjectId, ownerPlayerId, tileIndex);
        pile.GoldHeld = amount;
        return pile;
    }

    /// <summary>The neutral-owner overload, matching the floor-pile creation sites
    /// (gold-on-the-floor.md §8, Q136). Fetch tasks test this owner.</summary>
    public static ObjectState? Drop(
        WorldState world, GameDatabase database, int tileIndex, int amount) =>
        Drop(world, database, tileIndex, amount, PlayerIds.Neutral);

    /// <summary>Puts gold down at a point, DK2's <c>0x004510C0</c>: onto a floor pile already
    /// within 1/64 of a tile of it (<c>0x40</c>, Chebyshev), else as a new pile owned by the
    /// neutral player. Its one caller that matters is the mining strike (<c>0x004971D0</c>),
    /// for the gold its miner cannot hold (Q140, gold-on-the-floor.md §10). Deviates: "at that
    /// point" is "in that third-of-a-tile cell" (<see cref="MiningSlots"/>), and a miner always
    /// stands at the same point of its slot, so the two merge the same piles.</summary>
    public static ObjectState? PutDown(
        WorldState world, GameDatabase database, int tileIndex, byte cell, int amount)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (amount <= 0) return null;

        foreach (ObjectState instance in world.Objects)
        {
            if (!IsLoosePile(instance, tileIndex) || instance.Cell != cell) continue;
            instance.GoldHeld += amount;
            return instance;
        }
        ObjectState? pile = Drop(world, database, tileIndex, amount);
        if (pile is not null) pile.Cell = cell;
        return pile;
    }

    /// <summary>The largest loose floor pile in one cell of a tile, what the miner's two tests
    /// compare with <see cref="MaxPile"/>; 0 for none. <see cref="MiningSlots.AnyCell"/> asks
    /// the whole tile.</summary>
    public static int PileInCell(WorldState world, int tileIndex, byte cell)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        int largest = 0;
        foreach (ObjectState instance in world.Objects)
            if (IsLoosePile(instance, tileIndex) && (cell == MiningSlots.AnyCell || instance.Cell == cell))
                largest = Math.Max(largest, instance.GoldHeld);
        return largest;
    }

    private static bool IsLoosePile(ObjectState instance, int tileIndex) =>
        instance.TileIndex == tileIndex && instance.ObjectTypeId == FloorGoldObjectId
        && !instance.InHand && instance.CarriedByCreatureId < 0;

    /// <summary>Takes up to <paramref name="amount"/> off a pile and returns how much came off,
    /// destroying the pile when it empties.</summary>
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

    /// <summary>How many piles a creature carrying <paramref name="gold"/> leaves behind when it
    /// is destroyed: <b>one below 1000, two below 3000, three at 3000 and above</b>, and none at
    /// all with empty hands.</summary>
    public static int PileCount(int gold) =>
        gold <= 0 ? 0
        : gold < TwoPilesAbove ? 1
        : gold < ThreePilesAbove ? 2
        : 3;

    /// <summary>Drops a creature's gold on the tile it is standing on, empties its hands, and
    /// returns how much actually reached the floor — sometimes less than it was carrying, because
    /// the original divides by the pile count with integer division and gives each pile that share,
    /// destroying the remainder: 2999 carried becomes two piles of 1499. Reproduced, not corrected.
    /// DK2's <c>0x0048F930</c>, called on entering the dying state (<c>0x004596D0</c>,
    /// <c>0x00459560</c>) and again by the death bookkeeping, which finds the hands empty unless
    /// the creature skipped the dying window. DK2 scatters each pile at a random point of the tile,
    /// x then y from the simulation stream; ours takes the same two draws per pile, to keep the
    /// stream in step, but puts the pile at the tile. A creature leaving by a portal keeps its gold (Q137).</summary>
    public static int DropFrom(
        WorldState world, GameDatabase database, CreatureState creature, SimRandom rng)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        int piles = PileCount(creature.GoldHeld);
        if (piles == 0) return 0;

        int each = creature.GoldHeld / piles;
        creature.GoldHeld = 0;
        if (each <= 0) return 0;

        int tileIndex = creature.Tile.Y * world.Width + creature.Tile.X;
        for (int i = 0; i < piles; i++)
        {
            rng.NextInt(PileScatter);
            rng.NextInt(PileScatter);
            Drop(world, database, tileIndex, each);
        }
        return each * piles;
    }
}
