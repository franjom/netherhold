// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The level-statistics counters' writers and the level-end sample of
/// <c>Player_EndLevelStatistics</c> (<c>0x004BE630</c>); docs/research/level-statistics.md.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/level-statistics.md")]
public static class LevelStatisticsRules
{
    private const byte FirstSpecial = 117, LastSpecial = 129;

    /// <summary><c>Room_TransferOwnership</c> at <c>0x004E5890</c>/<c>0x004E58D1</c>: the new owner
    /// counts a room unless it is a bridge (types 8, 17, 22), and every type adds slabs × cost / 100.</summary>
    public static void CreditRoomTakeover(WorldState world, RoomDef room, int slabs, PlayerId newOwner)
    {
        if (!world.HasPlayer(newOwner)) return;
        ref PlayerLevelStatistics stats = ref world.Player(newOwner).Statistics;
        if (room.TypeId is not (RoomTypeId.WoodenBridge or RoomTypeId.StoneBridge or RoomTypeId.HeroStoneBridge))
            stats.RoomsTakenOver++;
        stats.RoomExtraPoints += slabs * room.Cost / 100;
    }

    /// <summary>The death handler's tally after the hero count (<c>0x0048830B</c>–<c>0x00488336</c>):
    /// a non-hero victim of another owner counts for the credited killer, and every counted death
    /// for the victim's owner, credited or not.</summary>
    public static void CreditDeath(WorldState world, CreatureDef creature, CreatureState state, byte credited)
    {
        if (state.Health > 0 || state.DeathUncounted) return;
        if (credited != 0 && !creature.Flags.HasFlag(CreatureFlags.IsGood) && credited != state.OwnerPlayerId
            && world.HasPlayer(credited))
            world.Player(credited).Statistics.EnemyCreaturesKilled++;
        if (world.HasPlayer(state.OwnerPlayerId)) world.Player(state.OwnerPlayerId).Statistics.OwnCreaturesKilled++;
    }

    /// <summary><c>0x004DA7B2</c>: the destroyer of a heart, when it is not the heart's owner.</summary>
    public static void CreditHeartDestroyed(WorldState world, PlayerId owner, PlayerId destroyer)
    {
        if (destroyer != owner && world.HasPlayer(destroyer)) world.Player(destroyer).Statistics.DungeonHeartsDestroyed++;
    }

    /// <summary>The level-end part of <c>0x004BE630</c>, once per player: samples mana, creatures and
    /// training, adds the specials still held and the creatures left dying, sets <c>LevelWon</c> by
    /// status and takes the land count. Our <c>LandOwned</c> counts owned tiles at the end where DK2
    /// keeps <c>DungeonArea</c> live through every owner change; the two agree if the map's initial
    /// tiles count, which is STRONGLY INFERRED.</summary>
    public static void Sample(WorldState world, GameDatabase database, PlayerId playerId, byte status)
    {
        ref PlayerState player = ref world.Player(playerId);
        ref PlayerLevelStatistics stats = ref player.Statistics;
        if (stats.Sampled) return;
        stats.ManaStored = player.Mana;

        int owned = 0, levels = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            // A body stays in our list with its owner; DK2's corpse is another thing, off the player's list.
            if (CreatureDeathRules.IsCorpse(creature)) continue;
            if (creature.OwnerPlayerId == playerId)
            {
                owned++;
                levels += creature.Level;
                continue;
            }
            // 0x004BE98D: another player's creature left dying by this player's blow. DK2 tests +0x3E6 bit
            // 0x200000; the collapsed state stands in for it (a creature at 0 health not yet down is missed).
            if (creature.HealthSourcePlayerId != playerId.Value || !CreatureDeathRules.IsDying(creature)) continue;
            if (creature.OwnerPlayerId == PlayerIds.Good) player.HeroesDestroyed++;
            else stats.EnemyCreaturesKilled++;
        }
        stats.CreaturesCommanded = owned;
        stats.CreatureTraining = owned == 0 ? 0 : (int)(((long)levels << 12) / owned);

        foreach (ObjectState item in world.Objects)
            if (item.OwnerPlayerId == playerId.Value && item.ObjectTypeId is >= FirstSpecial and <= LastSpecial)
                stats.SpecialsFound++;

        stats.LevelWon = status == PlayerStatus.Won;
        int land = 0;
        for (int i = 0; i < world.TileCount; i++)
            if (world.TileAtIndex(i).OwnerPlayerId == playerId.Value) land++;
        stats.LandOwned = land;
        stats.EndTurn = world.CurrentTick.Value;
        stats.AtVerdict = new LevelCounters
        {
            DungeonHeartsDestroyed = stats.DungeonHeartsDestroyed, EnemyCreaturesKilled = stats.EnemyCreaturesKilled,
            HeroesDestroyed = player.HeroesDestroyed, RoomsTakenOver = stats.RoomsTakenOver, RoomExtraPoints = stats.RoomExtraPoints,
            GoldMined = player.GoldMined, ItemsManufactured = player.ItemsManufactured, CreaturesConverted = stats.CreaturesConverted,
            OwnCreaturesKilled = stats.OwnCreaturesKilled, MoneySpent = stats.MoneySpent, ThingsResearched = stats.ThingsResearched,
            SpecialsFound = stats.SpecialsFound,
        };
        stats.Sampled = true;
    }

    /// <summary>The game turn of the verdict as DK2 numbers it: its turn 1 is our tick 0.</summary>
    public static long NativeEndTurn(in PlayerLevelStatistics stats) => stats.EndTurn + 1;

    /// <summary>The block <c>0x00561E00</c> copies to the profile at the verdict (valid once sampled), with <c>TimeTaken</c> as the verdict
    /// turn over the level's TicksPerSecond (0 when that is 0, <c>0x004BE6BD</c>).</summary>
    public static LevelStatisticsBlock Block(in PlayerState player, int levelTicksPerSecond)
    {
        PlayerLevelStatistics s = player.Statistics;
        LevelCounters c = s.AtVerdict;
        return new LevelStatisticsBlock
        {
            DungeonHeartsDestroyed = (uint)c.DungeonHeartsDestroyed, EnemyCreaturesKilled = (uint)c.EnemyCreaturesKilled,
            HeroesDestroyed = (uint)c.HeroesDestroyed, RoomsTakenOver = (uint)c.RoomsTakenOver,
            RoomExtraPoints = (uint)c.RoomExtraPoints, LandOwned = (uint)s.LandOwned, GoldMined = (uint)c.GoldMined,
            ManaStored = s.ManaStored, ItemsManufactured = (uint)c.ItemsManufactured,
            CreaturesCommanded = (uint)s.CreaturesCommanded, CreaturesConverted = (uint)c.CreaturesConverted,
            CreatureTraining = (uint)s.CreatureTraining, LevelWon = s.LevelWon, OwnCreaturesKilled = (uint)c.OwnCreaturesKilled,
            MoneySpent = (uint)c.MoneySpent, ThingsResearched = (uint)c.ThingsResearched,
            TimeTaken = levelTicksPerSecond == 0 ? 0 : (uint)(NativeEndTurn(s) / levelTicksPerSecond),
            SpecialsFound = (byte)c.SpecialsFound,
        };
    }
}
