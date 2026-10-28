// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The level-statistics block (docs/research/level-statistics.md).</summary>
public class LevelStatisticsTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static WorldState NewWorld(GameDatabase database) =>
        WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    [Fact]
    public void ADeathCountsForTheKillerUnlessAHeroOrItsOwnAndAlwaysForTheVictimsOwner()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        CreatureDef goblin = database.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDef knight = database.CreatureById(Dk2TerrainFixture.KnightCreatureId);
        CreatureState Dead(byte owner) { var c = world.SpawnCreature(database, goblin.CreatureId, owner, new TilePos(2, 2)); c.Health = 0; return c; }

        LevelStatisticsRules.CreditDeath(world, goblin, Dead(Rival), Keeper);
        LevelStatisticsRules.CreditDeath(world, goblin, Dead(Keeper), Keeper);
        LevelStatisticsRules.CreditDeath(world, knight, Dead(PlayerIds.Good), Keeper);
        LevelStatisticsRules.CreditDeath(world, goblin, Dead(Rival), 0);
        CreatureState uncounted = Dead(Rival); uncounted.DeathUncounted = true;
        LevelStatisticsRules.CreditDeath(world, goblin, uncounted, Keeper);
        CreatureState alive = Dead(Rival); alive.Health = 1;
        LevelStatisticsRules.CreditDeath(world, goblin, alive, Keeper);

        Assert.Equal(1, world.Player(Keeper).Statistics.EnemyCreaturesKilled);
        Assert.Equal(1, world.Player(Keeper).Statistics.OwnCreaturesKilled);
        Assert.Equal(2, world.Player(Rival).Statistics.OwnCreaturesKilled);
    }

    [Fact]
    public void TheVerdictSamplesTheEndStateOnceAndTheBlockKeepsTheNativeByteLayout()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        CreatureDef goblin = database.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        world.SpawnCreature(database, goblin.CreatureId, Keeper, new TilePos(2, 2)).Level = 2;
        world.SpawnCreature(database, goblin.CreatureId, Keeper, new TilePos(3, 2)).Level = 3;
        world.Player(Keeper).Mana = 1234;
        world.Player(Keeper).GoldMined = 500;
        world.TileAtIndex(0).OwnerPlayerId = Keeper;
        world.TileAtIndex(1).OwnerPlayerId = Keeper;

        Assert.True(world.EndGame(database, Keeper, PlayerStatus.Won));
        world.Player(Keeper).Mana = 1;
        Assert.False(world.EndGame(database, Keeper, PlayerStatus.Lost));
        PlayerLevelStatistics stats = world.Player(Keeper).Statistics;
        Assert.True(stats.LevelWon);
        Assert.Equal(1234, stats.ManaStored);
        Assert.Equal(2, stats.CreaturesCommanded);
        Assert.Equal(0x2800, stats.CreatureTraining);
        Assert.Equal(Enumerable.Range(0, world.TileCount).Count(i => world.TileAtIndex(i).OwnerPlayerId == Keeper), stats.LandOwned);

        Assert.Equal(0, stats.EndTurn);
        world.Player(Keeper).Statistics.EndTurn = 4 * 125 + 2; // DK2's turn 503
        LevelStatisticsBlock block = LevelStatisticsRules.Block(world.Player(Keeper), levelTicksPerSecond: 4);
        Assert.Equal(125u, block.TimeTaken);
        Assert.Equal((2u, 50u), block.TrainingParts);
        uint[] dwords = block.ToDwords();
        Assert.Equal(1u, dwords[12] & 0xFF);
        Assert.Equal(125u, (dwords[15] >> 8) | (dwords[16] << 24));
        Assert.Equal(block, LevelStatisticsBlock.FromDwords(dwords, 0));
    }

    [Fact]
    public void TheEvilRatingWeighsEachFieldAndAddsRoomPointsUnweighted()
    {
        var block = new LevelStatisticsBlock
        {
            DungeonHeartsDestroyed = 1, EnemyCreaturesKilled = 2, HeroesDestroyed = 3, RoomsTakenOver = 4,
            RoomExtraPoints = 7, LandOwned = 10, GoldMined = 100, ManaStored = 50, ItemsManufactured = 2,
            CreaturesCommanded = 5, CreaturesConverted = 1, CreatureTraining = (3 << 12) | 0x800,
        };
        // 1000 + 80 + 150 + 20 + 7 + 50 + 100 + 100 + 10 + 100 + 40 + 3 × 50 + trunc(50 × 50 / 100).
        Assert.Equal(1832u, block.TotalEvilRating);
    }

    [Fact]
    public void ABridgeTakenOverAddsOnlyRoomPoints()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        RoomDef bridge = database.Rooms[RoomTypeId.WoodenBridge] with { Cost = 250 };
        RoomDef lair = database.Rooms[RoomTypeId.Lair] with { Cost = 150 };
        LevelStatisticsRules.CreditRoomTakeover(world, bridge, 2, Keeper);
        LevelStatisticsRules.CreditRoomTakeover(world, lair, 3, Keeper);
        Assert.Equal(1, world.Player(Keeper).Statistics.RoomsTakenOver);
        Assert.Equal(5 + 4, world.Player(Keeper).Statistics.RoomExtraPoints);
    }

    [Fact]
    public void StatisticsAreHashedAndSurviveASnapshot()
    {
        GameSimulation a = TestWorld.Simulation(seed: 7), b = TestWorld.Simulation(seed: 7);
        PlayerId keeper = b.World.PlayerAtIndex(b.World.PlayerCount - 1).PlayerId;
        b.World.Player(keeper).Statistics.MoneySpent = 9;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(b), b.Database);
        Assert.Equal(9, restored.World.Player(keeper).Statistics.MoneySpent);
        Assert.Equal(b.StateHash(), restored.StateHash());
    }

    [Fact]
    public void TheEndPassCountsCreaturesLeftDyingAndSkipsBodiesAndTheVerdictFreezesTheCounters()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        byte goblin = Dk2TerrainFixture.GoblinCreatureId;
        world.SpawnCreature(database, goblin, Keeper, new TilePos(2, 2)).Level = 4;
        CreatureState body = world.SpawnCreature(database, goblin, Keeper, new TilePos(3, 2));
        body.Level = 10; body.Dk2StateId = CreatureStateMachine.NoState;     // a corpse keeps its owner here
        CreatureState rival = world.SpawnCreature(database, goblin, Rival, new TilePos(4, 2));
        rival.Dk2StateId = CreatureDeathRules.DyingState; rival.HealthSourcePlayerId = Keeper;
        CreatureState hero = world.SpawnCreature(database, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(5, 2));
        hero.Dk2StateId = CreatureDeathRules.DyingState; hero.HealthSourcePlayerId = Keeper;
        CreatureState unrelated = world.SpawnCreature(database, goblin, Rival, new TilePos(6, 2));
        unrelated.Dk2StateId = CreatureDeathRules.DyingState; unrelated.HealthSourcePlayerId = Rival;
        world.Player(Keeper).Statistics.MoneySpent = 40;

        Assert.True(world.EndGame(database, Keeper, PlayerStatus.Lost));
        world.Player(Keeper).Statistics.MoneySpent = 90;
        world.Player(Keeper).GoldMined = 1000;
        PlayerLevelStatistics stats = world.Player(Keeper).Statistics;
        Assert.Equal(1, stats.CreaturesCommanded);
        Assert.Equal(4 << 12, stats.CreatureTraining);
        Assert.Equal(1, stats.EnemyCreaturesKilled);
        Assert.Equal(1, world.Player(Keeper).HeroesDestroyed);
        Assert.False(stats.LevelWon);
        LevelStatisticsBlock block = LevelStatisticsRules.Block(world.Player(Keeper), 4);
        Assert.Equal(40u, block.MoneySpent);
        Assert.Equal(0u, block.GoldMined);
        Assert.Equal(1u, block.HeroesDestroyed);
    }
}
