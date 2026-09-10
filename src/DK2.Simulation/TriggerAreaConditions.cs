// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The seven condition types an <b>action point's</b> own dispatcher computes
/// (<c>0x004B0D70</c>, base 39, range <c>0x1F</c>). Every one measures something over
/// that point's inclusive rectangle; every other type the dispatcher hands to the world.
/// <para>All seven have an implemented path. Type 70's object branch is declined because
/// object possession is not simulation state (Q280). Evidence and confidence per type:
/// docs/research/trigger-dispatch-tables.md.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/trigger-semantics.md")]
public static class TriggerAreaConditions
{
    /// <summary>The types the area dispatcher owns. The rest reach the world evaluator
    /// through its default arm, so they mean there what they mean anywhere.</summary>
    public static bool Owns(byte type) => type is 39 or 40 or 41 or 42 or 62 or 69 or 70;

    /// <summary>Evaluates one area-owned condition against <paramref name="area"/>'s
    /// rectangle.</summary>
    public static TriggerConditionResult Evaluate(
        TriggerRecord record,
        ActionPointState area,
        WorldState world,
        GameDatabase database)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (area is null) throw new ArgumentNullException(nameof(area));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));

        IReadOnlyDictionary<string, long> p = record.Parameters;
        return record.Type switch
        {
            39 => Result(TriggerState.Compare(
                Byte(p, "Comparison"),
                Things(area, world, Byte(p, "ThingType"), Byte(p, "PlayerId"), Byte(p, "TypeId")),
                Int(p, "Value"))),

            40 or 42 => Result(TriggerState.Compare(
                Byte(p, "Comparison"),
                Matching(area, world, database, Byte(p, "PlayerId"), Byte(p, "TerrainId")),
                Int(p, "Value"))),

            41 => Result(TriggerState.Compare(
                EveryTile,
                Matching(area, world, database, Byte(p, "PlayerId"), Byte(p, "TerrainId")),
                Size(area))),

            62 => Result(TriggerState.Compare(
                Byte(p, "Comparison"),
                Tagged(area, world, Byte(p, "PlayerId")),
                Int(p, "Value"))),

            69 => Result(TriggerState.Compare(
                AtLeast,
                Tagged(area, world, Byte(p, "PlayerId")),
                Taggable(area, world, database))),

            70 when Byte(p, "ThingType") != ObjectThingType => Result(
                PossessedCreatureExists(
                    area, world, Byte(p, "PlayerId"), Byte(p, "TypeId"))),

            _ => TriggerConditionResult.NotEvaluated,
        };
    }

    /// <summary>Comparison code 3, equality, which type 41's arm supplies as a literal
    /// where the other types read byte 00 (<c>0x004B0E4A</c>).</summary>
    private const byte EveryTile = 3;

    /// <summary>Comparison code 5, "left is at least right", type 69's literal
    /// (<c>0x004B0EA1</c>).</summary>
    private const byte AtLeast = 5;

    private const byte ObjectThingType = 6;

    /// <summary>Tiles in the rectangle matching an owner, a terrain type, or both — DK2's
    /// <c>FUN_004B07E0</c>, the counter behind types 40, 41 and 42.</summary>
    /// <remarks>
    /// <para>Rule: zero means "any" on each axis, and <b>both</b> zero returns the whole
    /// rectangle without looking at a tile. With a terrain type named, ownership is tested
    /// by owner alone; with none named, the tile must also be <see cref="TerrainDef.IsOwnable"/>.
    /// That asymmetry is the original's, not a simplification.</para>
    /// <para>Ownership compares the condition's player id against the tile's owner. DK2
    /// compares the tile's low twelve bits against a word on the player object; that word
    /// being the player id is STRONGLY INFERRED, not read (Q247).</para>
    /// </remarks>
    private static int Matching(
        ActionPointState area, WorldState world, GameDatabase database, byte player, byte terrain)
    {
        if (player == 0 && terrain == 0) return Size(area);

        int count = 0;
        foreach (TilePos pos in Tiles(area, world))
        {
            ref TileState tile = ref world.Tile(pos);
            if (terrain != 0 && tile.TerrainId != terrain) continue;
            if (player != 0)
            {
                if (tile.OwnerPlayerId != player) continue;
                if (terrain == 0 && !database.TerrainById(tile.TerrainId).IsOwnable) continue;
            }
            count++;
        }

        return count;
    }

    /// <summary>Things standing in the rectangle, filtered by owner and type — DK2's
    /// <c>FUN_004B0620</c> with the predicate <c>FUN_004B0A80</c>, behind type 39. The
    /// largest condition block in the corpus at 183 records.</summary>
    /// <remarks>
    /// <para>Rule: <paramref name="thingType"/> <b>6 counts objects and anything else
    /// counts creatures</b>, and the predicate then reads the creature's or the object's
    /// own type id. Zero means "any" on both other axes. DK2 walks each tile's thing list,
    /// so a held object is not there to count.</para>
    /// <para>The player is matched against <c>Number</c> (<c>+0x10FC</c>), not the
    /// <c>TagId</c> that pairs a player with its root; ours has one id for both
    /// (Q271).</para>
    /// </remarks>
    private static int Things(
        ActionPointState area, WorldState world, byte thingType, byte player, byte typeId)
    {
        int count = 0;
        foreach (TilePos pos in Tiles(area, world))
        {
            int index = (pos.Y * world.Width) + pos.X;
            count += thingType == ObjectThingType
                ? world.Objects.Count(o =>
                    !o.InHand && o.TileIndex == index && Matches(o.OwnerPlayerId, o.ObjectTypeId))
                : world.Creatures.Count(c =>
                    c.Tile == pos && Matches(c.OwnerPlayerId, c.CreatureTypeId));
        }

        return count;

        bool Matches(byte owner, byte type) =>
            (player == 0 || owner == player) && (typeId == 0 || type == typeId);
    }

    /// <summary>Whether the rectangle contains a possessed creature matching the owner
    /// and subtype selectors — type 70's existence walk at <c>0x004B06D0</c>.</summary>
    /// <remarks>DK2 tests the creature's <c>Possessed</c> flag at <c>+0x3E6</c> bit 7.
    /// This simulation stores the other end of that relation on each player. Thing type 6
    /// selects objects instead; that branch is declined because object possession is not
    /// represented (Q280).</remarks>
    private static bool PossessedCreatureExists(
        ActionPointState area, WorldState world, byte player, byte typeId)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            if (!Contains(area, creature.Tile)) continue;
            if (player != 0 && creature.OwnerPlayerId != player) continue;
            if (typeId != 0 && creature.CreatureTypeId != typeId) continue;

            foreach (byte playerId in world.PlayerIds)
                if (world.Player(playerId).PossessedCreatureId == creature.Id) return true;
        }

        return false;
    }

    private static bool Contains(ActionPointState area, TilePos pos) =>
        pos.X >= area.StartX && pos.X <= area.EndX &&
        pos.Y >= area.StartY && pos.Y <= area.EndY;

    /// <summary>Tiles in the rectangle carrying one player's excavation task — DK2's
    /// <c>FUN_004B0960</c>, behind types 62 and 69.</summary>
    /// <remarks>
    /// <para>DK2 counts entries of kinds 1 and 2 in that player's task pool, which
    /// <c>FUN_00512D20</c> creates one per tagged tile and splits only by whether the
    /// terrain holds gold. Both kinds count, so the split does not reach this condition
    /// and our single tag bit is the same quantity.</para>
    /// <para>The pool is keyed by coordinate and never consults the map, so a rectangle
    /// running off the grid costs DK2 nothing. Ours skips those tiles, which is the same
    /// answer.</para>
    /// </remarks>
    private static int Tagged(ActionPointState area, WorldState world, byte player)
    {
        int count = 0;
        foreach (TilePos pos in Tiles(area, world))
            if (world.Tile(pos).IsTaggedBy(player)) count++;
        return count;
    }

    /// <summary>Tiles in the rectangle whose terrain can be tagged at all — DK2's
    /// <c>FUN_004B0A00</c>, which reads terrain flag bit 3, our
    /// <see cref="TerrainFlags.Taggable"/>. Type 69's right-hand side, so that condition
    /// asks whether the player has marked everything here that can be marked.</summary>
    private static int Taggable(ActionPointState area, WorldState world, GameDatabase database)
    {
        int count = 0;
        foreach (TilePos pos in Tiles(area, world))
            if (database.TerrainById(world.Tile(pos).TerrainId).IsTaggable) count++;
        return count;
    }

    /// <summary>The rectangle's tile count, inclusive of both bounds.</summary>
    private static int Size(ActionPointState area) =>
        Span(area.StartX, area.EndX) * Span(area.StartY, area.EndY);

    private static int Span(ushort from, ushort to) => to < from ? 0 : to - from + 1;

    /// <summary>Every tile of the rectangle that is on the map, row by row.</summary>
    /// <remarks>DK2 strides the tile array without a bound check and an inverted or
    /// overhanging rectangle would read past it. No shipped point has one; skipping is a
    /// deviation only such a point could show (Q248).</remarks>
    private static IEnumerable<TilePos> Tiles(ActionPointState area, WorldState world)
    {
        for (int y = area.StartY; y <= area.EndY; y++)
            for (int x = area.StartX; x <= area.EndX; x++)
                if (world.Contains(x, y)) yield return new TilePos(x, y);
    }

    private static byte Byte(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((byte)parameters[name]);

    private static int Int(IReadOnlyDictionary<string, long> parameters, string name) =>
        checked((int)parameters[name]);

    private static TriggerConditionResult Result(bool value) =>
        value ? TriggerConditionResult.Satisfied : TriggerConditionResult.Unsatisfied;
}
