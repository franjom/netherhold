// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The hole in a portal and the heart on its platform — Q88, CONFIRMED by the
/// owner on 2026-09-07 and unbuilt until then.
///
/// <para>Two halves. The tile is not walkable, and <b>dropping a creature into
/// a portal's centre removes it from the dungeon</b> — a keeper power nothing
/// here had.</para>
/// </summary>
public sealed class StructureBlocksTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;
    private const byte GoblinId = 7;

    private static int Second => SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    /// <summary>Claimed floor, so there is somewhere to walk around a room.</summary>
    private static void Claim(WorldState world, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }
    }

    /// <summary>Paints a rectangular room of one type, owned by one keeper.</summary>
    private static void Paint(
        GameDatabase db, WorldState world, RoomTypeId type,
        int x0, int y0, int x1, int y1, byte owner = Keeper)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(db, ref tile, db.Room(type), owner);
            }
        }
    }

    private static WorldPathGrid Grid(GameDatabase db, WorldState world) =>
        new(db, world, db.CreatureById(GoblinId));

    // ---- the hole ---------------------------------------------------------

    [Fact]
    public void APortalsCentreIsNotWalkableAndItsEightRingTilesAre()
    {
        // OWNER (Q88): "no it is not walkable [...] portal's center is reserved
        // for creature's 'arriving' animation and for hand of evil to throw
        // creatures in". Eight of the nine tiles are floor; the middle one is a
        // hole, and a creature crossing a portal goes round it.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        WorldPathGrid grid = Grid(db, world);

        Assert.False(grid.IsWalkable(6, 6));

        int walkable = 0;
        for (int y = 5; y <= 7; y++)
        {
            for (int x = 5; x <= 7; x++)
            {
                if (grid.IsWalkable(x, y)) walkable++;
            }
        }
        Assert.Equal(8, walkable);
    }

    [Fact]
    public void ADungeonHeartsMiddleNineAreNotWalkableAndTheSixteenAroundThemAre()
    {
        // Nine tiles, not one: the middle nine are the raised platform the heart
        // stands on. The count comes from arithmetic with nothing to do with
        // walking — a keeper with no treasury tops out at 16000 gold against
        // MaxGoldPerDungeonHeartTile of 1000, so 16 of the 25 tiles hold gold and
        // the other 9 are structure (Q52, CONFIRMED off the owner's gold counter).
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.DungeonHeart, 4, 4, 8, 8);

        WorldPathGrid grid = Grid(db, world);

        int walkable = 0;
        int blocked = 0;
        for (int y = 4; y <= 8; y++)
        {
            for (int x = 4; x <= 8; x++)
            {
                if (grid.IsWalkable(x, y)) walkable++;
                else blocked++;
            }
        }

        Assert.Equal(16, walkable);
        Assert.Equal(9, blocked);

        // And it is the same predicate the gold rules use, so the two cannot
        // drift apart: 16 storable tiles and 16 walkable tiles is one fact.
        Assert.False(grid.IsWalkable(6, 6));
        Assert.True(grid.IsWalkable(4, 4));
    }

    [Fact]
    public void ARoomWithNoMiddleBlocksNothingAtAll()
    {
        // A portal smaller than 3x3 does not exist in DK2, and blocking the only
        // tile of a 1x1 would wall off the thing a deserter walks to. Same guard
        // RoomInstance.IsCentrePlatform makes for a half-destroyed heart: a room
        // with no middle to speak of has no structure standing in it.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 5, 5);

        Assert.True(Grid(db, world).IsWalkable(5, 5));
        Assert.Equal(0, StructureBlocks.For(world, db).Count);

        // Two wide is still not a portal.
        Paint(db, world, RoomTypeId.Portal, 5, 5, 6, 6);
        Assert.Equal(0, StructureBlocks.For(world, db).Count);
    }

    [Fact]
    public void AMapWithNeitherRoomComputesAnEmptyMask()
    {
        // The common case, and the reason the mask scans for the two terrain ids
        // before it does anything else.
        GameDatabase db = Db;
        Assert.Equal(0, StructureBlocks.For(World(db), db).Count);
    }

    // ---- arriving through it still works ----------------------------------

    [Fact]
    public void ACreatureThatArrivesOnTheHoleCanStillWalkOffIt()
    {
        // THE INTERACTION THAT COULD HAVE BROKEN THE PORTAL. A new creature is
        // spawned ON the centre (ADR-0042), which the owner's answer identifies
        // as the arriving animation - so the hole has to be a legal place to
        // stand and an illegal place to walk. It is, and by accident of good
        // design rather than a special case: GridPathfinder tests the
        // destination and every neighbour it expands but never the START.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 6));
        Assert.False(Grid(db, world).IsWalkable(6, 6));

        var pathfinder = new DK2.Pathfinding.GridPathfinder();
        var route = new List<TilePos>();
        bool routed = pathfinder.TryFindPath(
            Grid(db, world), goblin.Tile, new TilePos(9, 9), route);

        Assert.True(routed);
        Assert.NotEmpty(route);

        // And nothing can be routed the other way, onto the hole.
        Assert.False(pathfinder.TryFindPath(
            Grid(db, world), new TilePos(9, 9), new TilePos(6, 6), route));
    }

    [Fact]
    public void ADeserterStillReachesARealPortalAndLeavesThroughIt()
    {
        // The half of Q88 that had to keep working. A creature walking out goes
        // to the nearest tile of one of its keeper's portals (PortalRules
        // .IsExitTile), which is any of the eight - so blocking the hole does
        // not trap a deserter, and the first version of this change did trap one
        // because the test portal was a single tile.
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Deserter());
        WorldState world = World(db);
        Claim(world, 1, 1, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        CreatureState goblin = world.SpawnCreature(
            db, Dk2TerrainFixture.DeserterCreatureId, Keeper, new TilePos(1, 1));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;

        var sim = new GameSimulation(db, world, 42,
            [new CreatureMovementSystem(), new CreatureAngerSystem()]);
        sim.Run(Second * 60);

        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
    }

    // ---- thrown in --------------------------------------------------------

    [Fact]
    public void DroppingACreatureIntoTheHoleRemovesItFromTheDungeonWithItsGold()
    {
        // CONFIRMED (owner): the portal's centre is "for hand of evil to throw
        // creatures in, removing them from dungeon", and the gold goes with the
        // creature rather than spilling on the floor — Q137, in the owner's own
        // parenthesis: "a creature leaving (being thrown) through portal does not
        // drop its gold".
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 4));
        goblin.GoldHeld = 350;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));

        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);

        SimEvent left = Assert.Single(events.Events, e => e.Kind == "creature_left");
        Assert.Contains("via=thrown_in", left.Detail);
        Assert.Contains("gold_lost=350", left.Detail);

        // No gold pile anywhere: it went with the creature.
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void DroppingItOnOneOfTheEightRingTilesJustPutsItDown()
    {
        // The hole is one tile, not the room. A keeper who means to keep a
        // creature can put it down in its own portal.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 4));
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(5, 5))));

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Equal(new TilePos(5, 5), goblin.Tile);
        Assert.Contains(events.Events, e => e.Kind == "dropped");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_left");
    }

    [Fact]
    public void AKeeperCannotPostACreatureThroughSomebodyElsesPortal()
    {
        // Falls out of the ownership gate the hand already had (ADR-0077): a
        // creature goes down on its keeper's own ground, so a rival's portal is
        // not a place a drop can even land. Asserted rather than assumed,
        // because the removal is destructive and the gate is somewhere else.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7, owner: Rival);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 4));
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Contains(events.Events, e => e.Kind == "drop_refused");
    }

    [Fact]
    public void APrisonerIsRefusedTheHoleRatherThanPostedHomeOrFreedOntoIt()
    {
        // A deliberate narrowing, flagged in the code too. Whether DK2 lets a
        // keeper post an enemy captive home through its own portal is UNKNOWN;
        // ejecting it and freeing it into the dungeon both invent something, while
        // refusing the drop invents least, leaves the prisoner rules (ADR-0064)
        // untouched, and does not stand a freed prisoner on an unwalkable tile.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        Paint(db, world, RoomTypeId.Prison, 1, 1, 3, 3);
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, new TilePos(2, 2));
        PrisonRules.Imprison(world, db, captive, Keeper, (2 * world.Width) + 2);
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));

        Assert.Contains(world.Creatures, c => c.Id == captive.Id);
        Assert.Contains(events.Events, e =>
            e.Kind == "drop_refused" && e.Detail.Contains("not_walkable"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_left");
        Assert.DoesNotContain(events.Events, e => e.Kind == "prisoner_freed");

        // Still a prisoner, and still in the hand - a refused drop changes
        // nothing at all.
        Assert.True(PrisonRules.IsImprisoned(captive));
    }
}
