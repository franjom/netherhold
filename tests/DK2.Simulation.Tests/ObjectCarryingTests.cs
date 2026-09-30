// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// DK2's imp task <b>kind 8</b>: carry a loose object to the room it belongs in
/// (ADR-0065).
///
/// <para>The third and last of the carrying tasks. Kind 13 carries the
/// unconscious to a bed or a cell and kind 12 carries the dead to a graveyard;
/// this one carries <i>things</i>, and unlike either of those it sits
/// <b>second</b> in the original's Tier A table — above marked excavation and
/// above claiming.</para>
/// </summary>
public class ObjectCarryingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const byte SpellBook = Dk2TerrainFixture.SpellBookObjectId;
    private const int Second = SimulationTiming.TicksPerSecond;

    // The fixture map's walkable ground is a 3x3 claimed room at (1..3, 1..3).
    private static readonly TilePos Shelf = new(3, 3);
    private static readonly TilePos Shelf2 = new(3, 2);
    private static readonly TilePos Loose = new(1, 1);
    private static readonly TilePos Middle = new(2, 2);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new TileTagSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new TileWorkSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, params TilePos[] at)
    {
        foreach (TilePos tile in at)
        {
            ref TileState state = ref world.Tile(tile.X, tile.Y);
            RoomRules.Place(db, ref state, db.Room(type), Keeper);
        }
    }

    private static ObjectState Drop(
        GameDatabase db, WorldState world, TilePos at, byte type = SpellBook,
        byte owner = PlayerIds.Unowned) =>
        world.SpawnObject(db, type, owner, (at.Y * world.Width) + at.X);

    // ---- where a thing belongs ---------------------------------------------

    [Fact]
    public void AnObjectsCarryFlagIsWhatSaysWhereALooseThingBelongs()
    {
        // DK2's destination helper 0x0046E480 tests the object's own flags (ADR-0183): a
        // spell book or a Special to the Library, a crate to the Workshop.
        GameDatabase db = Db;

        Assert.Equal(RoomTypeId.Library, ObjectCarryRules.HomeRoomFor(db, SpellBook));
        Assert.Equal(RoomTypeId.Library,
            ObjectCarryRules.HomeRoomFor(db, Dk2TerrainFixture.RevealMapSpecialObjectId));
        Assert.Equal(RoomTypeId.Workshop,
            ObjectCarryRules.HomeRoomFor(db, Dk2TerrainFixture.AlarmCrateObjectId));

        // The Library's own furniture has no carry flag, though the room lists it, and a
        // bed belongs to a creature.
        Assert.Null(ObjectCarryRules.HomeRoomFor(db, Dk2TerrainFixture.BookCaseObjectId));
        Assert.Null(ObjectCarryRules.HomeRoomFor(db, Dk2TerrainFixture.LibraryBookObjectId));
        Assert.Null(ObjectCarryRules.HomeRoomFor(db, Dk2TerrainFixture.GoblinBedObjectId));
    }

    [Fact]
    public void ABedIsNeverFetchedHoweverLongItLies()
    {
        // Two reasons, and either alone would do: a bed has no carry flag, and a
        // bed carries the creature id of whoever put it down.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, Loose);
        Paint(db, world, RoomTypeId.Library, Shelf);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Loose);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        ObjectState bed = Assert.Single(world.Objects);
        Assert.Equal(goblin.Id, bed.CreatureId);
        Assert.Equal(-1, bed.CarriedByCreatureId);
    }

    // ---- the carry ----------------------------------------------------------

    /// <summary>
    /// <b>Gold in the hands does not stop an imp shelving a book.</b> The objects handler
    /// (<c>0x0046B9C0</c>) reads nothing about what the imp holds (imp-task-objects-and-walks.md §1).
    /// </summary>
    [Fact]
    public void AnImpHoldingGoldStillPicksUpABook()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);
        imp.GoldHeld = 500;

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Run(4);

        Assert.Contains(sink.Events, e => e.Kind == "object_claimed");
    }

    /// <summary>
    /// <b>A failed Workshop search on a tile skips its books too.</b> The handler keeps one latch
    /// for specials and one shared by crates and books: a crate first in the tile's list with no
    /// Workshop to go to leaves the book beside it, though the Library has room. Alone, the book
    /// is taken.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AFailedWorkshopSearchOnATileSkipsItsBooksToo(bool crateFirst, bool bookTaken)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        world.Player(Keeper).Blueprints.Clear();
        if (crateFirst) Drop(db, world, Loose, Dk2TerrainFixture.AlarmCrateObjectId);
        Drop(db, world, Loose);
        world.SpawnCreature(db, ImpId, Keeper, Middle);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Run(4);

        Assert.Equal(bookTaken, sink.Events.Any(e => e.Kind == "object_claimed"));
    }

    [Fact]
    public void AnImpCarriesALooseSpellBookToTheLibrary()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState book = Drop(db, world, Loose);
        world.SpawnCreature(db, ImpId, Keeper, Middle);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        // On a library tile, and the keeper's rather than nobody's - which is
        // what takes it out of IsLoose and stops it being fetched for ever.
        Assert.Equal((Shelf2.Y * world.Width) + Shelf2.X, book.TileIndex);
        Assert.Equal(Keeper, book.OwnerPlayerId);
        Assert.Equal(-1, book.CarriedByCreatureId);
        Assert.Contains(sink.Events, e => e.Kind == "object_stored");
    }

    [Fact]
    public void AShelvedObjectIsNotFetchedAgain()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState book = Drop(db, world, Shelf, owner: Keeper);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.False(ObjectCarryRules.IsLoose(db, book));
        Assert.Equal((Shelf.Y * world.Width) + Shelf.X, book.TileIndex);
        Assert.NotEqual(CreatureActivity.CarryingObject, imp.Activity);
    }

    [Fact]
    public void AnImpLeavesALooseThingWhereItIsWhenItsRoomDoesNotExist()
    {
        // The destination is settled before the walk, so an imp with nowhere to
        // put a book does not cross the dungeon to stand over one.
        GameDatabase db = Db;
        WorldState world = World(db);
        ObjectState book = Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.Equal((Loose.Y * world.Width) + Loose.X, book.TileIndex);
        Assert.NotEqual(CreatureActivity.FetchingObject, imp.Activity);
        Assert.NotEqual(CreatureActivity.CarryingObject, imp.Activity);
    }

    [Fact]
    public void AnImpLeavesALooseThingWhereItIsWhenTheLibraryIsFull()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf);
        Drop(db, world, Shelf, owner: Keeper);

        ObjectState book = Drop(db, world, Loose);
        world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.Equal((Loose.Y * world.Width) + Loose.X, book.TileIndex);
        Assert.Equal(PlayerIds.Unowned, book.OwnerPlayerId);
    }

    [Fact]
    public void ALooseThingOnSomebodyElsesFloorIsLeftAloneUntilTheFloorIsTaken()
    {
        // DK2's test is "own floor or water": a keeper's imps tidy the keeper's
        // dungeon. **The rule is about the ground, not the object**, which is
        // what the second half of this shows - the imp claims the tile out from
        // under the book and then comes back for it.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);

        world.Tile(Loose.X, Loose.Y).OwnerPlayerId = Rival;
        ObjectState book = Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        sim.Tick();

        // Kind 8 outranks claiming, so an imp that took the claim job on that
        // tile is an imp that was not offered the book standing on it.
        Assert.NotEqual(CreatureActivity.FetchingObject, imp.Activity);
        Assert.Equal((Loose.Y * world.Width) + Loose.X, book.TileIndex);

        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.Equal(Keeper, world.Tile(Loose.X, Loose.Y).OwnerPlayerId);
        Assert.Equal(Keeper, book.OwnerPlayerId);
    }

    // ---- the ordering, which is the interesting part -------------------------

    [Fact]
    public void PuttingABookAwayComesBeforeDiggingAWallTheKeeperMarked()
    {
        // Kind 8 is SECOND in DK2's Tier A table - `14, 8, 9, 1, 3, 13, 12` -
        // so it outranks marked excavation and claiming both. Nobody would
        // guess that; the table says it plainly, and this is the assertion that
        // would fail if somebody quietly moved it down the list.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        // A wall marked right next to the imp, which is as favourable as
        // marked excavation ever gets.
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        sim.Tick();

        Assert.Equal(CreatureActivity.FetchingObject, imp.Activity);

        SimEvent first = sink.Events.First(e => e.Kind is "object_claimed" or "job_taken");
        Assert.Equal("object_claimed", first.Kind);
    }

    // ---- one load at a time --------------------------------------------------

    [Fact]
    public void AnImpCarryingABookIsNotOfferedAnother()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState first = Drop(db, world, Loose);
        ObjectState second = Drop(db, world, new TilePos(1, 2));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 3 * Second; i++)
        {
            sim.Tick();
            if (imp.CarriedObjectId >= 0) break;
        }

        Assert.True(imp.CarriedObjectId >= 0);
        int carried = imp.CarriedObjectId;

        // The other one is still on the floor and in nobody's hands.
        ObjectState other = carried == first.Id ? second : first;
        Assert.Equal(-1, other.CarriedByCreatureId);
    }

    [Fact]
    public void ACarriedBookFollowsTheImpTileByTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState book = Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++)
        {
            sim.Tick();
            if (imp.CarriedObjectId != book.Id) continue;

            // While it is held, the book is wherever the imp is - never a tile
            // behind, because the follow runs after the mover.
            Assert.Equal((imp.Tile.Y * world.Width) + imp.Tile.X, book.TileIndex);
        }
    }

    [Fact]
    public void AnImpPickedUpByTheKeeperLeavesTheBookOnTheFloor()
    {
        // An imp in the hand is out of the dungeon, and a book that went up
        // there with it would be somewhere no rule could reach.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState book = Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 20 * Second; i++)
        {
            sim.Tick();
            if (imp.CarriedObjectId == book.Id) break;
        }
        Assert.Equal(book.Id, imp.CarriedObjectId);

        TilePos where = imp.Tile;
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        Assert.True(imp.InHand);
        Assert.Equal(-1, imp.CarriedObjectId);
        Assert.Equal(-1, book.CarriedByCreatureId);
        Assert.Equal((where.Y * world.Width) + where.X, book.TileIndex);

        // And it is still loose, so the next imp along takes it to the same
        // library.
        Assert.True(ObjectCarryRules.IsLoose(db, book));
        Assert.Contains(sink.Events,
            e => e.Kind == "object_dropped" && e.Detail.Contains("carrier_picked_up"));
    }

    [Fact]
    public void ACarriedBookSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Library, Shelf, Shelf2);
        ObjectState book = Drop(db, world, Loose);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Middle);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++)
        {
            sim.Tick();
            if (imp.CarriedObjectId == book.Id) break;
        }
        Assert.Equal(book.Id, imp.CarriedObjectId);

        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, db, SimulationSystems.Default());

        Assert.Equal(sim.StateHash(), restored.StateHash());

        CreatureState? back = restored.World.CreatureById(imp.Id);
        Assert.NotNull(back);
        Assert.Equal(book.Id, back!.CarriedObjectId);

        ObjectState? carried = restored.World.ObjectById(book.Id);
        Assert.NotNull(carried);
        Assert.Equal(imp.Id, carried!.CarriedByCreatureId);
    }
}
