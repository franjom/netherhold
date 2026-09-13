// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Objects standing in the world — the third list
/// <see cref="WorldState"/> has, after tiles and creatures. The first thing to use
/// it is the bed, which DK2 keeps on the object rather than on the creature:
/// <c>Lair.CreatureId</c>, a u16 at the first offset of the object's union
/// (docs/research/objects.md §7, Q130). So a bed is now something that can be
/// looked at — which of the twelve it is, whose it is, and whether it is still
/// there — and these tests are as much about that move as about the list.</summary>
public class ObjectsInWorldTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;
    private const byte GoblinId = 7;
    private const byte VampireId = Dk2TerrainFixture.VampireCreatureId;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static void Paint(
        GameDatabase db, WorldState world, RoomTypeId type, TilePos at, byte owner)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), owner);
    }

    // ---- the list itself ---------------------------------------------------

    [Fact]
    public void AWorldStartsWithNoObjectsInIt()
    {
        // Nothing places objects from a level file yet: the PLAYERS and THINGS
        // records are unparsed. So an empty list is the honest starting state,
        // and a test says so rather than leaving it to be assumed.
        Assert.Empty(World(Db).Objects);
    }

    [Fact]
    public void AnObjectIdIsNeverReusedAfterTheObjectIsRemoved()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        ObjectState first = world.SpawnObject(
            db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, 10);
        Assert.True(world.RemoveObject(first.Id));

        ObjectState second = world.SpawnObject(
            db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, 10);

        // The same rule creatures follow, for the same reason: an id that came
        // back would let a stale reference point at a different thing.
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void RemovingAnObjectThatIsNotThereSaysSo()
    {
        WorldState world = World(Db);
        Assert.False(world.RemoveObject(1));
    }

    [Fact]
    public void AnObjectTheDatabaseHasNeverHeardOfIsRefusedRatherThanPlaced()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Not fussiness. Every rule asks a type flag — is this a bed, is this
        // food — and a definition-less object would answer "no" to all of them,
        // which is a wrong answer that looks like a correct one.
        Assert.Throws<KeyNotFoundException>(() => world.SpawnObject(db, 200, Keeper, 10));
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void AnObjectOutsideTheMapIsRefused()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        Assert.Throws<ArgumentOutOfRangeException>(() => world.SpawnObject(
            db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, world.TileCount));
    }

    // ---- a bed is an object -----------------------------------------------

    [Fact]
    public void ClaimingABedPutsTheCreaturesOwnBedOnTheTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        ObjectState bed = Assert.Single(world.Objects);
        Assert.Equal(Dk2TerrainFixture.GoblinBedObjectId, bed.ObjectTypeId);
        Assert.Equal((at.Y * world.Width) + at.X, bed.TileIndex);
        Assert.Equal(Keeper, bed.OwnerPlayerId);

        // And the object is the one that knows whose bed it is.
        Assert.Equal(goblin.Id, bed.CreatureId);
    }

    [Fact]
    public void AVampireGetsItsCoffinAndNotAGoblinsBed()
    {
        // The reason the bed cannot be shipped with the room: twelve bed objects
        // serve 46 creatures, and which one a tile holds is decided by whoever
        // claims it. A Goblin's LairObjectId is 22 and a Vampire's is 26.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, at);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, vampire));

        Assert.Equal(
            Dk2TerrainFixture.VampireBedObjectId,
            Assert.Single(world.Objects).ObjectTypeId);
    }

    [Fact]
    public void ACreatureWithNoLairObjectGetsNoBedObject()
    {
        // An imp's LairObjectId is 0, so there is nothing to place — and the
        // claim has to fail before it tries, or it would ask the database for
        // object 0 and be refused with an exception instead of a false.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);

        Assert.False(LairRules.TryPlaceBedInAnyLair(world, db, imp));
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void TwoCreaturesGetTwoBedsOnTwoTiles()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, new TilePos(4, 4), Keeper);
        Paint(db, world, RoomTypeId.Lair, new TilePos(5, 4), Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 3));

        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, first));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, second));

        Assert.Equal(2, world.Objects.Count);
        Assert.NotEqual(world.Objects[0].TileIndex, world.Objects[1].TileIndex);
        Assert.Equal(first.Id, world.Objects[0].CreatureId);
        Assert.Equal(second.Id, world.Objects[1].CreatureId);
    }

    [Fact]
    public void ClaimingTwiceDoesNotPlaceASecondBed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, new TilePos(4, 4), Keeper);
        Paint(db, world, RoomTypeId.Lair, new TilePos(5, 4), Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        // Two free tiles and one creature: a claim that did not notice it
        // already owned a bed would furnish both and leak the first forever.
        Assert.Single(world.Objects);
    }

    // ---- and it goes away again -------------------------------------------

    [Fact]
    public void ADeadCreaturesBedIsDestroyedRatherThanLeftStanding()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        Assert.Single(world.Objects);

        goblin.Health = 0;
        new GameSimulation(db, world, 42, [
            new CreatureSleepSystem(), new CreatureHealthSystem(),
        ]).Run(Second * 61);

        // Dead, and now a corpse on the floor for a second sixty seconds
        // (ADR-0063). The creature is still in the world; its bed is not.
        Assert.Equal(CreatureActivity.Dead, goblin.Activity);

        // The old model could not get this wrong, because there was nothing to
        // leave behind: releasing a bed was clearing an integer. Now it is a
        // removal, and a forgotten one would be a bed standing on a lair tile
        // with a creature id that answers to nobody.
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void ADeserterTakesItsBedWithIt()
    {
        // A Deserter rather than a Goblin: the angry job is re-rolled now
        // (ADR-0061), and this test is about the bed, not about a coin toss.
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Deserter());
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(
            db, Dk2TerrainFixture.DeserterCreatureId, Keeper, at);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;

        new GameSimulation(db, world, 42, [new CreatureAngerSystem()]).Run(Second);

        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void ABedOnATileThatStoppedBeingALairIsDestroyed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        var sim = new GameSimulation(db, world, 42, [new CreatureSleepSystem()]);

        // Standing on the lair already: one tick to decide on it, one to test
        // the tile underfoot and put the bed down (ADR-0060).
        sim.Run(2);
        Assert.Single(world.Objects);

        RoomRules.Unmake(db, ref world.Tile(at.X, at.Y));
        sim.Run(1);

        Assert.Empty(world.Objects);
    }

    // ---- it is state, so it survives a save -------------------------------

    [Fact]
    public void ObjectsSurviveASnapshotRoundTrip()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        var before = new GameSimulation(db, world, 42, [new CreatureSleepSystem()]);
        GameSimulation after = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(before), db, [new CreatureSleepSystem()]);

        ObjectState bed = Assert.Single(after.World.Objects);
        Assert.Equal(Dk2TerrainFixture.GoblinBedObjectId, bed.ObjectTypeId);
        Assert.Equal(goblin.Id, bed.CreatureId);
        Assert.Equal((at.Y * world.Width) + at.X, bed.TileIndex);

        // And the creature on the other side still finds it, which is the whole
        // point of storing the relation once: a restore that dropped the object
        // would silently un-bed a sleeping creature.
        CreatureState restored = Assert.Single(after.World.Creatures);
        Assert.Equal(bed.TileIndex, LairRules.BedTileIndex(after.World, db, restored));

        // The counter comes back too, or the next bed reuses a live id.
        Assert.Equal(before.StateHash(), after.StateHash());
    }

    // ---- the flag, not the id ---------------------------------------------

    [Fact]
    public void ABedIsRecognisedByItsTypeFlagRatherThanByWhichBedItIs()
    {
        // DK2's own union dispatch tests the TypeLair bit rather than naming any
        // of the twelve bed objects, which is how one branch serves all twelve
        // (objects.md §7). Both fixture beds must therefore answer the same.
        GameDatabase db = Db;
        WorldState world = World(db);

        foreach (byte id in new[]
        {
            Dk2TerrainFixture.GoblinBedObjectId, Dk2TerrainFixture.VampireBedObjectId,
        })
        {
            ObjectState instance = world.SpawnObject(db, id, Keeper, 0);
            Assert.True(LairRules.IsBed(db, instance), $"object {id} should be a bed");
        }
    }
}
