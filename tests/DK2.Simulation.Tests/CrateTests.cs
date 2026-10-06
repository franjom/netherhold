// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Trap and door crates (ADR-0214): the Workshop that holds them, the keeper's blueprint
/// list, and the imp that carries a crate to a blueprint and builds it (task kinds 9 and 10,
/// states 34 to 37).</summary>
public class CrateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte MakerId = 61;
    private const byte DoorCrate = Dk2TerrainFixture.WoodenDoorCrateObjectId;
    private const byte TrapCrate = Dk2TerrainFixture.AlarmCrateObjectId;
    private const int Second = SimulationTiming.TicksPerSecond;

    // The fixture's claimed 3x3 at (1..3, 1..3) is the Workshop; a corridor runs east along y = 2.
    private static readonly TilePos DoorAt = new(9, 2);
    private static readonly TilePos ImpAt = new(6, 2);

    /// <summary>A maker at 40 a second whose content table is Manufacture alone, as the shipped
    /// Troll's is.</summary>
    private static GameDatabase Db() => Dk2TerrainFixture.Database(
        Dk2TerrainFixture.Goblin() with
        {
            CreatureId = MakerId, Name = "Maker", ManufacturePerSecond = 40,
            HappyJobs = [new(JobType.Manufacture, 0, 0, 100)],
        });

    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        for (int x = 4; x <= 12; x++)
        {
            world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, 2).OwnerPlayerId = Keeper;
        }

        Dk2TerrainFixture.FurnishRooms(db, world);
        return world;
    }

    private static DoorThing Door(TilePos at, byte flags = 0x02) => new(new byte[20])
    {
        X = at.X, Y = at.Y, DoorId = 1, PlayerId = Keeper, Flags = flags, Unknown_0011 = new byte[3],
    };

    private static TrapThing Trap(TilePos at, byte shots = 0) => new(new byte[16])
    {
        X = at.X, Y = at.Y, TrapId = 7, PlayerId = Keeper, NumberOfShots = shots,
    };

    private static RoomInstance Workshop(GameDatabase db, WorldState world) =>
        RoomIndex.RoomAt(world, db, world.Width + 1)!;

    private static ObjectState Crate(GameDatabase db, WorldState world, byte type, TilePos at, byte owner = Keeper) =>
        world.SpawnObject(db, type, owner, (at.Y * world.Width) + at.X);

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink, params ISimulationSystem[] extra) =>
        Observed(new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(), new CreatureMovementSystem(), new ImpTaskSystem(), new CreatureJobSystem(), .. extra,
        ]), sink);

    private static GameSimulation Observed(GameSimulation sim, ListEventSink sink)
    {
        sim.ObserveWith(sink);
        return sim;
    }

    private static CreatureState Maker(GameDatabase db, WorldState world, TilePos at)
    {
        CreatureState it = world.SpawnCreature(db, MakerId, Keeper, at);
        it.CurrentJob = JobType.Manufacture;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, JobRules.StateFor(JobType.Manufacture)!.Value);
        it.JobTileIndex = (at.Y * world.Width) + at.X;
        world.Player(Keeper).ManufacturingCreatures++; // placed, so no enter hook counted it
        return it;
    }

    // ---- kinds and the blueprint list -------------------------------------------

    [Fact]
    public void KindZeroIsADoorAndKindOneATrap_OnAnOrderAndOnACrate()
    {
        // 0x00506BA0: the door definition for kind 0, the trap definition for 1.
        GameDatabase db = Db();
        Assert.Equal(DoorCrate, ManufactureRules.CrateObjectId(db, new ManufactureOrder(0, 1, 1000)));
        Assert.Equal(TrapCrate, ManufactureRules.CrateObjectId(db, new ManufactureOrder(1, 7, 1000)));
        Assert.Equal((byte)0, CrateRules.KindOf(db, DoorCrate));
        Assert.Equal((byte)1, CrateRules.KindOf(db, TrapCrate));
        Assert.Equal((byte)0, CrateRules.DoorKind);
        Assert.Equal((byte)1, ManufactureRules.TrapKind);
    }

    [Fact]
    public void ADoorBlueprintInTheLevelJoinsTheList_AndATrapBlueprintIsNeitherListedNorOrdered()
    {
        // The door loader 0x00520F40 pushes both; the trap loader pushes neither.
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt), Trap(new TilePos(11, 2))]);

        Assert.Equal([new BlueprintRef(CrateRules.DoorKind, world.Doors[0].Id)], world.Player(Keeper).Blueprints);
        ManufactureOrder order = Assert.Single(world.Player(Keeper).ManufactureQueue);
        Assert.Equal(CrateRules.DoorKind, order.Kind);
        Assert.True(Assert.Single(world.Traps).UnderConstruction);
    }

    // ---- the crate maker ----------------------------------------------------------

    [Fact]
    public void AMadeCrateIsTheWorkshops_AndEveryoneWorkingThereIsSentThrough68ToIdle()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        CreatureState maker = Maker(db, world, new TilePos(2, 2));
        CreatureState other = Maker(db, world, new TilePos(3, 3));
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        world.Player(Keeper).ManufacturePoints = 995;
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second);

        ObjectState crate = Assert.Single(world.Objects, o => o.ObjectTypeId == DoorCrate);
        Assert.Equal(Keeper, crate.OwnerPlayerId);
        Assert.True(CrateRules.IsListedIn(world, db, Workshop(db, world), crate));
        Assert.Equal(2, sink.Events.Count(e => e.Kind == "crate_celebrated"));
        Assert.Equal(1, world.Player(Keeper).ItemsManufactured);
    }

    [Fact]
    public void TwoMakersWorkOneQueue_OneCrateAtATime_InHalfTheTime()
    {
        // CONFIRMED (owner, 2026-09-25): one queue for the keeper; more workers make the same
        // crate sooner, not a crate each. 1000 points at 40 a second each: 12.5 s for two.
        GameDatabase db = Db();
        WorldState world = World(db);
        Maker(db, world, new TilePos(1, 1));
        Maker(db, world, new TilePos(3, 3));
        List<ManufactureOrder> queue = world.Player(Keeper).ManufactureQueue;
        queue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        queue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        GameSimulation sim = Simulation(db, world, new ListEventSink());

        sim.Run(Second * 12);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == DoorCrate);
        sim.Run(Second * 2);
        Assert.Single(world.Objects, o => o.ObjectTypeId == DoorCrate);
        Assert.Single(queue);
    }

    [Fact]
    public void AMakerWhoseTableIsManufactureAloneGoesStraightBackToWorkAfterACrate()
    {
        // CONFIRMED (owner, 2026-09-25, Q432): they carry on working. 68 ends in idle, and idle's
        // chooser takes Manufacture again for a creature whose content table holds nothing else.
        GameDatabase db = Db();
        WorldState world = World(db);
        TilePos at = new(2, 2);
        CreatureState maker = Maker(db, world, at);
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(CrateRules.DoorKind, 1, 1000));
        world.Player(Keeper).ManufacturePoints = 995;
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second);

        Assert.Contains(sink.Events, e => e.Kind == "crate_celebrated");
        Assert.True(maker.CurrentJob == JobType.Manufacture,
            string.Join(" | ", sink.Events.Select(e => $"{e.Tick} {e.Kind} {e.Detail}")));
        Assert.Equal(CreatureStateCategory.Manufacture, CreatureStateMachine.UpdatingCategory(maker));
        Assert.Equal(at, maker.Tile);
    }

    [Fact]
    public void AWorkshopWhoseCratesFillItsTilesLeavesTheNewCrateToNobody_AndRefusesAWorker()
    {
        // 0x004F6140: crates / 3 must stay below the tiles less the furniture; 27 crates fill 9.
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance workshop = Workshop(db, world);
        foreach (int tile in workshop.TileIndices)
            for (int k = 0; k < 3; k++) world.SpawnObject(db, DoorCrate, Keeper, tile);
        Assert.False(CrateRules.WorkshopHasRoom(world, db, workshop));
        Assert.False(RoomCapacityRules.HasRoom(world, db, workshop));

        ObjectState made = world.SpawnObject(db, DoorCrate, PlayerIds.Unowned, workshop.TileIndices[0]);
        Assert.False(CrateRules.List(new StateHookContext(world, db), workshop, made));
        Assert.Equal(PlayerIds.Unowned, made.OwnerPlayerId);
    }

    [Fact]
    public void CratesAreSetDownThreeToATile_NewestFirst_OffTheFurnitureSlots()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance workshop = Workshop(db, world);
        var crates = new List<ObjectState>();
        for (int i = 0; i < 4; i++)
        {
            ObjectState crate = world.SpawnObject(db, DoorCrate, PlayerIds.Unowned, workshop.TileIndices[0]);
            Assert.True(CrateRules.List(new StateHookContext(world, db), workshop, crate));
            crates.Add(crate);
        }

        var tiles = new List<int>();
        CrateRules.CrateTiles(world, db, workshop, tiles);
        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, db, workshop, slots);
        Assert.DoesNotContain(tiles, slots.Contains);
        Assert.Contains(world.Width * 2 + 2, slots);  // the centre, the only interior tile
        Assert.Equal(tiles[0], crates[3].TileIndex);
        Assert.Equal(tiles[0], crates[1].TileIndex);
        Assert.Equal(tiles[1], crates[0].TileIndex);

        // Enough crates to pass the slots in index order: none is set down on one.
        for (int i = 0; i < 11; i++)
            Assert.True(CrateRules.List(new StateHookContext(world, db), workshop, world.SpawnObject(db, DoorCrate, PlayerIds.Unowned, workshop.TileIndices[0])));
        Assert.All(world.Objects.Where(o => o.ObjectTypeId == DoorCrate), o => Assert.DoesNotContain(o.TileIndex, slots));
    }

    // ---- the imp ------------------------------------------------------------------

    [Fact]
    public void AnImpTakesTheWorkshopsCrateToTheListedDoor_AndBuildsIt()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        ObjectState crate = Crate(db, world, DoorCrate, new TilePos(2, 2));
        Assert.True(CrateRules.List(new StateHookContext(world, db), Workshop(db, world), crate));
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second * 20);

        Assert.Contains(sink.Events, e => e.Kind == "crate_claimed" && e.Detail.Contains("how=blueprint_list"));
        Assert.Contains(sink.Events, e => e.Kind == "door_built");
        Assert.False(world.Doors[0].UnderConstruction);
        Assert.DoesNotContain(world.Objects, o => o.Id == crate.Id);
        Assert.Empty(world.Player(Keeper).Blueprints);
    }

    [Fact]
    public void TheCrateTakenOutOfTheWorkshopBelongsToNobodyWhileCarried()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        ObjectState crate = Crate(db, world, DoorCrate, new TilePos(2, 2));
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), crate);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        while (!sink.Events.Any(e => e.Kind == "crate_picked_up") && sim.CurrentTick.Value < Second * 20) sim.Tick();

        Assert.True(crate.CarriedByCreatureId >= 0);
        Assert.Equal(PlayerIds.Unowned, crate.OwnerPlayerId);
    }

    [Fact]
    public void OneImpTakesABlueprint_TheOtherLeavesIt()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), Crate(db, world, DoorCrate, new TilePos(2, 2)));
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), Crate(db, world, DoorCrate, new TilePos(2, 2)));
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(7, 2));
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second * 3);

        Assert.Single(sink.Events, e => e.Kind == "crate_claimed");
    }

    [Fact]
    public void AListedBlueprintThatIsAlreadyBuiltLeavesTheList()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        world.Doors[0].UnderConstruction = false;
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);

        Simulation(db, world, new ListEventSink()).Run(Second * 2);

        Assert.Empty(world.Player(Keeper).Blueprints);
    }

    [Fact]
    public void ALooseTrapCrateGoesStraightToATrapBlueprint_ThoughNoListNamesIt()
    {
        // Kind 8's crate branch 0x00468910 walks the keeper's traps, not the blueprint list.
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Trap(new TilePos(11, 2))]);
        ObjectState crate = Crate(db, world, TrapCrate, new TilePos(5, 2), PlayerIds.Unowned);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second * 20);

        Assert.Contains(sink.Events, e => e.Kind == "crate_claimed" && e.Detail.Contains("how=loose"));
        Assert.Contains(sink.Events, e => e.Kind == "trap_built");
        Assert.False(world.Traps[0].UnderConstruction);
        Assert.DoesNotContain(world.Objects, o => o.Id == crate.Id);
    }

    /// <summary>The objects handler asks a crate's blueprint branch (<c>0x00468910</c>) before the
    /// latch it shares with books (<c>0x0046B9C0</c>, ADR-0380): a book with no Library, earlier
    /// in the tile's list, does not hide the trap crate after it from its blueprint.</summary>
    [Fact]
    public void ABookWithNoLibraryDoesNotHideACrateFromItsBlueprint()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Trap(new TilePos(11, 2))]);
        world.SpawnObject(db, Dk2TerrainFixture.SpellBookObjectId, PlayerIds.Unowned, (2 * world.Width) + 5);
        Crate(db, world, TrapCrate, new TilePos(5, 2), PlayerIds.Unowned);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second * 2);

        Assert.Contains(sink.Events, e => e.Kind == "crate_claimed" && e.Detail.Contains("how=loose"));
    }

    [Fact]
    public void ALooseCrateNoBlueprintNeedsIsCarriedIntoTheWorkshop_AndJoinsIt()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ObjectState crate = Crate(db, world, DoorCrate, new TilePos(8, 2), PlayerIds.Unowned);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(Second * 20);

        Assert.Equal(Keeper, crate.OwnerPlayerId);
        Assert.True(CrateRules.IsListedIn(world, db, Workshop(db, world), crate));
        Assert.Contains(sink.Events, e => e.Kind == "object_stored");
    }

    [Fact]
    public void ACrateCarriedToABlueprintThatIsGoneIsSetDownLoose()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        ObjectState crate = Crate(db, world, DoorCrate, new TilePos(2, 2));
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), crate);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        while (!sink.Events.Any(e => e.Kind == "crate_picked_up") && sim.CurrentTick.Value < Second * 20) sim.Tick();

        world.Doors[0].UnderConstruction = false;
        sim.Run(Second * 10);

        Assert.Contains(sink.Events, e => e.Kind == "crate_dropped" && e.Detail.Contains("reason=no_blueprint"));
        Assert.Contains(world.Objects, o => o.Id == crate.Id);
    }

    [Fact]
    public void ACarrierWhoseWalkEndsShortOfTheBlueprintBuildsNothing_AndGoesIdle()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        ObjectState crate = Crate(db, world, DoorCrate, new TilePos(2, 2));
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), crate);
        world.SpawnCreature(db, ImpId, Keeper, ImpAt);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        while (!sink.Events.Any(e => e.Kind == "crate_picked_up") && sim.CurrentTick.Value < Second * 20) sim.Tick();

        world.Tile(8, 2).TerrainId = Dk2TerrainFixture.Rock;   // the corridor is cut before the door
        world.Tile(8, 2).OwnerPlayerId = PlayerIds.Unowned;
        sim.Run(Second * 10);

        Assert.True(world.Doors[0].UnderConstruction);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "door_built");
        Assert.Contains(sink.Events, e => e.Kind == "crate_dropped" && e.Detail.Contains("reason=no_route"));
    }

    [Fact]
    public void AnImpDroppedOnABlueprintTakesItsCrateBeforeTheWallsBesideIt()
    {
        // The drop scan's third step is 0x0046C1A0, traps and doors, and its ring scan hands the
        // landing tile to the chain first: the rock beside it is on ring 1 (ADR-0292). Dropped one
        // tile short, the imp would reinforce (8,1), which the chooser visits first.
        GameDatabase db = Db();
        WorldState world = World(db);
        ThingProjection.Place(world, db, [Door(DoorAt)]);
        world.Player(Keeper).ManufactureQueue.Clear();
        world.Player(Keeper).Blueprints.Clear();   // so only the drop scan can find it
        CrateRules.List(new StateHookContext(world, db), Workshop(db, world), Crate(db, world, DoorCrate, new TilePos(2, 2)));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, DoorAt);
        imp.JustDropped = true;
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(2);

        Assert.Contains(sink.Events, e => e.Kind == "crate_claimed" && e.Detail.Contains("how=dropped"));
    }
}
