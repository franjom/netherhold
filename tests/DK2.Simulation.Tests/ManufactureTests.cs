// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Workshop (ADR-0203): the category 5 update credits the keeper's manufacture points
/// every turn, and a paid-for order becomes a crate in the Workshop.</summary>
public class ManufactureTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte MakerId = 61;
    private const int Second = SimulationTiming.TicksPerSecond;
    private static readonly TilePos Bench = new(3, 3);

    /// <summary>A Goblin that manufactures 40 a second: 10 a turn after the loader's division.</summary>
    private static GameDatabase Db() => Dk2TerrainFixture.Database(
        Dk2TerrainFixture.Goblin() with { CreatureId = MakerId, Name = "Maker", ManufacturePerSecond = 40 });

    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 2; y <= 4; y++)
        for (int x = 2; x <= 4; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        return world;
    }

    private static CreatureState Maker(GameDatabase db, WorldState world)
    {
        CreatureState it = world.SpawnCreature(db, MakerId, Keeper, Bench);
        it.CurrentJob = JobType.Manufacture;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, JobRules.StateFor(JobType.Manufacture)!.Value);
        it.JobTileIndex = (Bench.Y * world.Width) + Bench.X;
        world.Player(Keeper).ManufacturingCreatures++; // placed, so no enter hook counted it
        return it;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureJobSystem(), new ManufactureDecaySystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void AManufacturerCreditsItsKeeperEveryTurn()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState maker = Maker(db, world);
        Assert.Equal(10, ManufactureRules.PerOriginalTick(db, db.CreatureById(MakerId), maker));
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(ManufactureRules.DoorKind, 1, 60_000));   // 63 idles with none; never paid here

        Simulation(db, world, new ListEventSink()).Run(Second * 5);

        Assert.Equal(200, world.Player(Keeper).ManufacturePoints);
    }

    [Fact]
    public void APaidForOrderBecomesACrateAndThePointsGoToZero()
    {
        // 1000 for a Wooden Door: 100 turns at 10. The overshoot is lost, not carried.
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState maker = Maker(db, world);
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(ManufactureRules.DoorKind, 1, 1000));
        world.Player(Keeper).ManufacturePoints = 995;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(Second);

        ObjectState crate = Assert.Single(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.WoodenDoorCrateObjectId);
        Assert.Equal(Keeper, crate.OwnerPlayerId);
        Assert.Empty(world.Player(Keeper).ManufactureQueue);
        Assert.Equal(1, world.Player(Keeper).ItemsManufactured);
        Assert.InRange(world.Player(Keeper).ManufacturePoints, 0, 30);
        Assert.Contains(sink.Events, e => e.Kind == "crate_manufactured");
    }

    [Fact]
    public void AnOrderWrittenOntoSlotZeroAfterTheRingWrapsSetsTheCost()
    {
        var keeper = new PlayerState { ManufactureQueue = [] };
        for (int i = 0; i < ManufactureRules.QueueCapacity; i++)
            ManufactureRules.Enqueue(ref keeper, new ManufactureOrder(ManufactureRules.DoorKind, 1, i == 0 ? 3000 : 1500));
        keeper.ManufactureQueue.Clear();   // all paid: the head has moved past every slot
        Assert.Equal(0, keeper.ManufactureRingTail);
        Assert.Equal(3000, keeper.ManufactureSlotZeroCost);

        ManufactureRules.Enqueue(ref keeper, new ManufactureOrder(ManufactureRules.TrapKind, 3, 1500));
        Assert.Equal(1500, keeper.ManufactureSlotZeroCost);
        Assert.Equal(1, keeper.ManufactureRingTail);
    }

    [Fact]
    public void WithNothingOrderedTheManufacturerPlaysItsIdleActionAndStops()
    {
        // 63 (0x00470110): the keeper's order count player+0xA1C at zero, action 43 once, then 0.
        GameDatabase db = Db();
        db.Variables.Misc[VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond] =
            new MiscVariable(VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond, 0, 0, 0);
        WorldState world = World(db);
        CreatureState maker = Maker(db, world);

        Simulation(db, world, new ListEventSink()).Run(Second * 10);

        Assert.Equal(JobType.None, maker.CurrentJob);
        Assert.InRange(world.Player(Keeper).ManufacturePoints, 1, 399);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.WoodenDoorCrateObjectId);
    }

    [Fact]
    public void WithNobodyManufacturingThePointsFallByTheShippedOneASecond()
    {
        // ManufacturePointLossWhileNoWorkersPerSecond, 1 as shipped (0x004C3340).
        GameDatabase db = Db();
        db.Variables.Misc[VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond] =
            new MiscVariable(VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond, 1, 0, 0);
        WorldState world = World(db);
        world.Player(Keeper).ManufacturePoints = 3;

        Simulation(db, world, new ListEventSink()).Run(Second * 5);

        Assert.Equal(0, world.Player(Keeper).ManufacturePoints);
    }

    /// <summary>0x005066E0: an order only while the blueprints of a type outnumber its crates. The
    /// loader 0x00520F40 pushes per record, so a crate authored after the doors counts for neither:
    /// crate first gives one order, crate last two.</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void AnAuthoredDoorBlueprintCountsTheCratesLoadedBeforeIt(bool crateFirst, int orders)
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        var crate = new ObjectThing(new byte[24])
        {
            X = 3, Y = 3, ObjectId = Dk2TerrainFixture.WoodenDoorCrateObjectId, PlayerId = Keeper,
            Unknown_0008 = new byte[4],
        };
        ThingProjection.Place(world, db, crateFirst ? [crate, Door(6, 6), Door(7, 6)] : [Door(6, 6), Door(7, 6), crate]);

        Assert.Equal(Enumerable.Repeat(new ManufactureOrder(ManufactureRules.DoorKind, 1, 1000), orders),
            world.Player(Keeper).ManufactureQueue);
        Assert.Null(JobRules.Refused(world, db, db.CreatureById(MakerId), new CreatureState { OwnerPlayerId = Keeper }, JobType.Manufacture));
    }

    private static DoorThing Door(int x, int y) => new(new byte[20])
    {
        X = x, Y = y, DoorId = 1, PlayerId = Keeper, Flags = 0x02, Unknown_0011 = new byte[3],
    };
}
