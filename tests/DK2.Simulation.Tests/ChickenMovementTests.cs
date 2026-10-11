// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ChickenMovementTests
{
    private static (GameDatabase Db, WorldState World, ObjectState Chicken) HatchBird()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), PlayerIds.FirstKeeper);
        ObjectState egg = world.SpawnObject(db, HatcheryRules.EggObjectId, PlayerIds.FirstKeeper, 2 * world.Width + 2);
        egg.PosX = egg.PosY = CreatureState.CentreOf(2);
        return (db, world, HatcheryRules.Hatch(world, db, egg));
    }

    [Fact]
    public void OwnedHatcheryRegistrationDisablesEscapeAndRetainsCapacityOutsideTheRoom()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.False(chicken.ChickenCanEscape);
        chicken.TileIndex = 6 * world.Width + 6;
        chicken.PosX = chicken.PosY = CreatureState.CentreOf(6);
        RoomInstance room = RoomIndex.RoomAt(world, db, 2 * world.Width + 2)!;
        Assert.Equal(1, HatcheryRules.Flock(world, room));
        Assert.Equal(1, HatcheryRules.Birds(world, room));
    }

    [Fact]
    public void SuccessfulHandLiftReleasesRegisteredHatcheryCapacity()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, 42, [new HandSystem(), new ChickenMovementSystem()]);
        sim.Tick();
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(PlayerIds.FirstKeeper, chicken.Id)));
        Assert.True(chicken.InHand);
        Assert.True(chicken.ChickenCanEscape);
        Assert.Equal(0, HatcheryRules.Birds(world, RoomIndex.RoomAt(world, db, 2 * world.Width + 2)!));
    }

    [Fact]
    public void ASpawnedBirdStartsAtItsRequestedTileCentre()
    {
        var (db, world, _) = HatchBird();
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId,
            PlayerIds.FirstKeeper, 6 * world.Width + 5);
        Assert.Equal(CreatureState.CentreOf(5), chicken.PosX);
        Assert.Equal(CreatureState.CentreOf(6), chicken.PosY);
    }

    [Theory]
    [InlineData(HatcheryRules.ChickenObjectId)]
    [InlineData(HatcheryRules.EggObjectId)]
    public void AMapPlacedBirdStartsAtItsPlacedTileCentre(byte type)
    {
        var (db, world, _) = HatchBird();
        ThingProjection.Place(world, db,
        [
            new ObjectThing(new byte[24])
            {
                X = 5, Y = 6, ObjectId = type, PlayerId = PlayerIds.FirstKeeper,
                Unknown_0008 = new byte[4],
            },
        ]);
        ObjectState bird = world.Objects.Last();
        Assert.Equal(CreatureState.CentreOf(5), bird.PosX);
        Assert.Equal(CreatureState.CentreOf(6), bird.PosY);
    }

    [Fact]
    public void ACapacityRefusedChickenStillRoamsAndRetainsEscapeEligibility()
    {
        var (db, world, chicken) = HatchBird();
        for (int i = 0; i < 9; i++)
        {
            ObjectState newer = world.SpawnObject(db, HatcheryRules.ChickenObjectId,
                PlayerIds.FirstKeeper, chicken.TileIndex);
            newer.ProcessState = (byte)ChickenProcess.Hatchery;
        }
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.Equal(9, world.Objects.Count(o => o.HatcheryRoomAnchorTileIndex >= 0));
        Assert.Equal(-1, chicken.HatcheryRoomAnchorTileIndex);
        Assert.True(chicken.ChickenCanEscape);
        Assert.Equal((byte)ChickenProcess.Moving, chicken.ProcessState);
    }

    [Fact]
    public void NorthWestRoomGrowthPreservesRegisteredCapacityAndEscapeEligibility()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        int anchor = chicken.HatcheryRoomAnchorTileIndex;
        RoomRules.Place(db, ref world.Tile(1, 0), db.Room(RoomTypeId.Hatchery), PlayerIds.FirstKeeper);
        sim.Tick();
        Assert.Equal(anchor, chicken.HatcheryRoomAnchorTileIndex);
        Assert.False(chicken.ChickenCanEscape);
        Assert.Equal(1, HatcheryRules.Birds(world, RoomIndex.RoomAt(world, db, 1)!));
    }

    [Fact]
    public void LosingTheRegisteredRoomAnchorReleasesTheChicken()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        world.TileAtIndex(chicken.HatcheryRoomAnchorTileIndex).TerrainId = Dk2TerrainFixture.ClaimedPath;
        sim.Tick();
        Assert.Equal(-1, chicken.HatcheryRoomAnchorTileIndex);
        Assert.True(chicken.ChickenCanEscape);
    }

    [Fact]
    public void AHatchedChickenWalksAcrossItsOwnedRoomInsteadOfRemainingAtItsEgg()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, seed: 42, SimulationSystems.Default());
        var positions = new HashSet<(int, int)>();
        for (int i = 0; i < 120; i++)
        {
            sim.Tick();
            positions.Add((chicken.PosX, chicken.PosY));
            Assert.Equal((chicken.PosY >> 12) * world.Width + (chicken.PosX >> 12), chicken.TileIndex);
            Assert.Equal(chicken.TileIndex, chicken.TileLink.TileIndex);
            Assert.Equal(RoomTypeId.Hatchery, db.RoomByTerrain(world.TileAtIndex(chicken.TileIndex).TerrainId)!.TypeId);
        }
        Assert.True(positions.Count > 20, $"Only {positions.Count} positions were visited.");
    }

    [Fact]
    public void TurningConsumesTicksWithoutAdvancingTheMovementCountdown()
    {
        var (db, world, chicken) = HatchBird();
        chicken.ProcessState = (byte)ChickenProcess.Moving;
        chicken.MoveFlags = ObjectMoveFlags.Turning;
        chicken.MoveTicksRemaining = 3;
        chicken.MoveDestinationX = chicken.PosX + 1024;
        chicken.MoveDestinationY = chicken.PosY;
        chicken.MoveVelocityX = 256;
        chicken.MoveResumeState = (byte)ChickenProcess.Reaction;
        int initialX = chicken.PosX;
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.Equal(256, chicken.Heading);
        Assert.Equal(3, chicken.MoveTicksRemaining);
        Assert.Equal(initialX, chicken.PosX);
        sim.Tick();
        Assert.Equal(512, chicken.Heading);
        Assert.Equal(2, chicken.MoveTicksRemaining);
        Assert.Equal(initialX + 256, chicken.PosX);
        sim.Run(2);
        Assert.Equal(initialX + 1024, chicken.PosX);
        Assert.Equal((byte)ChickenProcess.Reaction, chicken.ProcessState);
        Assert.Equal(0, chicken.ProcessAgeTicks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHeldOrCarriedChickenDoesNotRunItsProcess(bool carried)
    {
        var (db, world, chicken) = HatchBird();
        if (carried) chicken.CarriedByCreatureId = 17;
        else chicken.InHand = true;
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        int x = chicken.PosX, y = chicken.PosY;
        sim.Run(30);
        Assert.Equal(x, chicken.PosX);
        Assert.Equal(y, chicken.PosY);
        Assert.Equal(0, chicken.ProcessAgeTicks);
        Assert.Equal((byte)ChickenProcess.Hatchery, chicken.ProcessState);
        Assert.False(chicken.TileLink.IsLinked);
    }

    [Fact]
    public void TheRoomSearchRejectsCoopObstacleTilesButNotOtherBirds()
    {
        var (db, world, chicken) = HatchBird();
        foreach (TilePos tile in new[] { new TilePos(2, 1), new TilePos(3, 2), new TilePos(2, 3) })
            world.SpawnObject(db, HatcheryRules.CoopObjectId, PlayerIds.FirstKeeper, tile.Y * world.Width + tile.X);
        world.SpawnObject(db, HatcheryRules.ChickenObjectId, PlayerIds.FirstKeeper, 2 * world.Width + 1);
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.Equal(1, chicken.MoveDestinationX >> 12);
        Assert.Equal(2, chicken.MoveDestinationY >> 12);
        Assert.InRange(chicken.MoveDestinationX & 0xFFF, 0x500, 0xEFF);
        Assert.InRange(chicken.MoveDestinationY & 0xFFF, 0x500, 0xAFF);
    }

    [Fact]
    public void ReactionWaitsForItsCountdownBeforeSelectingAnotherRoomPoint()
    {
        var (db, world, chicken) = HatchBird();
        chicken.ProcessState = (byte)ChickenProcess.Reaction;
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        int countdown = new SimRandom(42).NextInt(16) + 8;
        sim.Tick();
        Assert.Equal(countdown - 1, chicken.ChickenReactionTicks);
        sim.Run(countdown - 1);
        Assert.Equal(0, chicken.ChickenReactionTicks);
        Assert.Equal((byte)ChickenProcess.Reaction, chicken.ProcessState);
        sim.Tick();
        Assert.Equal((byte)ChickenProcess.Hatchery, chicken.ProcessState);
        Assert.Equal(0, chicken.ProcessAgeTicks);
    }

    [Fact]
    public void AMovingChickenRoundTripsAndContinuesWithTheSameRandomStream()
    {
        var (db, world, chicken) = HatchBird();
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Run(5);
        Assert.Equal((byte)ChickenProcess.Moving, chicken.ProcessState);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new ChickenMovementSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int i = 0; i < 300; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }

    [Fact]
    public void EveryChickenMovementFieldContributesToItsStateHash()
    {
        var (_, _, chicken) = HatchBird();
        var changes = new Action<ObjectState>[]
        {
            c => c.Heading++, c => c.ProcessAgeTicks++, c => c.ChickenCanEscape = false,
            c => c.HatcheryRoomAnchorTileIndex++,
            c => c.ChickenScared = true, c => c.ChickenReactionTicks++, c => c.MoveResumeState++,
            c => c.MoveDestinationX++, c => c.MoveDestinationY++, c => c.MoveVelocityX++,
            c => c.MoveVelocityY++, c => c.MoveTicksRemaining++, c => c.MoveFlags = ObjectMoveFlags.Turning,
        };
        foreach (Action<ObjectState> change in changes)
        {
            Hash64 before = Hash64.Start();
            chicken.HashInto(ref before);
            change(chicken);
            Hash64 after = Hash64.Start();
            chicken.HashInto(ref after);
            Assert.NotEqual(before.Value, after.Value);
        }
    }

    [Theory]
    [InlineData(4096, 0, 15)]
    [InlineData(4097, 0, 16)]
    [InlineData(4096, 4096, 21)]
    public void MovementCountUsesTheSquaredDistanceQuotientAndRemainder(int dx, int dy, int expectedTicks)
    {
        var (db, world, chicken) = HatchBird();
        var starter = new ProcessProbe(context =>
            Assert.True(ChickenMovementRules.StartMove(context, chicken,
                new ObjectMovePoint(new Fp32(chicken.PosX + dx), new Fp32(chicken.PosY + dy)),
                ChickenProcess.Reaction, default, interruptible: true)));
        var sim = new GameSimulation(db, world, 42, [starter]);
        sim.Tick();
        Assert.Equal(expectedTicks, chicken.MoveTicksRemaining);
        Assert.Equal(ObjectMoveFlags.Turning | ObjectMoveFlags.Interruptible, chicken.MoveFlags);
        Assert.Equal(256, ChickenMovementRules.RuntimeSpeed(db.ObjectById(HatcheryRules.ChickenObjectId)!));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void AReactionEscapesAGroundedCreatureButNotAWorkerOrAScaredChicken(bool worker, bool scared, bool escapes)
    {
        var (db, world, chicken) = HatchBird();
        CreatureDef def = db.Creatures.Values.First(c => c.Flags.HasFlag(CreatureFlags.IsWorker) == worker
            && !c.Flags.HasFlag(CreatureFlags.CanFly));
        CreatureState creature = world.SpawnCreature(db, def.CreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        creature.PosX -= 256;
        chicken.ProcessState = (byte)ChickenProcess.Reaction;
        chicken.ProcessAgeTicks = 1;
        chicken.ChickenReactionTicks = 7;
        chicken.ChickenScared = scared;
        chicken.MoveFlags = ObjectMoveFlags.Interruptible;
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        sim.Tick();
        Assert.Equal(escapes ? (byte)ChickenProcess.Moving : (byte)ChickenProcess.Reaction, chicken.ProcessState);
        Assert.Equal(escapes ? 7 : 6, chicken.ChickenReactionTicks);
        if (escapes)
        {
            Assert.Equal(chicken.PosX + 2048, chicken.MoveDestinationX);
            Assert.Equal(512, chicken.MoveVelocityX);
            Assert.Equal((byte)ChickenProcess.Reaction, chicken.MoveResumeState);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void ASubQuantumMovementSpeedIsRejectedInsteadOfDividingSilently(int fileSpeed)
    {
        var (originalDb, world, chicken) = HatchBird();
        var db = new GameDatabase(originalDb.Terrain.Values, originalDb.Rooms.Values,
            originalDb.Creatures.Values, originalDb.Variables,
            objects: originalDb.Objects.Values.Select(o => o.ObjectId == HatcheryRules.ChickenObjectId
                ? o with { Speed = new Fp32(fileSpeed) } : o));
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        var error = Assert.Throws<DivideByZeroException>(() => sim.Tick());
        Assert.Contains("fixed-point square", error.Message);
    }

    [Fact]
    public void AnOutOfMapChickenPositionReportsTheMovementInvariant()
    {
        var (db, world, chicken) = HatchBird();
        chicken.PosX = -1;
        var sim = new GameSimulation(db, world, 42, [new ChickenMovementSystem()]);
        var error = Assert.Throws<InvalidOperationException>(() => sim.Tick());
        Assert.Contains("outside the map", error.Message);
    }

    [Fact]
    public void TheNewestChickenConsumesTheFirstRoomDestinationDraws()
    {
        var (db, referenceWorld, reference) = HatchBird();
        new GameSimulation(db, referenceWorld, 42, [new ChickenMovementSystem()]).Tick();
        var (_, world, older) = HatchBird();
        ObjectState egg = world.SpawnObject(db, HatcheryRules.EggObjectId, PlayerIds.FirstKeeper, older.TileIndex);
        egg.PosX = older.PosX;
        egg.PosY = older.PosY;
        ObjectState newer = HatcheryRules.Hatch(world, db, egg);
        new GameSimulation(db, world, 42, [new ChickenMovementSystem()]).Tick();
        Assert.Equal(reference.MoveDestinationX, newer.MoveDestinationX);
        Assert.Equal(reference.MoveDestinationY, newer.MoveDestinationY);
    }

    [Theory]
    [InlineData(RoomTypeId.Hatchery, 6)]
    [InlineData(RoomTypeId.Prison, 3)]
    [InlineData(RoomTypeId.Treasury, 5)]
    public void DroppingAMovingChickenSelectsItsLandingRoomInsteadOfResumingTheOldDestination(RoomTypeId room, int expectedProcess)
    {
        var (db, world, chicken) = HatchBird();
        var landing = new TilePos(6, 6);
        RoomRules.Place(db, ref world.Tile(landing), db.Room(room), PlayerIds.FirstKeeper);
        chicken.ProcessState = (byte)ChickenProcess.Moving;
        chicken.ProcessAgeTicks = 30;
        chicken.MoveDestinationX = chicken.PosX + 128;
        chicken.MoveDestinationY = chicken.PosY;
        chicken.MoveTicksRemaining = 1;
        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(PlayerIds.FirstKeeper, chicken.Id)));
        Assert.True(chicken.InHand);
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, landing)));
        Assert.False(chicken.InHand);
        Assert.Equal(expectedProcess, chicken.ProcessState);
        Assert.Equal(0, chicken.ProcessAgeTicks);
        Assert.Equal(CreatureState.CentreOf(landing.X), chicken.PosX);
        Assert.Equal(CreatureState.CentreOf(landing.Y), chicken.PosY);
    }

    [Fact]
    public void AChickenDroppedOnFloorNeverSnapsBackToItsPrePickupDestination()
    {
        var (db, world, chicken) = HatchBird();
        var landing = new TilePos(6, 6);
        world.Tile(landing).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(landing).OwnerPlayerId = PlayerIds.FirstKeeper;
        chicken.ProcessState = (byte)ChickenProcess.Moving;
        chicken.MoveDestinationX = chicken.PosX + 128;
        chicken.MoveDestinationY = chicken.PosY;
        chicken.MoveTicksRemaining = 1;
        var sim = new GameSimulation(db, world, 42, [new HandSystem(), new ChickenMovementSystem()]);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(PlayerIds.FirstKeeper, chicken.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, landing)));
        sim.Run(20);
        Assert.Equal((byte)ChickenProcess.Loose, chicken.ProcessState);
        Assert.Equal(CreatureState.CentreOf(landing.X), chicken.PosX);
        Assert.Equal(CreatureState.CentreOf(landing.Y), chicken.PosY);
        Assert.Equal(landing.Y * world.Width + landing.X, chicken.TileLink.TileIndex);
    }

    [Fact]
    public void ARefusedDropKeepsTheMovingChickenSuspendedInTheHand()
    {
        var (db, world, chicken) = HatchBird();
        chicken.ProcessState = (byte)ChickenProcess.Moving;
        chicken.ProcessAgeTicks = 17;
        chicken.MoveTicksRemaining = 1;
        var sim = new GameSimulation(db, world, 42, [new HandSystem(), new ChickenMovementSystem()]);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(PlayerIds.FirstKeeper, chicken.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, new TilePos(6, 6))));
        Assert.True(chicken.InHand);
        Assert.Equal((byte)ChickenProcess.Moving, chicken.ProcessState);
        Assert.Equal(17, chicken.ProcessAgeTicks);
        Assert.Equal(1, chicken.MoveTicksRemaining);
        Assert.False(chicken.TileLink.IsLinked);
    }

    private sealed class ProcessProbe(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "ProcessProbe";
        public void Execute(SimulationContext context) => action(context);
    }
}
