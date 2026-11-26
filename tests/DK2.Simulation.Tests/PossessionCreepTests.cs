// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Creeping (possession-client.md §2, combat-target-selection.md §2): the possessor's creep
/// key sets the bit, leaving clears it, and its readers judge the creeper at a share of its distance.</summary>
public sealed class PossessionCreepTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte CreeperCreatureId = 52;

    /// <summary>A Goblin with the shipped per cent, 50; the fixture's own Goblin carries 0.</summary>
    private static CreatureDef Creeper(int percent = 50) => Dk2TerrainFixture.Goblin() with
    {
        Name = "Creeper", CreatureId = CreeperCreatureId, CreepingDistancePercent = percent,
    };

    private static (GameDatabase Db, WorldState World, CreatureState Me) Open(int percent = 50)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor(), Creeper(percent));
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(30, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }

        CreatureState me = world.SpawnArrived(db, CreeperCreatureId, Keeper, new TilePos(5, 10));
        me.Possessed = true;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        return (db, world, me);
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        sim.Tick();
        return sim;
    }

    [Fact]
    public void TheCreepCommandSetsAndClearsTheBit_StronglyInferredFromBinary()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new SetCreepingCommand(Keeper, true)));
        Assert.True(me.Creeping);
        sim.Tick(InputFrame.Of(new SetCreepingCommand(Keeper, false)));
        Assert.False(me.Creeping);
    }

    [Fact]
    public void LeavingPossessionEndsCreeping_StronglyInferredFromBinary()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(new SetCreepingCommand(Keeper, true)));

        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));

        Assert.False(me.Creeping);
    }

    [Theory]
    [InlineData(false, 50, 4096 * 25, 4096 * 25)]     // not creeping: untouched
    [InlineData(true, 50, 4096 * 25, 4096 * 25 / 2)]
    [InlineData(true, 50, 3, 1)]                      // truncates
    [InlineData(true, 0, 4096 * 25, 0)]               // the Horned Reaper's per cent
    [InlineData(true, 50, 50_000_000, 25_000_000)]    // the product wraps negative as a signed word; the divide is unsigned
    [InlineData(true, 50, 100_000_000, 7_050_327)]    // and it keeps only its low 32 bits
    public void ACreepersDistanceIsItsPerCentOfTheTrueOne_StronglyInferredFromBinary(bool creeping, int percent, int distance, int expected)
    {
        (GameDatabase? db, WorldState _, CreatureState? me) = Open(percent);
        me.Creeping = creeping;

        Assert.Equal(expected, PossessionRules.CreepingDistance(db, me, distance));
    }

    /// <summary>A watcher five tiles off with four tiles of sight does not see the sneaker; creeping
    /// halves the squared distance (25 to 12.5 tiles squared, under 16), so it does.</summary>
    [Theory]
    [InlineData(false, Rival)]
    [InlineData(true, 0)]
    public void ACreepingSneakerIsJudgedCloserByAWatcher_StronglyInferredFromBinary(bool creeping, byte disguise)
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        world.Tile(20, 15).OwnerPlayerId = Rival;
        GameSimulation sim = Sim(db, world);
        if (creeping)
        {
            sim.Tick(InputFrame.Of(new SetCreepingCommand(Keeper, true)));
        }

        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 20, 15)));

        Assert.Equal(disguise, me.SneakingAs);
    }

    [Fact]
    public void CreepingSurvivesASnapshot()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        me.Creeping = true;

        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);

        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.True(copy.World.CreatureById(me.Id)!.Creeping);
    }
}
