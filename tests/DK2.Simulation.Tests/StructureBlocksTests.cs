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

    /// <summary>A Hero Portal carries the Portal's handler set byte for byte, and its tile pass
    /// <c>0x004DCD20</c> writes navigation byte 4 on its centre just as it does a Portal's.</summary>
    [Fact]
    public void AHeroPortalsCentreIsTheSameHoleAsAPortals()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.HeroPortal, 5, 5, 7, 7, owner: PlayerIds.Neutral);

        StructureBlocks blocks = StructureBlocks.For(world, db);

        int centre = (6 * world.Width) + 6;
        Assert.True(blocks.IsPortalCentre(centre));
        Assert.True(blocks.Blocks(centre));
        Assert.Equal(1, blocks.Count);
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

        var sim = new GameSimulation(db, world, 42, [new HandSystem(), new CreatureStatusSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));
        Assert.Equal(197, goblin.Dk2StateId);
        sim.Tick();

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
        // Put down, and stunned as any own non-worker is off a room that takes it over (ADR-0226).
        Assert.Contains(events.Events, e => e.Kind is "dropped" or "dropped_stunned");
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
    public void AnImpIsRefusedThePortalAndGoesThroughTheHeartInstead()
    {
        // CONFIRMED (owner, 2026-09-13): disposal has two tiles and they are
        // exclusive. The imp is refused the hole a goblin leaves through, and
        // leaves through the heart's platform instead, which is why this test
        // asserts both halves of the same drop.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 1, 1, 14, 10);
        Paint(db, world, RoomTypeId.Portal, 2, 2, 4, 4);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        imp.GoldHeld = 120;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(3, 3))));

        Assert.Contains(world.Creatures, c => c.Id == imp.Id);
        Assert.Contains(events.Events, e => e.Kind == "drop_refused");

        // Not the raised 3x3, only its middle: a platform tile beside the centre
        // takes the imp as a normal drop (owner's photos, 2026-10-23).
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(9, 6))));
        Assert.Contains(world.Creatures, c => c.Id == imp.Id);
        Assert.Equal(new TilePos(9, 6), imp.Tile);
        Assert.False(imp.InHand);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(10, 6))));

        Assert.DoesNotContain(world.Creatures, c => c.Id == imp.Id);
        SimEvent left = Assert.Single(events.Events, e => e.Kind == "creature_left");
        Assert.Contains("at=(10,6)", left.Detail);

        // The gold goes with it: CONFIRMED for the heart too, not assumed.
        Assert.Contains("gold_lost=120", left.Detail);
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void AGoblinIsRefusedTheHeartAltogether()
    {
        // CONFIRMED (owner, 2026-09-15): a screenshot of a held goblin over a
        // dungeon heart shows the refusal cursor. The two disposal tiles really
        // are exclusive, which ADR-0148 could only assume.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 1, 1, 14, 10);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(10, 6))));

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Contains(events.Events, e => e.Kind == "drop_refused");

        // The ring around the platform is ordinary heart floor, and takes it.
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(8, 4))));
        Assert.Equal(new TilePos(8, 4), goblin.Tile);
    }

    [Fact]
    public void AnImpIsRefusedARivalsHeartTheWayItIsRefusedARivalsPortal()
    {
        // Disposal never bypasses ownership, only walkability.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 1, 1, 14, 10);
        Paint(db, world, RoomTypeId.DungeonHeart, 8, 4, 12, 8, owner: Rival);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(10, 6))));

        Assert.Contains(world.Creatures, c => c.Id == imp.Id);
        Assert.Contains(events.Events, e => e.Kind == "drop_refused");
    }

    [Fact]
    public void APrisonerIsRefusedTheHoleRatherThanPostedHomeOrFreedOntoIt()
    {
        // DK2's drop gate refuses a creature the keeper does not own on a Portal
        // centre (0x0040D8F0, ADR-0526), so a captive never reaches the hole.
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 4, 4, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);

        Paint(db, world, RoomTypeId.Prison, 1, 1, 3, 3);
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, new TilePos(2, 2));
        PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, (2 * world.Width) + 2);
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));

        Assert.Contains(world.Creatures, c => c.Id == captive.Id);
        Assert.Contains(events.Events, e =>
            e.Kind == "drop_refused" && e.Detail.Contains("not_allowed"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_left");
        Assert.DoesNotContain(events.Events, e => e.Kind == "prisoner_freed");

        // Still a prisoner, and still in the hand - a refused drop changes
        // nothing at all.
        Assert.True(PrisonRules.IsImprisoned(captive));
    }

    // ---- the router's view (DK2's cache, captured 2026-09-17) ---------------

    /// <summary>DK2's navigation cache holds byte 4 on a portal's centre and the cost function
    /// crosses that boundary only for a mover whose state passed option bit 2: a creature
    /// leaving the dungeon walks in, anyone else routes round (navigation-mesh-and-search.md §4).</summary>
    [Fact]
    public void TheRouterEntersAPortalsHoleOnlyForAMoverWhoseWordHasBitTwo()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 3, 3, 9, 9);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 7, 7);
        var steering = new Navigation.WorldSteering(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 6));
        Assert.False(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.Loose));
        Assert.True(steering.TryRoute(goblin, new TilePos(9, 6), Navigation.MoveOptions.Loose));
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.Leaving));
    }

    /// <summary>The native cache retains floor; only targets inside the small core are moved.</summary>
    [Fact]
    public void NativeHeartFloorIsPreservedWhileTheRouterAvoidsOnlyItsCore()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 3, 3, 9, 9);
        Paint(db, world, RoomTypeId.DungeonHeart, 4, 4, 8, 8);

        Assert.False(Grid(db, world).IsWalkable(6, 6));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 6));
        var steering = new Navigation.WorldSteering(db, world);
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.Loose));
        Assert.Equal(DK2.Pathfinding.NavBytes.Floor, steering.NavByte(6, 6));
        Assert.Equal(new DK2.Simulation.Steering.Fixed16Vector((6 << 16) - (1 << 13),
            (6 << 16) + (1 << 15)), goblin.Body.PathTargetPos);
        Assert.True(steering.TryRoute(goblin, new TilePos(4, 6), Navigation.MoveOptions.Loose));
    }

    /// <summary>A running heart's centre tile holds three objects (Level 10 dump): the solid
    /// obstacle (100, width 3.0) DK2's enumerator forwards once per query with flag bits 0 and 1,
    /// and the visible heart (13) and arches (86), plain shoves. The native obstacles remain;
    /// the off-axis walk follows the obstacle ring.</summary>
    [Fact]
    public void TheHeartHasItsThreeObstaclesAndAnOffAxisWalkerStaysOnTheRim()
    {
        (WorldState world, Navigation.WorldSteering steering, CreatureState goblin, GameSimulation sim) =
            HeartWalk(new TilePos(2, 4), new TilePos(10, 7));

        var obstacles = new List<DK2.Simulation.Steering.SteeringObstacle>();
        steering.CollectObstacles(new DK2.Simulation.Steering.Fixed16Vector(6 << 16, 6 << 16), 1 << 16, obstacles);
        Assert.Equal(3, obstacles.Count);
        DK2.Simulation.Steering.SteeringObstacle heart = Assert.Single(obstacles, o => o.Solid);
        Assert.Equal(3 << 15, heart.Size);
        Assert.True(heart.Flag2);
        Assert.Contains(obstacles, o => !o.Solid && o.Size == 1 << 15);
        Assert.Contains(obstacles, o => !o.Solid && o.Size == (2457 << 4) >> 1);

        int closest = int.MaxValue;
        for (int tick = 0; tick < 400 && goblin.Tile != new TilePos(10, 7); tick++)
        {
            sim.Tick(InputFrame.Empty);
            closest = Math.Min(closest, DistanceToHeartCentre(goblin));
            Assert.False(goblin.Body.EscapeStuckMode);
        }
        Assert.Equal(new TilePos(10, 7), goblin.Tile);
        Assert.InRange(closest, 3 << 15, 3 << 16);
    }

    /// <summary>The native route and obstacle exemptions leave only the small core protected.</summary>
    [Fact]
    public void AWalkerAimedThroughAHeartArrivesWithoutEnteringItsCore()
    {
        (WorldState world, Navigation.WorldSteering steering, CreatureState goblin, GameSimulation sim) =
            HeartWalk(new TilePos(2, 6), new TilePos(10, 6));

        int closest = int.MaxValue;
        for (int tick = 0; tick < 400 && goblin.Tile != new TilePos(10, 6); tick++)
        {
            sim.Tick(InputFrame.Empty);
            closest = Math.Min(closest, DistanceToHeartCentre(goblin));
            Assert.True(DistanceToHeartCentre(goblin) >= 1 << 15);
        }
        Assert.Equal(new TilePos(10, 6), goblin.Tile);
        Assert.InRange(closest, 1 << 15, 3 << 16);
    }

    [Fact]
    public void DestroyingAHeartReopensItsPlatformAndRemovesItsObstacles()
    {
        var (world, steering, goblin, sim) = HeartWalk(new TilePos(2, 6), new TilePos(10, 6));
        Assert.False(Grid(Db, world).IsWalkable(6, 6));
        world.Player(Keeper).HeartRuined = true;
        steering.Refresh();
        Assert.True(Grid(Db, world).IsWalkable(6, 6));
        Assert.Empty(StructureBlocks.For(world, Db).HeartCentres);
        var obstacles = new List<DK2.Simulation.Steering.SteeringObstacle>();
        steering.CollectObstacles(new((6 << 16) + 0x8000, (6 << 16) + 0x8000), 1 << 16, obstacles);
        Assert.Empty(obstacles);
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.Loose));
        for (int tick = 0; tick < 400 && goblin.Tile != new TilePos(6, 6); tick++) sim.Tick(InputFrame.Empty);
        Assert.Equal(new TilePos(6, 6), goblin.Tile);
    }

    [Theory]
    [InlineData(-0.65, 0, -0.25, 0, false)]
    [InlineData(-0.55, 0, -0.45, 0, false)]
    [InlineData(-0.49, -0.2, -0.49, 0.2, false)]
    [InlineData(-0.6, 0, -0.6, 0.4, true)]
    [InlineData(0, 0, 0.4, 0, true)]
    [InlineData(0.1, 0, 0, 0, false)]
    public void ALoadedBodyInsideTheCoreMayEscapeWithoutMovingDeeper(
        double x0, double y0, double x1, double y1, bool allowed)
    {
        var (world, steering, _, _) = HeartWalk(new TilePos(2, 6), new TilePos(10, 6));
        var from = HeartRelativePoint(x0, y0);
        var to = HeartRelativePoint(x1, y1);
        Assert.Equal(allowed, steering.CanMove(from, to));
        world.Player(Keeper).HeartRuined = true;
        steering.Refresh();
        Assert.True(steering.CanMove(from, to));
    }

    [Fact]
    public void TheCoreGuardIncludesThePositionRoundingUsedWhenMovementCommits()
    {
        var (_, steering, _, _) = HeartWalk(new TilePos(2, 6), new TilePos(10, 6));
        var centre = HeartRelativePoint(0, 0);
        Assert.False(steering.CanMove(centre + new DK2.Simulation.Steering.Fixed16Vector(40000, 10000),
            centre + new DK2.Simulation.Steering.Fixed16Vector(23171, 23171)));
    }

    [Fact]
    public void ARouteToTheCoreFinishesAtASafeEdge_AndRuinsKeepTheOriginalTarget()
    {
        var (world, steering, goblin, sim) = HeartWalk(new TilePos(2, 6), new TilePos(10, 6));
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.ExactSpot));
        Assert.True((goblin.Body.PathTargetPos - HeartRelativePoint(0, 0)).Length() > 1 << 15);
        for (int tick = 0; tick < 400 && !goblin.Body.PathFinished; tick++) sim.Tick(InputFrame.Empty);
        Assert.True(goblin.Body.PathFinished);
        Assert.True(DistanceToHeartCentre(goblin) >= 1 << 15);
        world.Player(Keeper).HeartRuined = true;
        Assert.Equal(HeartRelativePoint(0, 0), steering.ConstrainTarget(goblin.Body.Pos, HeartRelativePoint(0, 0)));
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), Navigation.MoveOptions.ExactSpot));
        Assert.Equal(HeartRelativePoint(0, 0), goblin.Body.PathTargetPos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeitherASingleStepNorAnArrivalSnapCanEnterTheLiveCore(bool snap)
    {
        var (_, steering, goblin, _) = HeartWalk(new TilePos(2, 6), new TilePos(10, 6));
        var body = new DK2.Simulation.Steering.SteeringBody
        {
            Pos = HeartRelativePoint(snap ? -0.55 : -0.65, 0),
            Vel = new((int)(0.4 * 65536), 0),
            PathFlags = snap ? (byte)4 : (byte)0,
            PathTotalCount = 1,
            EscapeStuckMode = !snap,
        };
        body.Subpath[0] = HeartRelativePoint(-0.45, 0);
        var before = body.Pos;
        DK2.Simulation.Steering.SteeringIntegrator.Step(body, DK2.Simulation.Steering.Fixed16Vector.Zero,
            Navigation.WorldSteering.ProfileOf(Db.CreatureById(GoblinId), Keeper, Navigation.MoveOptions.Loose), steering);
        Assert.True(steering.CanMove(before, body.Pos));
        Assert.True((body.Pos - HeartRelativePoint(0, 0)).Length() >= 1 << 15);
    }

    private static DK2.Simulation.Steering.Fixed16Vector HeartRelativePoint(double x, double y) =>
        new((int)((6.5 + x) * 65536), (int)((6.5 + y) * 65536));

    private static int DistanceToHeartCentre(CreatureState goblin) =>
        (goblin.Body.Pos - new DK2.Simulation.Steering.Fixed16Vector((6 << 16) + 0x8000, (6 << 16) + 0x8000)).Length();

    /// <summary>A 9×7 claimed room holding a 5×5 heart centred on (6,6), and a goblin routed across it.</summary>
    private static (WorldState, Navigation.WorldSteering, CreatureState, GameSimulation) HeartWalk(TilePos from, TilePos to)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Claim(world, 2, 3, 10, 9);
        Paint(db, world, RoomTypeId.DungeonHeart, 4, 4, 8, 8);
        var steering = new Navigation.WorldSteering(db, world);
        steering.Refresh();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, from);
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        Assert.True(steering.TryRoute(goblin, to, Navigation.MoveOptions.Loose));
        return (world, steering, goblin, sim);
    }
}
