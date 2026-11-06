// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The loose Chicken's process 10, <c>0x004A26D0</c> (ADR-0647).</summary>
public class LooseChickenTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    // The four sides in DK2's order (0x006BC350): north, east, south, west.
    private static readonly TilePos[] Sides = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    /// <summary>A bird loose at (2, 2), the middle of the fixture's 3x3 of the keeper's claimed path.</summary>
    private static (GameSimulation Sim, ObjectState Chicken, ListEventSink Sink) Start(uint seed, Action<GameDatabase, WorldState>? shape = null)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        shape?.Invoke(db, world);
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 2 * world.Width + 2);
        chicken.ChickenCanEscape = true;
        ChickenMovementRules.SetProcess(chicken, ChickenProcess.Loose);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new ChickenMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, chicken, sink);
    }

    private static uint SeedOf(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static void Rock(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.Rock;
        tile.OwnerPlayerId = PlayerIds.Unowned;
    }

    [Fact]
    public void ItWalksToAPointInTheFirstOpenSideFromTheDrawnOne()
    {
        var (sim, chicken, _) = Start(1);
        var expected = new Dk2Lcg(SeedOf(sim));
        sim.Tick();

        TilePos side = Sides[expected.NextInt(4)];
        int x = ((2 + side.X) << 12) | (expected.NextInt(0x555) + 0x555);
        int y = ((2 + side.Y) << 12) | (expected.NextInt(0x555) + 0x555);
        Assert.Equal(expected.Seed, SeedOf(sim));
        Assert.Equal((byte)ChickenProcess.Moving, chicken.ProcessState);
        Assert.Equal((byte)ChickenProcess.Loose, chicken.MoveResumeState);
        Assert.Equal((x, y), (chicken.MoveDestinationX, chicken.MoveDestinationY));
    }

    [Fact]
    public void WithNoSideOpenItDrawsOnlyTheStartAndItsProcessIsCleared()
    {
        var (sim, chicken, _) = Start(1, (db, world) =>
        {
            foreach (TilePos side in Sides) Rock(db, world, new TilePos(2 + side.X, 2 + side.Y));
        });
        var expected = new Dk2Lcg(SeedOf(sim));
        sim.Tick();
        expected.NextInt(4);
        Assert.Equal(expected.Seed, SeedOf(sim));
        Assert.Equal((byte)ChickenProcess.None, chicken.ProcessState);
    }

    /// <summary>A room is refused on the first three tries and taken on the fourth.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARoomSideIsTakenOnlyOnTheFourthTry(bool fourth)
    {
        // The drawn side is the room when fourth is false, else the side tried last; the rest is rock.
        uint seed = 1;
        int start = new Dk2Lcg(seed).NextInt(4);
        int roomSide = fourth ? (start + 3) & 3 : start;
        var (sim, chicken, _) = Start(seed, (db, world) =>
        {
            for (int i = 0; i < 4; i++)
            {
                var at = new TilePos(2 + Sides[i].X, 2 + Sides[i].Y);
                if (i == roomSide) RoomRules.Place(db, ref world.Tile(at), db.Room(RoomTypeId.Lair), Keeper);
                else Rock(db, world, at);
            }
        });
        sim.Tick();
        Assert.Equal(fourth ? (byte)ChickenProcess.Moving : (byte)ChickenProcess.None, chicken.ProcessState);
    }

    /// <summary>A Treasury tile holding gold holds DK2's Gold Pile 1, an obstacle (ADR-0649): on the
    /// fourth try, where a room may be taken, the empty one is and the stocked one is not.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void AStoreTileHoldingGoldIsRefused(int gold)
    {
        uint seed = 1;
        int last = (new Dk2Lcg(seed).NextInt(4) + 3) & 3;
        var (sim, chicken, _) = Start(seed, (db, world) =>
        {
            for (int i = 0; i < 4; i++)
            {
                var at = new TilePos(2 + Sides[i].X, 2 + Sides[i].Y);
                if (i != last) { Rock(db, world, at); continue; }
                RoomRules.Place(db, ref world.Tile(at), db.Room(RoomTypeId.Treasury), Keeper);
                world.Tile(at).GoldHeld = gold;
            }
        });
        sim.Tick();
        Assert.Equal(gold == 0 ? (byte)ChickenProcess.Moving : (byte)ChickenProcess.None, chicken.ProcessState);
    }

    /// <summary>A bird no room holds loses one health a turn (DieOverTimeIfNotInRoom, ADR-0650); at none it
    /// dies and goes the same turn.</summary>
    [Fact]
    public void AFreeRangeBirdLosesOneHealthATurnAndDiesAtNone()
    {
        var (sim, chicken, sink) = Start(1);
        Assert.Equal(1000, chicken.Health);
        sim.Tick();
        sim.Tick();
        Assert.Equal(998, chicken.Health);
        chicken.Health = 1;
        sim.Tick();
        Assert.DoesNotContain(chicken, sim.World.Objects);
        Assert.Contains(sink.Events, e => e.Kind == "chicken_died" && e.Detail == $"object={chicken.Id}");
    }

    [Fact]
    public void AnotherOwnersTileIsNotASide()
    {
        var (sim, chicken, _) = Start(1, (db, world) =>
        {
            foreach (TilePos side in Sides) world.Tile(new TilePos(2 + side.X, 2 + side.Y)).OwnerPlayerId = PlayerIds.Neutral;
        });
        sim.Tick();
        Assert.Equal((byte)ChickenProcess.None, chicken.ProcessState);
    }

    public enum Blocker { None, Door, Trap, Obstacle, Water }

    [Theory]
    [InlineData(4, 3, true, false, 100000, true)]
    [InlineData(3, 5, true, false, 100000, true)]
    [InlineData(5, 5, true, false, 100000, true)]
    [InlineData(5, 5, false, false, 100000, false)]
    [InlineData(5, 5, true, true, 100000, false)]
    [InlineData(5, 5, true, false, 0, false)]
    [InlineData(4, 5, true, false, 100000, false)]
    public void AHeartPillarSideIsRejectedOnlyWhileThePillarExistsAndIsAnObstacle(
        int sideX, int sideY, bool obstacle, bool ruined, int health, bool blocked)
    {
        GameDatabase original = Dk2TerrainFixture.Database();
        ObjectDef pillar = original.ObjectById(Navigation.WorldSteering.ArchesObjectId)! with
        {
            ObjectId = HeartPillarRules.ArchPillarObjectId,
            Flags = obstacle ? ObjectFlags.Obstacle : ObjectFlags.None,
        };
        var db = new GameDatabase(original.Terrain.Values, original.Rooms.Values, original.Creatures.Values,
            original.Variables, objects: original.Objects.Values.Append(pillar));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        for (int y = 2; y <= 6; y++)
            for (int x = 2; x <= 6; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Keeper);
        ObjectState bird = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper,
            sideY * world.Width + sideX - 1);
        bird.ChickenCanEscape = true;
        ChickenMovementRules.SetProcess(bird, ChickenProcess.Loose);
        const uint seed = 1860230197;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new ChickenMovementSystem()]);
        world.Player(Keeper).HeartRuined = ruined;
        world.Player(Keeper).HeartHealth = health;
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == HeartPillarRules.ArchPillarObjectId);
        var expected = new Dk2Lcg(seed);
        Assert.Equal(2, expected.NextInt(4)); // South, west, north, then the east side.
        if (!blocked) { expected.NextInt(0x555); expected.NextInt(0x555); }

        sim.Tick();

        Assert.Equal(expected.Seed, SeedOf(sim));
        Assert.Equal(blocked ? (byte)ChickenProcess.None : (byte)ChickenProcess.Moving, bird.ProcessState);
        if (!blocked) Assert.Equal(sideX, bird.MoveDestinationX >> 12);
    }

    /// <summary>The one side left open holds a door, a trap or an obstacle, or is water (another nav
    /// class): no side takes the bird. With nothing there, it walks.</summary>
    [Theory]
    [InlineData(Blocker.None)]
    [InlineData(Blocker.Door)]
    [InlineData(Blocker.Trap)]
    [InlineData(Blocker.Obstacle)]
    [InlineData(Blocker.Water)]
    public void ADoorTrapObstacleOrOtherNavClassSideIsRefused(Blocker blocker)
    {
        var open = new TilePos(2, 1);
        var (sim, chicken, _) = Start(1, (db, world) =>
        {
            for (int i = 1; i < 4; i++) Rock(db, world, new TilePos(2 + Sides[i].X, 2 + Sides[i].Y));
            switch (blocker)
            {
                case Blocker.Door:
                    ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = open.X, Y = open.Y, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] }]);
                    break;
                case Blocker.Trap:
                    ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = open.X, Y = open.Y, TrapId = Dk2TerrainFixture.SpikeTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
                    break;
                case Blocker.Obstacle:
                    world.SpawnObject(db, Navigation.WorldSteering.DungeonHeartObstacleObjectId, Keeper, open.Y * world.Width + open.X);
                    break;
                case Blocker.Water:
                    world.Tile(open).TerrainId = Dk2TerrainFixture.Water;
                    break;
            }
        });
        sim.Tick();
        Assert.Equal(blocker == Blocker.None ? (byte)ChickenProcess.Moving : (byte)ChickenProcess.None, chicken.ProcessState);
    }

    [Fact]
    public void ABirdAHatcheryHoldsRoamsWhileOnAHatchery()
    {
        var (sim, chicken, _) = Start(1, (db, world) =>
            RoomRules.Place(db, ref world.Tile(2, 2), db.Room(RoomTypeId.Hatchery), Keeper));
        chicken.ChickenCanEscape = false;
        sim.Tick();
        Assert.Contains(chicken, sim.World.Objects);
        Assert.Equal((byte)ChickenProcess.Moving, chicken.ProcessState);
    }

    [Fact]
    public void AScaredBirdDrawsNothing()
    {
        var (sim, chicken, _) = Start(1);
        chicken.ChickenScared = true;
        uint before = SeedOf(sim);
        sim.Tick();
        Assert.Equal(before, SeedOf(sim));
        Assert.Equal((byte)ChickenProcess.None, chicken.ProcessState);
    }

    [Fact]
    public void ABirdAHatcheryHoldsDiesOffAnyHatcheryWithoutADraw()
    {
        var (sim, chicken, sink) = Start(1);
        chicken.ChickenCanEscape = false;
        uint before = SeedOf(sim);
        sim.Tick();
        Assert.Equal(before, SeedOf(sim));
        Assert.DoesNotContain(chicken, sim.World.Objects);
        Assert.Contains(sink.Events, e => e.Kind == "loose_chicken_died" && e.Detail.EndsWith("reason=held_off_hatchery"));
    }

    [Fact]
    public void ABirdOfAFallenKeeperDies()
    {
        var (sim, chicken, _) = Start(1);
        sim.World.Player(Keeper).KilledByPlayerId = PlayerIds.Good;
        uint before = SeedOf(sim);
        sim.Tick();
        Assert.Equal(before, SeedOf(sim));
        Assert.DoesNotContain(chicken, sim.World.Objects);
    }

    [Fact]
    public void ABirdOnATerrainWithDamageDies()
    {
        var (sim, chicken, _) = Start(1, (db, world) => world.Tile(2, 2).TerrainId = Dk2TerrainFixture.Lava);
        sim.Tick();
        Assert.DoesNotContain(chicken, sim.World.Objects);
    }
}
