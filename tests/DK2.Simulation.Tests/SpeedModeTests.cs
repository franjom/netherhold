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

    // 0x00488930 halves the speed on water (0x0048970B) before the state handler runs; a handler's
    // 0x004887D0 then writes the definition's speed, and the next update halves it again. This test
    // asserts the cap on a Goblin; the capture's case is the fight-entry test below.
    [Fact]
    public void ASpeedSetByAHandlerIsNotHalvedByWaterUntilTheNextTurn()
    {
        var (db, world, goblin) = Open();
        world.Tile(10, 10).TerrainId = Dk2TerrainFixture.Water;
        CreatureDef definition = db.CreatureById(goblin.CreatureTypeId);
        int run = CreatureMovementSystem.MaxSpeedOf(definition, CreatureSpeedMode.Run);
        int walk = CreatureMovementSystem.MaxSpeedOf(definition, CreatureSpeedMode.Walk);
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);

        sim.Tick();
        Assert.Equal(((walk >> 4) / 2) << 4, goblin.Body.MaxSpeed);

        goblin.SetSpeedMode(CreatureSpeedMode.Run, sim.CurrentTick.Value);
        sim.Tick();
        Assert.Equal(run, goblin.Body.MaxSpeed);

        sim.Tick();
        Assert.Equal(((run >> 4) / 2) << 4, goblin.Body.MaxSpeed);

        // A mode written without the handler's mark is halved on its own turn.
        goblin.SpeedMode = CreatureSpeedMode.Walk;
        sim.Tick();
        Assert.Equal(((walk >> 4) / 2) << 4, goblin.Body.MaxSpeed);
    }

    // Level 5 seed 1, turn 1728: hero 434 wading in water enters its fight; its body moves at 17772
    // against a cap of 17680 that turn and at about 8870 from the next (435 the same on 1731). State
    // 199's entry-turn run speed is the call (0x0045EBC9).
    [Fact]
    public void AFightEnteredInWaterRunsItsFirstTurnUnhalved_ConfirmedInCapture()
    {
        var (db, world, goblin) = Open();
        world.Tile(10, 10).TerrainId = Dk2TerrainFixture.Water;
        world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(13, 10));
        int run = CreatureMovementSystem.MaxSpeedOf(db.CreatureById(goblin.CreatureTypeId), CreatureSpeedMode.Run);
        var sim = new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);

        int turns = 0;
        while (goblin.SpeedSetTick < 0 && turns++ < 20) sim.Tick();
        Assert.Equal(sim.CurrentTick.Value - 1, goblin.SpeedSetTick);
        Assert.Equal(CreatureSpeedMode.Run, goblin.SpeedMode);
        Assert.Equal(run, goblin.Body.MaxSpeed);
    }

    [Fact]
    public void TheSpeedSetTurnIsHashedSavedAndCopied()
    {
        var (db, world, goblin) = Open();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        goblin.SetSpeedMode(CreatureSpeedMode.Walk, 7);
        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(7, restored.World.CreatureById(goblin.Id)!.SpeedSetTick);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var (_, copy, _) = Open();
        copy.CopyFrom(world);
        Assert.Equal(7, copy.CreatureById(goblin.Id)!.SpeedSetTick);
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
