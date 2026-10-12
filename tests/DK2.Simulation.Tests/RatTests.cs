// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class RatTests
{
    private static (GameDatabase Db, WorldState World) Prison()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        ObjectDef rat = basis.Objects.Values.First() with
        {
            Name = "Rat", ObjectId = 11, StartState = 11, Hp = 300,
            Flags = (ObjectFlags)0x210601, Speed = new Fp32(819),
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values.Select(r => r.TypeId == RoomTypeId.Prison ? r with { Construction = TileConstruction.DoubleQuad } : r), basis.Creatures.Values,
            basis.Variables, objects: basis.Objects.Values.Append(rat));
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), PlayerIds.FirstKeeper);
        return (db, world);
    }

    [Fact]
    public void EmptyPrisonGeneratesOneRatAndStopsAtThePlayerPopulationLimit()
    {
        var (db, world) = Prison();
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        for (int i = 0; i < 1000; i++) simulation.Tick();
        ObjectState rat = Assert.Single(world.Objects, o => o.ObjectTypeId == 11);
        Assert.Equal(PlayerIds.FirstKeeper, rat.OwnerPlayerId);
        Assert.Equal(0x1000, rat.PosZ);
        Assert.Equal((byte)11, rat.ProcessState);
        Assert.Empty(world.Creatures);
    }

    [Theory]
    [InlineData(PlayerStatus.Conquered)]
    [InlineData(PlayerStatus.Lost)]
    public void StampedHeartDeathAndDefeatDoNotGateAmbientRatGeneration(byte status)
    {
        var (db, world) = Prison();
        ref PlayerState player = ref world.PlayerAtIndex(2);
        player.KilledByPlayerId = PlayerIds.Neutral;
        player.HeartHealth = 0;
        player.Status = status;
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        for (int i = 0; i < 1000; i++) simulation.Tick();
        Assert.Single(world.Objects, rat => rat.ObjectTypeId == RatRules.RatObjectId);
    }

    [Fact]
    public void TortureTilesIncreaseThePrisonSpawnPopulation()
    {
        var (db, world) = Prison();
        for (int y = 1; y <= 3; y++)
            for (int x = 5; x <= 10; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TortureChamber), PlayerIds.FirstKeeper);
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        for (int i = 0; i < 1000; i++) simulation.Tick();
        Assert.Equal(3, world.Objects.Count(o => o.ObjectTypeId == 11));
        Assert.All(world.Objects, rat => Assert.Equal(Dk2TerrainFixture.PrisonTerrain, world.TileAtIndex(rat.TileIndex).TerrainId));
    }

    [Fact]
    public void TortureWithoutAnyPrisonConsumesOnlyTheChanceRoll()
    {
        var (db, world) = Prison();
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TortureChamber), PlayerIds.FirstKeeper);
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        var expected = new SimRandom(42);
        expected.NextInt(100);
        simulation.Tick();
        Assert.Equal(expected.SaveState(), simulation.Rng.SaveState());
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == 11);
    }

    [Fact]
    public void LargePrisonStillStopsAtSixteenRats()
    {
        var (db, _) = Prison();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 18; y++)
            for (int x = 1; x <= 18; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), PlayerIds.FirstKeeper);
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        for (int i = 0; i < 1000; i++) simulation.Tick();
        Assert.Equal(16, world.Objects.Count(o => o.ObjectTypeId == 11));
    }

    [Fact]
    public void KeeperSpawnRunsAfterTheRatObjectPass()
    {
        var (db, world) = Prison();
        var simulation = new GameSimulation(db, world, 42, [new RatSystem(), new RatSpawnSystem()]);
        for (int i = 0; i < 1000 && world.Objects.Count == 0; i++) simulation.Tick();
        ObjectState rat = Assert.Single(world.Objects);
        Assert.Equal(300, rat.Health);
        simulation.Tick();
        Assert.Equal(299, rat.Health);
    }

    [Fact]
    public void APrisonSmallerThanNineTilesCannotGenerateRats()
    {
        var (db, world) = Prison();
        world.Tile(1, 1).TerrainId = Dk2TerrainFixture.ClaimedPath;
        var simulation = new GameSimulation(db, world, 42, [new RatSpawnSystem()]);
        var expected = simulation.Rng.SaveState();
        for (int i = 0; i < 1000; i++) simulation.Tick();
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == 11);
        Assert.Equal(expected, simulation.Rng.SaveState());
    }

    [Fact]
    public void MovingRatSaveRestoreContinuesWithIdenticalStateAndRandomness()
    {
        var (db, world) = Prison();
        ObjectState rat = world.SpawnObject(db, RatRules.RatObjectId, PlayerIds.FirstKeeper, world.Width + 1);
        rat.Heading = 512;
        rat.Health = 117;
        var original = new GameSimulation(db, world, 42, [new ObjectMovementSystem()]);
        original.Tick();
        Assert.Equal(ObjectMovementRules.MovingProcess.Value, rat.ProcessState);
        Assert.Equal(RatRules.WanderingProcess.Value, rat.MoveResumeState);
        Assert.NotEqual(0, rat.Heading);
        Assert.True(rat.MoveTicksRemaining > 0);
        int savedX = rat.PosX, savedY = rat.PosY, savedHealth = rat.Health;
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new ObjectMovementSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        ObjectState copy = Assert.Single(restored.World.Objects);
        for (int i = 0; i < 24; i++)
        {
            original.Tick();
            restored.Tick();
            Assert.Equal(original.StateHash(), restored.StateHash());
            Assert.Equal(original.Rng.SaveState(), restored.Rng.SaveState());
            Assert.Equal((rat.PosX, rat.PosY, rat.Heading, rat.Health), (copy.PosX, copy.PosY, copy.Heading, copy.Health));
            Assert.Equal((rat.ProcessState, rat.MoveResumeState, rat.MoveTicksRemaining),
                (copy.ProcessState, copy.MoveResumeState, copy.MoveTicksRemaining));
        }
        Assert.True(rat.PosX != savedX || rat.PosY != savedY);
        Assert.Equal(savedHealth - 24, rat.Health);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedAnimalPassUsesNewestFirstSharedRandomDraws(bool ratNewest)
    {
        var (db, world) = Prison();
        ObjectState chicken;
        ObjectState rat;
        if (ratNewest)
        {
            chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, PlayerIds.FirstKeeper, world.Width + 1);
            rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, world.Width + 1);
        }
        else
        {
            rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, world.Width + 1);
            chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, PlayerIds.FirstKeeper, world.Width + 1);
        }
        rat.ProcessState = 11;
        chicken.ProcessState = 8;
        var draws = new Draws(ratNewest
            ? [(4, 0), (4096, 2048), (4096, 2048), (16, 1)]
            : [(16, 1), (4, 0), (4096, 2048), (4096, 2048)]);
        var simulation = new GameSimulation(db, world, new SimRandom(draws),
            SimulationSystems.Default().Where(s => s.Name is "Rat" or "ChickenMovement" or "ObjectMovement"));
        simulation.Tick();
        Assert.Equal(0, draws.Remaining);
        Assert.Equal(CreatureState.CentreOf(2), rat.MoveDestinationX);
        Assert.Equal(CreatureState.CentreOf(1), rat.MoveDestinationY);
        Assert.Equal(8, chicken.ChickenReactionTicks);
    }

    private sealed class Draws(params (int Bound, int Value)[] draws) : IRandomAlgorithm
    {
        private int _index;
        public int Remaining => draws.Length - _index;
        public int NextInt(int exclusiveMax)
        {
            var draw = draws[_index++];
            Assert.Equal(draw.Bound, exclusiveMax);
            return draw.Value;
        }
        public void HashInto(ref Hash64 hash) => hash.Add(_index);
    }

    [Fact]
    public void RatMovesThroughOwnedLandOutsideItsPrison()
    {
        var (db, world) = Prison();
        for (int y = 3; y <= 6; y++)
            for (int x = 3; x <= 6; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
            }
        ObjectState rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, 4 * world.Width + 4);
        rat.PosX = rat.PosY = CreatureState.CentreOf(4);
        rat.ProcessState = 11;
        var simulation = new GameSimulation(db, world, 42, [new RatSystem()]);
        simulation.Tick();
        Assert.Equal((byte)9, rat.ProcessState);
        Assert.Equal((byte)11, rat.MoveResumeState);
        Assert.InRange(rat.MoveDestinationX, 3 << 12, (6 << 12) - 1);
        for (int i = 0; i < 50; i++) simulation.Tick();
        Assert.NotEqual(CreatureState.CentreOf(4), rat.PosX);
    }

    [Fact]
    public void RatCannotChooseADoubleQuadInteriorTile()
    {
        var (db, world) = Prison();
        ObjectState rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, world.Width + 2);
        rat.PosX = CreatureState.CentreOf(2); rat.PosY = CreatureState.CentreOf(1); rat.ProcessState = 11;
        var simulation = new GameSimulation(db, world, 42, [new RatSystem()]);
        simulation.Tick();
        Assert.Equal((byte)9, rat.ProcessState);
        Assert.False((rat.MoveDestinationX >> 12) == 2 && (rat.MoveDestinationY >> 12) == 2);
    }

    [Fact]
    public void RatDisappearsWhenItsCurrentTileHasAnotherOwner()
    {
        var (db, world) = Prison();
        ObjectState rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, world.Width + 1);
        rat.PosX = rat.PosY = CreatureState.CentreOf(1); rat.ProcessState = 11;
        world.Tile(1, 1).OwnerPlayerId = PlayerIds.Neutral;
        var simulation = new GameSimulation(db, world, 42, [new RatSystem()]);
        simulation.Tick();
        Assert.DoesNotContain(rat, world.Objects);
    }

    [Fact]
    public void RatLosesOneHealthEachTickAndExpiresAfterThreeHundredTicks()
    {
        var (db, world) = Prison();
        ObjectState rat = world.SpawnObject(db, 11, PlayerIds.FirstKeeper, world.Width + 1);
        rat.PosX = rat.PosY = CreatureState.CentreOf(1); rat.ProcessState = 11;
        var simulation = new GameSimulation(db, world, 42, [new RatSystem()]);
        simulation.Tick();
        Assert.Equal(299, rat.Health);
        for (int i = 1; i < 300; i++) simulation.Tick();
        Assert.DoesNotContain(rat, world.Objects);
    }
}
