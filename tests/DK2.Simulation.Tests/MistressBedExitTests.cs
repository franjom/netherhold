// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class MistressBedExitTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly TilePos Tile = new(2, 2);

    [Theory]
    [InlineData(0, 16)]
    [InlineData(512, -16)]
    public void AReadyMistressUsesTheInferredBedClipClockBeforeWandering(int heading, int offsetY)
    {
        var (sim, creature, bed) = Start();
        bed.Heading = heading;
        creature.ActionUntilTick = 999;
        creature.PosZ = 999; bed.PosZ = 1234;
        StatePlacement.Place(creature, 83);
        sim.Tick();
        Assert.Equal(84, creature.Dk2StateId);
        sim.Run(4); // Thirty frames at30fps require four native turn advances.
        Assert.Equal(84, creature.Dk2StateId);
        sim.Tick();
        Assert.Equal(87, creature.Dk2StateId);
        Assert.Equal((bed.PosX, bed.PosY + offsetY), (creature.PosX, creature.PosY));
        Assert.Equal(1234, creature.PosZ);
    }

    [Fact]
    public void TheExitNeitherPaysDebtNorRefreshesTheLastSleepStamp()
    {
        var (sim, creature, _) = Start();
        StatePlacement.Place(creature, 84);
        creature.Health = 1;
        creature.SleepDebtTicks = 9;
        creature.LastSleptTick = new GameTick(-7);
        sim.Run(4);
        Assert.Equal(84, creature.Dk2StateId);
        Assert.Equal(9, creature.SleepDebtTicks);
        Assert.Equal(new GameTick(-7), creature.LastSleptTick);
        sim.Tick();
        Assert.Equal(87, creature.Dk2StateId);
    }

    [Fact]
    public void AnExitWithoutABedKeepsItsState()
    {
        var (sim, creature, _) = Start();
        LairRules.ReleaseBed(sim.World, sim.Database, creature);
        StatePlacement.Place(creature, 84);
        sim.Run(8);
        Assert.Equal(84, creature.Dk2StateId);
    }

    [Fact]
    public void SavingDuringTheInferredExitClockPreservesItsRemainingTime()
    {
        var (sim, creature, _) = Start();
        StatePlacement.Place(creature, 83);
        sim.Run(3);
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new CreatureSleepSystem()]);
        Assert.Equal(84, Assert.Single(copy.World.Creatures).Dk2StateId);
        for (int i = 0; i < 7; i++)
        {
            sim.Tick(); copy.Tick();
            Assert.Equal(sim.StateHash(), copy.StateHash());
        }
        Assert.Equal(87, creature.Dk2StateId);
    }

    [Fact]
    public void TheInferredBedExitLeavesWanderingForTheFollowingTurn()
    {
        var (original, creature, _) = Start();
        var sim = new GameSimulation(original.Database, original.World, 42,
            [new CreatureUpdateSystem(new CreatureSleepSystem(), new CreatureJobSystem())]);
        StatePlacement.Place(creature, 83);
        sim.Run(6);
        Assert.Equal(87, creature.Dk2StateId);
    }

    [Fact]
    public void TheCompletionPoseStampIsSavedAndContributesToTheHash()
    {
        var (sim, creature, _) = Start();
        ulong before = sim.StateHash();
        creature.LastBedExitTick = new GameTick(5);
        Assert.NotEqual(before, sim.StateHash());
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new CreatureSleepSystem()]);
        Assert.Equal(new GameTick(5), Assert.Single(copy.World.Creatures).LastBedExitTick);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    private static (GameSimulation Sim, CreatureState Creature, ObjectState Bed) Start()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[9];
        art[7] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };
        ObjectDef bedDef = basis.ObjectById(22)! with { ObjectId = 23, Name = "Test Mistress Bed", ArtSlots = art };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == 4 ? c with { LairObjectId = 23 } : c),
            basis.Variables, objects: basis.Objects.Values.Append(bedDef));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper.Value]);
        RoomRules.Place(db, ref world.Tile(Tile), db.Room(RoomTypeId.Lair), Keeper);
        CreatureState creature = world.SpawnCreature(db, 4, Keeper, Tile);
        ObjectState bed = LairRules.PlaceBed(world, db, creature, world.TileIndex(Tile));
        bed.PosX = CreatureState.CentreOf(Tile.X); bed.PosY = CreatureState.CentreOf(Tile.Y);
        return (new GameSimulation(db, world, 42, [new CreatureSleepSystem()]), creature, bed);
    }
}
