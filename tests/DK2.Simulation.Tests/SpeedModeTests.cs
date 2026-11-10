// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class SpeedModeTests
{
    private static (GameDatabase Db, WorldState World, CreatureState Goblin) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world, world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(10, 10)));
    }

    [Fact]
    public void EachModeCapsTheBodyAtItsOwnDefinitionSpeed()
    {
        var (db, world, goblin) = Open();
        CreatureDef definition = db.CreatureById(goblin.CreatureTypeId);
        CreatureDef paced = definition with { Speed = new Fp32(2000), RunSpeed = new Fp32(3000), ShuffleSpeed = new Fp32(1000) };
        Assert.Equal(SimulationTiming.LoaderRatePerTick(2000) << 4, CreatureMovementSystem.MaxSpeedOf(paced, CreatureSpeedMode.Walk));
        Assert.Equal(SimulationTiming.LoaderRatePerTick(3000) << 4, CreatureMovementSystem.MaxSpeedOf(paced, CreatureSpeedMode.Run));
        Assert.Equal(SimulationTiming.LoaderRatePerTick(1000) << 4, CreatureMovementSystem.MaxSpeedOf(paced, CreatureSpeedMode.Shuffle));
        int shuffle = CreatureMovementSystem.MaxSpeedOf(definition, CreatureSpeedMode.Shuffle);

        goblin.SpeedMode = CreatureSpeedMode.Shuffle;
        goblin.PartySpeedRaw = definition.Speed.Raw;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        sim.Tick();
        Assert.Equal(shuffle, goblin.Body.MaxSpeed);
        Assert.Equal(0, goblin.PartySpeedRaw);
    }

    [Fact]
    public void RunningIsTheRunModeAndClearingItIsTheWalk()
    {
        var (_, _, goblin) = Open();
        goblin.SpeedMode = CreatureSpeedMode.Shuffle;
        Assert.False(goblin.Running);
        goblin.Running = true;
        Assert.Equal(CreatureSpeedMode.Run, goblin.SpeedMode);
        goblin.Running = false;
        Assert.Equal(CreatureSpeedMode.Walk, goblin.SpeedMode);
    }

    [Theory]
    [InlineData(CreatureSpeedMode.Run)]
    [InlineData(CreatureSpeedMode.Shuffle)]
    public void TheModeIsHashedSavedAndCopied(CreatureSpeedMode mode)
    {
        var (db, world, goblin) = Open();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        goblin.SpeedMode = mode;
        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(mode, restored.World.CreatureById(goblin.Id)!.SpeedMode);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var (_, copy, _) = Open();
        copy.CopyFrom(world);
        Assert.Equal(mode, copy.CreatureById(goblin.Id)!.SpeedMode);
    }
}
