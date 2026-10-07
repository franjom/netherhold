// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Workshop's states 63–65 (workshop-states.md, ADR-0399).</summary>
public class WorkshopStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Vampire = Dk2TerrainFixture.VampireCreatureId;

    /// <summary>The Vampire with the work's art (slot 22).</summary>
    private static GameDatabase Db()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        foreach (int slot in new[] { 1, 20, WorkshopStep.WorkArtSlot }) slots[slot] = clip;
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Vampire ? c with { ArtSlots = slots } : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    /// <summary>A 5×5 Workshop of the keeper's from (1,1), furnished, with an order too dear to finish.</summary>
    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 5; y++)
        for (int x = 1; x <= 5; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(ManufactureRules.DoorKind, 1, 1_000_000));
        return world;
    }

    private static CreatureState Maker(GameDatabase db, WorldState world, TilePos at, int state = WorkshopStep.ChoosingState)
    {
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, at);
        it.CurrentJob = JobType.Manufacture;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, state);
        it.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, RoomCapacityRules.Units);   // the enter hook's claim
        world.Player(Keeper).ManufacturingCreatures++;   // and its count (ManufactureTests)
        return it;
    }

    private static RoomInstance Workshop(GameDatabase db, WorldState world) =>
        RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Workshop);

    private static GameSimulation Simulation(GameDatabase db, WorldState world) =>
        new(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);

    [Fact]
    public void AManufacturerWalksToAMachineFacesItWorksAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        Assert.Contains(world.Objects, o => WorkshopStep.IsMachine(o.ObjectTypeId));
        CreatureState it = Maker(db, world, new TilePos(3, 3));
        GameSimulation sim = Simulation(db, world);
        var seen = new HashSet<int>();

        for (int i = 0; i < 60 * SimulationTiming.TicksPerSecond; i++)
        {
            sim.Tick();
            seen.Add(CreatureStateTable.Get(it.Dk2StateId).IsUtility ? it.UtilityNewState : it.Dk2StateId);
        }

        Assert.Superset(new HashSet<int> { WorkshopStep.ChoosingState, WorkshopStep.AtTheMachineState, WorkshopStep.WorkingState }, seen);
        Assert.Equal(JobType.Manufacture, it.CurrentJob);
    }

    [Fact]
    public void TheFinderGivesTheMachineAnotherWorksAtAsASpotBesideIt()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world);
        CreatureState first = Maker(db, world, new TilePos(3, 3));
        Assert.Equal(WorkshopStep.AtIt, WorkshopStep.Find(world, db, room, first, out ObjectState? machine, out _));
        (first.StateTargetX, first.StateTargetY) = AutoFurnitureRules.FurniturePosition(world, db, room, machine!);
        first.MachineMode = WorkshopStep.AtIt;
        foreach (ObjectState other in world.Objects.Where(o => WorkshopStep.IsMachine(o.ObjectTypeId) && o.Id != machine!.Id).ToList())
            world.RemoveObject(other.Id);
        CreatureState second = Maker(db, world, new TilePos(3, 3));

        byte? mode = WorkshopStep.Find(world, db, room, second, out ObjectState? found, out (int X, int Y) point);

        Assert.Equal(WorkshopStep.BesideIt, mode);
        Assert.Equal(machine!.Id, found!.Id);
        Assert.NotEqual(AutoFurnitureRules.FurniturePosition(world, db, room, machine), point);
    }

    [Fact]
    public void BesideAFloorMachineTheStandPointIsTheFirstFreeSide_NorthThenEast()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world);
        ObjectState machine = world.Objects.First(o => WorkshopStep.IsMachine(o.ObjectTypeId)
            && AutoFurnitureRules.WallSide(world, db, room, o.TileIndex) < 0);
        foreach (ObjectState other in world.Objects.Where(o => WorkshopStep.IsMachine(o.ObjectTypeId) && o.Id != machine.Id).ToList())
            world.RemoveObject(other.Id);
        (int mx, int my) = AutoFurnitureRules.FurniturePosition(world, db, room, machine);
        CreatureState worker = Maker(db, world, new TilePos(mx >> 12, my >> 12));
        (worker.StateTargetX, worker.StateTargetY, worker.MachineMode) = (mx, my, WorkshopStep.AtIt);
        CreatureState seeker = Maker(db, world, new TilePos(1, 1));
        // r = (Width / 2 + footprint / 2) × 100 / 100 (0x004F5BE0).
        int r = (((ushort)db.ObjectById(machine.ObjectTypeId)!.Width.Raw >> 1) + ((ushort)db.CreatureById(Vampire).FootprintRadius.Raw >> 1)) * 100 / 100;

        WorkshopStep.Find(world, db, room, seeker, out _, out (int X, int Y) north);
        world.SpawnCreature(db, Vampire, Keeper, new TilePos(mx >> 12, (my >> 12) - 1));   // the north side taken
        WorkshopStep.Find(world, db, room, seeker, out _, out (int X, int Y) east);

        Assert.Equal((mx, my - r), north);   // angle 0: (sin 0, -cos 0) × r
        Assert.Equal((mx + r, my), east);    // angle 0x200
    }

    [Fact]
    public void WithNoOrder63PlaysActionFortyThreeOnceAndThenGoesIdle()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        world.Player(Keeper).ManufactureQueue.Clear();
        CreatureState it = Maker(db, world, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]);

        sim.Run(2);
        Assert.Equal(WorkshopStep.ChoosingState, it.Dk2StateId);
        Assert.True(it.ActionUntilTick > sim.World.CurrentTick.Value);
        for (int i = 0; i < 100 && it.CurrentJob != JobType.None; i++) sim.Tick();

        Assert.Equal(JobType.None, it.CurrentJob);
    }

    [Fact]
    public void TheSeekersOwnTargetCountsAsWorked()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world);
        CreatureState it = Maker(db, world, new TilePos(3, 3));
        WorkshopStep.Find(world, db, room, it, out ObjectState? first, out _);
        (it.StateTargetX, it.StateTargetY) = AutoFurnitureRules.FurniturePosition(world, db, room, first!);
        it.MachineMode = WorkshopStep.AtIt;

        WorkshopStep.Find(world, db, room, it, out ObjectState? second, out _);

        Assert.NotEqual(first!.Id, second?.Id);
    }

    [Fact]
    public void WithNoMachineOnTheTarget64GoesTo63AndStillStartsTheWork_AsDK2Does()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState it = Maker(db, world, new TilePos(3, 3), WorkshopStep.AtTheMachineState);
        (it.StateTargetX, it.StateTargetY) = (CreatureState.CentreOf(18), CreatureState.CentreOf(18));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]);
        sim.ObserveWith(sink);

        sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "workshop_work");
        Assert.Equal(WorkshopStep.WorkingState, it.Dk2StateId);
    }

    [Fact]
    public void TheWorkIsActionThirtyTwoSixToTwelveMorePlays()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState it = Maker(db, world, new TilePos(3, 3));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(sink);

        for (int i = 0; i < 400 && it.Dk2StateId != WorkshopStep.WorkingState; i++) sim.Tick();

        SimEvent work = Assert.Single(sink.Events, e => e.Kind == "workshop_work");
        int count = int.Parse(work.Detail.Split(' ')[1]["count=".Length..]);
        Assert.InRange(count, 6, 12);
    }

    [Fact]
    public void AManufacturerMovedWhileWorkingEndsTheWorkAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState it = Maker(db, world, new TilePos(3, 3), WorkshopStep.WorkingState);
        it.ActionUntilTick = long.MaxValue;
        (it.WaitPosX, it.WaitPosY) = (it.PosX - 100, it.PosY);
        it.StateEnteredTick = -5;

        new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]).Tick();

        Assert.Equal(WorkshopStep.ChoosingState, it.Dk2StateId);
        Assert.Equal(0, it.ActionUntilTick);
    }
}
