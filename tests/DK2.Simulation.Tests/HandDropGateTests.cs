// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's drop gate, <c>0x0040D8F0</c> with the map tests <c>0x00450910</c> and
/// <c>0x00450BE0</c> (placement-cursor.md, "The drop cage's colour"; ADR-0526), as
/// <see cref="HandRules.CanDrop"/> mirrors it, and the drop that obeys it.</summary>
public class HandDropGateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;
    private const byte GoblinId = 7;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static void Set(WorldState world, int x0, int y0, int x1, int y1, byte terrain, byte owner)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = terrain;
                tile.OwnerPlayerId = owner;
            }
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0, int x1, int y1, byte owner = Keeper)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), owner);
    }

    private static bool CanDrop(WorldState world, GameDatabase db, CreatureState creature, int x, int y) =>
        HandRules.CanDrop(world, db, Keeper, HandItem.Of(creature), new TilePos(x, y));

    private static bool CanDrop(WorldState world, GameDatabase db, ObjectState thing, int x, int y) =>
        HandRules.CanDrop(world, db, Keeper, HandItem.Of(thing), new TilePos(x, y));

    [Fact]
    public void AnAllysGroundTakesACreature_ARivalsDoesNot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 5, 5, 6, 5, Dk2TerrainFixture.ClaimedPath, Rival);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, goblin, 5, 5));
        world.SetAllied(Keeper, Rival, true);
        Assert.True(CanDrop(world, db, goblin, 5, 5));
        Assert.True(CanDrop(world, db, goblin, 2, 2));
    }

    [Fact]
    public void AWallIsRefusedEvenWhenTheKeeperOwnsIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, Dk2TerrainFixture.ReinforcedWall, Keeper);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, world.TileIndex(new TilePos(2, 2)));

        Assert.False(CanDrop(world, db, imp, 4, 2));
        Assert.False(CanDrop(world, db, chicken, 4, 2));
    }

    [Fact]
    public void AClosedDoorTileIsRefused_AnOpenOneIsFloor()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 6, 2, Dk2TerrainFixture.ClaimedPath, Keeper);
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = 5, Y = 2, DoorId = 1, PlayerId = Keeper, Flags = 0, Unknown_0011 = new byte[3],
        }]);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, goblin, 5, 2));
        Assert.True(CanDrop(world, db, goblin, 4, 2));

        world.DoorAt(5, 2)!.Open = true;
        Assert.True(CanDrop(world, db, goblin, 5, 2));
    }

    [Fact]
    public void AWorkerGoesOntoNeutralDryFloor_ANonWorkerDoesNot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Neutral);
        // Claimed floor the neutral player holds is neutral roomless dry ground too.
        Set(world, 5, 2, 5, 2, Dk2TerrainFixture.ClaimedPath, PlayerIds.Neutral);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.True(CanDrop(world, db, imp, 4, 2));
        Assert.True(CanDrop(world, db, imp, 5, 2));
        Assert.False(CanDrop(world, db, goblin, 4, 2));
        Assert.False(CanDrop(world, db, goblin, 5, 2));
    }

    [Fact]
    public void AWorkerIsRefusedWaterAndLava()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, Dk2TerrainFixture.Water, PlayerIds.Neutral);
        Set(world, 5, 2, 5, 2, Dk2TerrainFixture.Lava, PlayerIds.Neutral);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, imp, 4, 2));
        Assert.False(CanDrop(world, db, imp, 5, 2));
    }

    [Fact]
    public void ANonWorkerIsRefusedTheHeartsInnerThreeByThree_ButNotItsRing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 7, 3, 13, 9, Dk2TerrainFixture.ClaimedPath, Keeper);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        for (int y = 5; y <= 7; y++)
            for (int x = 9; x <= 11; x++)
            {
                Assert.False(CanDrop(world, db, goblin, x, y));
                // An Imp, evil and a worker, is refused only another keeper's inner 3x3.
                Assert.True(CanDrop(world, db, imp, x, y));
            }
        Assert.True(CanDrop(world, db, goblin, 8, 4));
        Assert.True(CanDrop(world, db, goblin, 12, 6));
    }

    [Fact]
    public void AChickenIsRefusedAPortalCentre_AndAGoblinARivalsOne()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 4, 9, 9, Dk2TerrainFixture.ClaimedPath, Keeper);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, world.TileIndex(new TilePos(2, 2)));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, chicken, 6, 6));
        Assert.True(CanDrop(world, db, chicken, 5, 5));
        // Our own Portal's centre takes a goblin: it is thrown out (DisposalRules).
        Assert.True(CanDrop(world, db, goblin, 6, 6));

        world.SetAllied(Keeper, Rival, true);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7, owner: Rival);
        Assert.False(CanDrop(world, db, goblin, 6, 6));
        Assert.True(CanDrop(world, db, goblin, 5, 6));
    }

    [Fact]
    public void ACallToArmsGoesOntoAnyOpenTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Neutral);
        Set(world, 5, 2, 5, 2, Dk2TerrainFixture.ClaimedPath, Rival);
        Set(world, 6, 2, 6, 2, Dk2TerrainFixture.Water, PlayerIds.Neutral);
        ObjectState flag = world.SpawnObject(db, CallToArmsRules.ObjectTypeId, Keeper, world.TileIndex(new TilePos(2, 2)));
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, world.TileIndex(new TilePos(2, 2)));

        Assert.True(CanDrop(world, db, flag, 4, 2));
        Assert.True(CanDrop(world, db, flag, 5, 2));
        Assert.True(CanDrop(world, db, flag, 6, 2));
        Assert.False(CanDrop(world, db, flag, 7, 2));
        Assert.False(CanDrop(world, db, chicken, 5, 2));
    }

    [Fact]
    public void AnUnbeatenToolIsRefusedTheTortureChamber_KnockedOutIsOurPrisonerOrCollapsedGuess()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 4, 10, 10, Dk2TerrainFixture.ClaimedPath, Keeper);
        Paint(db, world, RoomTypeId.TortureChamber, 5, 5, 7, 7);
        CreatureState tool = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        tool.IsTool = true;

        Assert.False(CanDrop(world, db, tool, 5, 5));
        Assert.True(CanDrop(world, db, tool, 9, 9));
        tool.IsTool = false;
        Assert.True(CanDrop(world, db, tool, 5, 5));
    }

    [Fact]
    public void AWorkerIsRefusedACombatPitFloorSlot_ButNotItsRim()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.CombatPit, 5, 3, 9, 7);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, imp, 7, 5));
        Assert.True(CanDrop(world, db, imp, 5, 3));
    }

    [Fact]
    public void ANonWorkerIsRefusedItsOwnCombatPitOnceEveryFloorSlotIsStoodOn()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.CombatPit, 5, 3, 9, 7);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Assert.True(CanDrop(world, db, goblin, 7, 5));

        for (int y = 4; y <= 6; y++)
            for (int x = 6; x <= 8; x++)
                world.SpawnCreature(db, GoblinId, Keeper, new TilePos(x, y));

        Assert.False(CanDrop(world, db, goblin, 7, 5));
        Assert.True(CanDrop(world, db, goblin, 5, 3));

        // A rival's pit floor is refused even with an alliance: the free-slot test needs the player's own.
        world.SetAllied(Keeper, Rival, true);
        Paint(db, world, RoomTypeId.CombatPit, 5, 3, 9, 7, owner: Rival);
        Assert.False(CanDrop(world, db, goblin, 7, 5));
    }

    [Fact]
    public void AnEvilWorkerIsRefusedAPrisonFloorSlot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Prison, 5, 3, 9, 7);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, imp, 7, 5));
        Assert.True(CanDrop(world, db, imp, 5, 3));
        Assert.True(CanDrop(world, db, goblin, 7, 5));
    }

    [Fact]
    public void AWorkerIsRefusedAnAllysHeartInnerThreeByThree()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SetAllied(Keeper, Rival, true);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8, owner: Rival);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, imp, 10, 6));
        Assert.False(CanDrop(world, db, imp, 9, 5));
        Assert.True(CanDrop(world, db, imp, 8, 4));
    }

    [Fact]
    public void AChickenIsRefusedTheHeartsInnerThreeByThree()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8);
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, world.TileIndex(new TilePos(2, 2)));

        Assert.False(CanDrop(world, db, chicken, 10, 6));
        Assert.False(CanDrop(world, db, chicken, 9, 7));
        Assert.True(CanDrop(world, db, chicken, 12, 8));
    }

    [Fact]
    public void AnUnbeatenToolGoesOntoHeroLairWhereTheMapTestRefused()
    {
        GameDatabase fixture = Db;
        TerrainDef lair = fixture.TerrainById(Dk2TerrainFixture.DirtPath) with { TerrainId = 35, Name = "Hero Lair" };
        var db = new GameDatabase(fixture.Terrain.Values.Append(lair), fixture.Rooms.Values,
            fixture.Creatures.Values, fixture.Variables, objects: fixture.Objects.Values);
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, 35, PlayerIds.Good);
        CreatureState tool = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.False(CanDrop(world, db, tool, 4, 2));
        tool.IsTool = true;
        Assert.True(CanDrop(world, db, tool, 4, 2));
    }

    [Fact]
    public void ARefusedDropStaysInTheHandAndTheCageIsRed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Set(world, 4, 2, 4, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Neutral);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        Assert.False(HandRules.DropLands(world, db, Keeper, HandItem.Of(goblin), new TilePos(4, 2)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(4, 2))));

        Assert.True(goblin.InHand);
        Assert.Contains(events.Events, e => e.Kind == "drop_refused" && e.Detail.Contains("not_allowed"));
    }
}
