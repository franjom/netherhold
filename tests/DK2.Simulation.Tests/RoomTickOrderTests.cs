// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's room tick walks one list of every room, newest first, all types together
/// (<c>0x004EC450</c>, <c>0x004EC490</c>): a room's handler, border step and health step come before the next
/// room's, so an older Treasury's draws follow a newer Workshop's (workshop-timers.md section 4, ADR-0634).</summary>
public sealed class RoomTickOrderTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Ticks = SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary>The generator as dk2-random.md section 1 states it.</summary>
    private sealed class Oracle(uint seed)
    {
        public uint Seed { get; private set; } = seed;

        public int Next(int n)
        {
            Seed = unchecked((Seed * 0x0019660Du) + 0x3C6EF35Fu);
            return (int)(((Seed >> 13) | (Seed << 19)) % (uint)n);
        }
    }

    private static WorldState World(GameDatabase db) =>
        WorldState.FromMap(db, Dk2TerrainFixture.Map(30, 30), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static void Build(GameDatabase db, WorldState world, RoomTypeId type, TilePos at, int size = 1)
    {
        for (int y = at.Y; y < at.Y + size; y++)
        for (int x = at.X; x < at.X + size; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
            RoomRules.Place(db, ref tile, db.Room(type), Keeper);
            SlabChain.Changed(world, db, world.TileIndex(new TilePos(x, y)), terrainBefore, ownerBefore);
        }
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, params IRoomTickStep[] steps) =>
        new(db, world, new SimRandom(new Dk2Lcg(1)), [new RoomTickSystem(steps)]);

    private static uint SeedOf(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static void ZeroTimers(WorldState world, TilePos at)
    {
        ref TileState t = ref world.Tile(at);
        (t.WorkshopSteamTimer, t.WorkshopLavaSpillTimer, t.WorkshopSmokeTimer, t.WorkshopLavaBubblingTimer) = (0, 0, 0, 0);
    }

    private static int Reload(Oracle oracle) => oracle.Next(16 * Ticks) + (8 * Ticks);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATreasurysSparkleDrawComesBeforeOrAfterAWorkshopsFourReloadsAsTheRoomIsNewerOrOlder(bool treasuryNewer)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        var workshopAt = new TilePos(10, 10);
        var treasuryAt = new TilePos(14, 10);
        GameSimulation sim = Sim(db, world, new StoreSweepSystem(), new WorkshopTimerSystem());
        if (treasuryNewer)
        {
            Build(db, world, RoomTypeId.Workshop, workshopAt);
            Build(db, world, RoomTypeId.Treasury, treasuryAt);
        }
        else
        {
            Build(db, world, RoomTypeId.Treasury, treasuryAt);
            Build(db, world, RoomTypeId.Workshop, workshopAt);
        }

        ZeroTimers(world, workshopAt);
        var oracle = new Oracle(SeedOf(sim));
        if (treasuryNewer) oracle.Next(100);   // 0x004F3A20, the sparkle's room roll, before the Workshop's
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        if (!treasuryNewer) oracle.Next(100);

        sim.Tick();

        Assert.Equal(steam, world.Tile(workshopAt).WorkshopSteamTimer);
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void AMixedListOfWorkshopsATreasuryAndALairTicksNewestFirstWholeRoomByRoom()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        TilePos oldWorkshop = new(8, 8), treasury = new(12, 8), newWorkshop = new(16, 8), lair = new(20, 8);
        GameSimulation sim = Sim(db, world, new StoreSweepSystem(), new HatedCompanySystem(), new WorkshopTimerSystem(),
            new RoomBorderFlameSystem(), new RoomHealthSystem());
        Build(db, world, RoomTypeId.Workshop, oldWorkshop);
        Build(db, world, RoomTypeId.Treasury, treasury);
        Build(db, world, RoomTypeId.Workshop, newWorkshop);
        Build(db, world, RoomTypeId.Lair, lair);
        ZeroTimers(world, oldWorkshop);
        ZeroTimers(world, newWorkshop);
        var oracle = new Oracle(SeedOf(sim));
        int newSteam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        oracle.Next(100);   // the Treasury, between the two Workshops
        int oldSteam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);

        sim.Tick();

        Assert.Equal((newSteam, oldSteam),
            (world.Tile(newWorkshop).WorkshopSteamTimer, world.Tile(oldWorkshop).WorkshopSteamTimer));
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void ANewerWorkshopsReloadsComeBeforeAnOlderHatcherysLayAndThatDrawComesAfter()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        TilePos hatchery = new(8, 8), workshop = new(16, 8);
        GameSimulation sim = Sim(db, world, new HatcheryLayStep(), new WorkshopTimerSystem());
        Build(db, world, RoomTypeId.Hatchery, hatchery, size: 3);
        Build(db, world, RoomTypeId.Workshop, workshop);
        world.Tile(hatchery).HatcheryLastGeneratedTick = -10_000;   // the lay is due
        ZeroTimers(world, workshop);
        var oracle = new Oracle(SeedOf(sim));
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);

        sim.Tick();

        Assert.Equal(steam, world.Tile(workshop).WorkshopSteamTimer);
        Assert.Contains(world.Objects, o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        Assert.NotEqual(oracle.Seed, SeedOf(sim));   // the lay drew after the Workshop's four
    }

    [Fact]
    public void AnEggTheRoomWalkLaysTakesItsFirstObjectStepNextTurnAsWhenTheSystemLaysAfterItsSteps()
    {
        static GameSimulation Make(bool walk)
        {
            // The egg's art as shipped (type 6), which is what the Hatchery's object step counts.
            GameDatabase fixture = Dk2TerrainFixture.Database();
            CreatureAnimationTiming[] art = new CreatureAnimationTiming[9];
            art[0] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 45, FramesPerSecond = 30 };
            art[5] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 15, FramesPerSecond = 30 };
            var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values, fixture.Variables,
                objects: fixture.Objects.Values.Select(o => o.ObjectId == HatcheryRules.EggObjectId ? o with { ArtSlots = art } : o));
            WorldState world = World(db);
            ISimulationSystem[] systems = walk
                ? [new RoomTickSystem(new HatcheryLayStep()), new HatcherySystem(lays: false)]
                : [new HatcherySystem()];
            var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), systems);
            Build(db, world, RoomTypeId.Hatchery, new TilePos(8, 8), size: 3);
            world.Tile(8, 8).HatcheryLastGeneratedTick = -10_000;   // the lay is due
            return sim;
        }

        GameSimulation walked = Make(walk: true), alone = Make(walk: false);
        bool sawAnEgg = false;
        for (int tick = 0; tick < 6; tick++)
        {
            walked.Tick();
            alone.Tick();
            sawAnEgg |= walked.World.Objects.Any(o => o.ObjectTypeId == HatcheryRules.EggObjectId);
            Assert.Equal(alone.World.Objects.Select(o => (o.ObjectTypeId, o.TileIndex, o.ProcessState, o.ProcessAgeTicks, o.TimerTicks, o.PhysicalVelocityX, o.PhysicalVelocityY, o.PhysicalVelocityZ)),
                walked.World.Objects.Select(o => (o.ObjectTypeId, o.TileIndex, o.ProcessState, o.ProcessAgeTicks, o.TimerTicks, o.PhysicalVelocityX, o.PhysicalVelocityY, o.PhysicalVelocityZ)));
        }

        Assert.True(sawAnEgg);
    }
}
