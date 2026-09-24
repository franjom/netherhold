// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Casino: a lever with two settings, what a gambler's second is worth on each,
/// and what the room settles at the lever every four seconds (ADR-0187, ADR-0188;
/// <c>state-category-hooks.md</c> §3.1). Generous (Smiley) calms and pays the gambler, and the
/// keeper pays for it; stingy ($) angers and fleeces the gambler, and the gold lands beneath
/// the lever.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/state-category-hooks.md")]
public static class CasinoRules
{
    /// <summary>The lever's settings, DK2's room byte <c>+0x4A</c>. A new Casino is generous.</summary>
    public const byte Generous = 0;
    public const byte Stingy = 1;

    /// <summary>The room's settlement runs every four game seconds (turn modulo variable 19 × 4).</summary>
    public const int SettlementSeconds = 4;

    /// <summary>The purse a gambler must beat: <c>abs(GamblingModifyCreatureMoneyMoneyPerSecond)</c>,
    /// 8. Tested by the Drink job, the enter hook <c>0x0047ACB0</c> and every second of the
    /// update <c>0x0047AD70</c>, which sends a creature at or below it idle.</summary>
    public static int Stake(GameDatabase database) =>
        Math.Abs(database[VariableTypeId.GamblingModifyCreatureMoneyMoneyPerSecond]);

    /// <summary>A gambler's second on this setting, to buckets 5, 0 and 3, Treatment, Company and
    /// Work: variable 201 (−2000) generous, 202 (+100) stingy.</summary>
    public static CreatureAnger AngerPerSecond(GameDatabase database, byte setting)
    {
        int per = database[setting == Stingy
            ? VariableTypeId.GamblingModifyCreatureAngerMoneyPerSecond
            : VariableTypeId.GamblingModifyCreatureAngerSmilesPerSecond];
        return new CreatureAnger { Treatment = per, Company = per, Work = per };
    }

    /// <summary>What the same second does to the purse: variable 199 (+1) generous, 200 (−8)
    /// stingy.</summary>
    public static int PursePerSecond(GameDatabase database, byte setting) =>
        database[setting == Stingy
            ? VariableTypeId.GamblingModifyCreatureMoneyMoneyPerSecond
            : VariableTypeId.GamblingModifyCreatureMoneySmilesPerSecond];

    /// <summary>What one gambler is worth to the lever's tile at a settlement, <c>0x004D7180</c>'s
    /// argument: four seconds of the purse's change, negated. Positive is gold put down beneath
    /// the lever (stingy, +32); negative is gold the keeper pays out (generous, −4).</summary>
    public static int SettlementPerGambler(GameDatabase database, byte setting) =>
        -PursePerSecond(database, setting) * SettlementSeconds;

    /// <summary>This room's lever, or null. A Casino without one is skipped by the settlement
    /// (<c>room+0x53</c> zero), as DK2's is.</summary>
    public static ObjectState? Lever(WorldState world, RoomInstance room)
    {
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.ObjectTypeId != AutoFurnitureRules.CasinoLeverObjectId || thing.InHand) continue;
            if (thing.OwnerPlayerId != room.OwnerPlayerId) continue;
            if (EatingRules.Contains(room, thing.TileIndex)) return thing;
        }
        return null;
    }

    /// <summary>The setting of the Casino this creature stands in: generous when it has no lever.</summary>
    public static byte SettingUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        TilePos at = creature.Tile;
        if (!world.Contains(at)) return Generous;
        RoomInstance? room = RoomIndex.RoomAt(world, database, (at.Y * world.Width) + at.X);
        return room is not null && Lever(world, room) is { } lever ? lever.Setting : Generous;
    }
}
