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
        // The control has the Workshop's timers running and the generator set to where the Workshop's four reloads
        // leave it: the walk must hand the lay exactly that state.
        static (GameSimulation Sim, WorldState World) Make(bool control, uint? reseed = null)
        {
            GameDatabase db = Dk2TerrainFixture.Database();
            WorldState world = World(db);
            GameSimulation sim = Sim(db, world, new HatcheryLayStep(), new WorkshopTimerSystem());
            Build(db, world, RoomTypeId.Hatchery, new TilePos(8, 8), size: 3);
            Build(db, world, RoomTypeId.Workshop, new TilePos(16, 8));
            world.Tile(new TilePos(8, 8)).HatcheryLastGeneratedTick = -10_000;   // the lay is due
            if (control)
            {
                ref TileState t = ref world.Tile(new TilePos(16, 8));
                (t.WorkshopSteamTimer, t.WorkshopLavaSpillTimer, t.WorkshopSmokeTimer, t.WorkshopLavaBubblingTimer) = (50, 50, 50, 50);
                ((Dk2Lcg)sim.Rng.Algorithm).Reseed(reseed!.Value);
            }
            else ZeroTimers(world, new TilePos(16, 8));
            return (sim, world);
        }

        (GameSimulation walked, WorldState world) = Make(control: false);
        var oracle = new Oracle(SeedOf(walked));
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        (GameSimulation control, WorldState controlWorld) = Make(control: true, oracle.Seed);

        walked.Tick();
        control.Tick();

        Assert.Equal(steam, world.Tile(new TilePos(16, 8)).WorkshopSteamTimer);
        ObjectState egg = Assert.Single(world.Objects, o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        ObjectState controlEgg = Assert.Single(controlWorld.Objects, o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        Assert.Equal((controlEgg.TileIndex, controlEgg.Heading), (egg.TileIndex, egg.Heading));
        Assert.Equal(SeedOf(control), SeedOf(walked));
        Assert.NotEqual(oracle.Seed, SeedOf(walked));   // and the lay did draw
    }

    [Fact]
    public void ALaunchingEggStepsInTheThingPassBeforeTheRoomWalkLaysSoItsHeadingDrawComesFirst()
    {
        // World_UpdateTurn runs the thing pass (0x004B7830) before the room walk (0x004EC450): an egg already
        // leaving its coop draws its heading, random(0x100), before the Hatchery's handler lays the next one.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)),
            [new HatcherySystem(lays: false), new RoomTickSystem(new HatcheryLayStep())]);
        Build(db, world, RoomTypeId.Hatchery, new TilePos(8, 8), size: 3);
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(8, 8)))!;
        HatcheryRules.PlaceCoops(world, db, room);
        ObjectState coop = world.Objects.First(o => o.ObjectTypeId == HatcheryRules.CoopObjectId);
        ObjectState leaving = world.SpawnObject(db, HatcheryRules.EggObjectId, Keeper, coop.TileIndex);
        leaving.ProcessState = EggLaunchRules.LaunchProcess;
        leaving.ProcessAgeTicks = EggLaunchRules.HiddenTicks - 1;
        world.Tile(8, 8).HatcheryLastGeneratedTick = -10_000;   // the lay is due
        int eggsBefore = world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        int side = AutoFurnitureRules.Choice(coop.TileIndex % world.Width, coop.TileIndex / world.Width, 4) << 9;
        int expected = (side + new Oracle(SeedOf(sim)).Next(0x100) - 0x80) & 0x7FF;

        sim.Tick();

        Assert.Equal(expected, leaving.Heading);
        Assert.Equal(eggsBefore + 1, world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.EggObjectId));
    }

    [Fact]
    public void TheDefaultSystemsStepALaunchingEggBeforeTheWalkLaysAfterIt()
    {
        // The same as the launching-egg test, on SimulationSystems.Default(): the eggs' object step is in the thing
        // pass, so the heading draw precedes the lay's, whatever else the declared order holds.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), SimulationSystems.Default());
        Build(db, world, RoomTypeId.Hatchery, new TilePos(8, 8), size: 3);
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(8, 8)))!;
        HatcheryRules.PlaceCoops(world, db, room);
        ObjectState coop = world.Objects.First(o => o.ObjectTypeId == HatcheryRules.CoopObjectId);
        ObjectState leaving = world.SpawnObject(db, HatcheryRules.EggObjectId, Keeper, coop.TileIndex);
        leaving.ProcessState = EggLaunchRules.LaunchProcess;
        leaving.ProcessAgeTicks = EggLaunchRules.HiddenTicks - 1;
        world.Tile(8, 8).HatcheryLastGeneratedTick = -10_000;   // the lay is due
        int eggsBefore = world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        int side = AutoFurnitureRules.Choice(coop.TileIndex % world.Width, coop.TileIndex / world.Width, 4) << 9;
        int expected = (side + new Oracle(SeedOf(sim)).Next(0x100) - 0x80) & 0x7FF;

        sim.Tick();

        Assert.Equal(expected, leaving.Heading);
        Assert.Equal(eggsBefore + 1, world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.EggObjectId));
    }

    [Fact]
    public void ANewerWorkshopsReloadsComeBeforeAnOlderHeartsSparkleAndItsOwnStepsDrawNothingMore()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        TilePos heart = new(8, 8), workshop = new(16, 8);
        GameSimulation sim = Sim(db, world, new StoreSweepSystem(), new HeartDefenceSystem(), new HeartFrameStep(), new WorkshopTimerSystem());
        Build(db, world, RoomTypeId.DungeonHeart, heart);
        Build(db, world, RoomTypeId.Workshop, workshop);
        ZeroTimers(world, workshop);
        var oracle = new Oracle(SeedOf(sim));
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        oracle.Next(100);   // the Heart's sparkle (0x004F3A20), once the newer room is done

        sim.Tick();

        Assert.Equal(steam, world.Tile(workshop).WorkshopSteamTimer);
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void ANewerWorkshopsReloadsComeBeforeAnOlderCasinosPartyDraw()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        TilePos casino = new(8, 8), workshop = new(16, 8);
        GameSimulation sim = Sim(db, world, new CasinoSystem(), new WorkshopTimerSystem());
        Build(db, world, RoomTypeId.Casino, casino);
        Build(db, world, RoomTypeId.Workshop, workshop);
        // A party on, with someone holding a place, draws a tile for its effect every twelve game seconds
        // (0x004D6561), on the turn that is a multiple of them: our tick 47.
        world.Tile(casino).CasinoParty = CasinoJackpotRules.PartyBit;
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, casino).HeldPlace =
            new RoomPlace(world.TileIndex(casino), RoomCapacityRules.Units);
        int turn = (CasinoJackpotRules.EffectSeconds * Ticks) - 1;
        ref TileState timers = ref world.Tile(workshop);
        (timers.WorkshopSteamTimer, timers.WorkshopLavaSpillTimer, timers.WorkshopSmokeTimer, timers.WorkshopLavaBubblingTimer) =
            (turn + 5, turn + 5, turn + 5, turn + 5);
        uint before = SeedOf(sim);
        sim.Run(turn);
        Assert.Equal(before, SeedOf(sim));   // nothing drew on the way: the party's first draw is the one at the turn
        ZeroTimers(world, workshop);
        var oracle = new Oracle(before);
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        oracle.Next(1);   // the Casino's effect tile, one tile to pick

        sim.Tick();

        Assert.Equal(steam, world.Tile(workshop).WorkshopSteamTimer);
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void ANewerWorkshopsReloadsComeBeforeAnOlderPortalsPick()
    {
        const byte goblin = 7;

        static (GameSimulation Sim, WorldState World) Make(bool control, uint? reseed = null)
        {
            var level = new GlobalVariables();
            level.CreaturePool.Add(new CreaturePoolEntry(goblin, 15, Keeper));
            level.Availabilities.Add(new AvailabilityEntry(2, Keeper, goblin, 3));
            GameDatabase db = Dk2TerrainFixture.Database().ForLevel(level);
            WorldState world = World(db);
            GameSimulation sim = Sim(db, world, new PortalSystem(), new WorkshopTimerSystem());
            Build(db, world, RoomTypeId.Lair, new TilePos(4, 4));
            Build(db, world, RoomTypeId.Portal, new TilePos(8, 8), size: 3);
            Build(db, world, RoomTypeId.Workshop, new TilePos(16, 8));
            RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(8, 8)))!;
            world.TileAtIndex(PortalRules.EntranceTileIndex(world, room)).EntranceIdleTicks =
                PortalRules.SpawnIntervalTicks(db, 0) - 1;   // the roll is due this turn
            if (control)
            {
                ref TileState t = ref world.Tile(new TilePos(16, 8));
                (t.WorkshopSteamTimer, t.WorkshopLavaSpillTimer, t.WorkshopSmokeTimer, t.WorkshopLavaBubblingTimer) = (50, 50, 50, 50);
                ((Dk2Lcg)sim.Rng.Algorithm).Reseed(reseed!.Value);
            }
            else ZeroTimers(world, new TilePos(16, 8));
            return (sim, world);
        }

        (GameSimulation walked, WorldState world) = Make(control: false);
        var oracle = new Oracle(SeedOf(walked));
        int steam = Reload(oracle);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);
        (GameSimulation control, WorldState controlWorld) = Make(control: true, oracle.Seed);

        walked.Tick();
        control.Tick();

        Assert.Equal(steam, world.Tile(new TilePos(16, 8)).WorkshopSteamTimer);
        CreatureState arrived = Assert.Single(world.Creatures, c => c.CreatureTypeId == goblin);
        CreatureState controlArrived = Assert.Single(controlWorld.Creatures, c => c.CreatureTypeId == goblin);
        Assert.Equal((controlArrived.Tile, controlArrived.Heading), (arrived.Tile, arrived.Heading));
        Assert.Equal(SeedOf(control), SeedOf(walked));
        Assert.NotEqual(oracle.Seed, SeedOf(walked));   // and the pick did draw
    }
}
