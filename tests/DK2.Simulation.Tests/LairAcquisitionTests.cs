// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// How a creature gets a bed: a room chosen by distance, a walk, and a tile
/// chosen on arrival (ADR-0060; <c>docs/research/lair-acquisition.md</c> §4,
/// §5). Every test here is on the fixture map — a 3×3 claimed room at (1..3,
/// 1..3) in rock — with corridors and lairs painted onto the rock as needed,
/// so that reach is a real thing a test can take away.
///
/// <para>A Goblin walks 0.7 tiles a second, so a tile is about 29 ticks and the
/// five-tile corridor most of these use is about seven seconds.</para>
/// </summary>
public class LairAcquisitionTests
{
    private const byte Keeper = 3;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    /// <summary>
    /// A creature of the other side, for the alignment preference. Synthetic:
    /// a Goblin with <c>IsEvil</c> swapped for <c>IsGood</c>, because the
    /// fixture transcribes no hero and one test needs one. Not a claim about
    /// any shipped record.
    /// </summary>
    private const byte HeroId = 40;

    private static CreatureDef Hero() => Dk2TerrainFixture.Goblin() with
    {
        Name = "Test Hero",
        CreatureId = HeroId,
        Flags = CreatureFlags.IsGood | CreatureFlags.CanWalkOnWater,
    };

    private static GameDatabase Db => Dk2TerrainFixture.Database(Hero());

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Walking(
        GameDatabase db, WorldState world, ListEventSink? sink = null, ulong seed = 42)
    {
        var sim = new GameSimulation(db, world, seed, [
            new CreatureMovementSystem(), new CreatureSleepSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void Lair(GameDatabase db, WorldState world, int x, int y)
    {
        ref TileState tile = ref world.Tile(x, y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Lair), Keeper);
    }

    private static void Lair(GameDatabase db, WorldState world, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Lair(db, world, x, y);
    }

    /// <summary>Claimed floor along row <paramref name="y"/>, leaving lair tiles alone.</summary>
    private static void Corridor(WorldState world, int y, int x0, int x1)
    {
        for (int x = x0; x <= x1; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            if (tile.TerrainId == Dk2TerrainFixture.LairTerrain) continue;
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }
    }

    private static int Index(WorldState world, int x, int y) => (y * world.Width) + x;

    private static int IndexOf(IReadOnlyList<SimEvent> events, Func<SimEvent, bool> match)
    {
        for (int i = 0; i < events.Count; i++) if (match(events[i])) return i;
        return -1;
    }

    private static int LastIndexOf(IReadOnlyList<SimEvent> events, Func<SimEvent, bool> match)
    {
        for (int i = events.Count - 1; i >= 0; i--) if (match(events[i])) return i;
        return -1;
    }

    private static TilePos BedOf(WorldState world, GameDatabase db, CreatureState creature)
    {
        int index = LairRules.BedTileIndex(world, db, creature);
        Assert.True(index >= 0, "no bed");
        return new TilePos(index % world.Width, index / world.Width);
    }

    /// <summary>The standard layout: a corridor east from the room to a 3×3 lair at (6..8, 1..3).</summary>
    private static void CorridorToA3x3Lair(GameDatabase db, WorldState world)
    {
        Corridor(world, 2, 4, 5);
        Lair(db, world, 6, 1, 8, 3);
    }

    // ---- no remote acquisition --------------------------------------------

    [Fact]
    public void ABedlessIdleCreatureWalksToTheLairAndBedsDownWhereItArrives()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();
        CorridorToA3x3Lair(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Walking(db, world, events);

        // The first tick decides and sets off. Nothing is claimed from here.
        sim.Run(1);
        Assert.Equal(CreatureActivity.SeekingLair, goblin.Activity);
        Assert.False(LairRules.HasBed(world, db, goblin));
        Assert.Empty(world.Objects);
        Assert.NotEmpty(goblin.Path);

        sim.Run(Second * 15);

        // It aimed at the room's centre (Q157, ours) and, finding that tile
        // free, put its bed down exactly there.
        Assert.Equal(new TilePos(7, 2), BedOf(world, db, goblin));
        Assert.Equal(new TilePos(7, 2), goblin.Tile);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);

        SimEvent sought = Assert.Single(events.Events, e => e.Kind == "lair_sought");
        Assert.Contains("to=(7,2)", sought.Detail);
        Assert.Contains("preferred", sought.Detail);
        Assert.Single(events.Events, e => e.Kind == "bed_claimed");
    }

    [Fact]
    public void ALairItCannotWalkToIsNotALairItCanUse()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        // A lair three tiles into solid rock, with no way through.
        Lair(db, world, 7, 2);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(Second * 5);

        Assert.False(LairRules.HasBed(world, db, goblin));
        Assert.Empty(world.Objects);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Contains(events.Events, e =>
            e.Kind == "lair_attempt_failed" && e.Detail.Contains("reason=lair_unreachable"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "lair_sought");
    }

    [Fact]
    public void AFailedAttemptIsRetriedAtEveryIdleDecisionAndNotEveryTick()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        // No lair at all.
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(120);

        // Tick 0, then every IdleRedecisionTicks: 0, 12, 24, ... 108.
        Assert.Equal(120 / LairRules.IdleRedecisionTicks,
            events.Events.Count(e => e.Kind == "lair_attempt_failed"));
        Assert.All(events.Events.Where(e => e.Kind == "lair_attempt_failed"),
            e => Assert.Contains("reason=no_lair", e.Detail));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
    }

    // ---- which room ---------------------------------------------------------

    [Fact]
    public void TheNearestReachableLairWithAFreeSlotIsChosenAndAFullOneIsPassedOver()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        // Two one-tile lairs down one corridor. The nearer is full.
        Corridor(world, 2, 4, 8);
        Lair(db, world, 4, 2);
        Lair(db, world, 8, 2);

        CreatureState lodger = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 2));
        LairRules.PlaceBed(world, db, lodger, Index(world, 4, 2));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(Second * 15);

        Assert.Equal(new TilePos(8, 2), BedOf(world, db, goblin));

        SimEvent sought = Assert.Single(events.Events, e => e.Kind == "lair_sought");
        Assert.Contains("to=(8,2)", sought.Detail);
        Assert.Contains("candidates=1", sought.Detail);
    }

    [Fact]
    public void ALairHoldingABedOfTheOtherSideIsPassedOverForAFartherOne()
    {
        // Q151. The near lair has a free tile and a Goblin's bed in it; a hero
        // walks past it to the far one. A Goblin in the same dungeon does not.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        Corridor(world, 2, 4, 8);
        Lair(db, world, 4, 1, 4, 2);
        Lair(db, world, 8, 2);

        CreatureState lodger = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 1));
        LairRules.PlaceBed(world, db, lodger, Index(world, 4, 1));

        CreatureState hero = world.SpawnCreature(db, HeroId, Keeper, new TilePos(2, 2));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 3));
        Walking(db, world, events).Run(Second * 15);

        Assert.Equal(new TilePos(8, 2), BedOf(world, db, hero));
        Assert.Equal(new TilePos(4, 2), BedOf(world, db, goblin));

        SimEvent heroSought = Assert.Single(events.Events,
            e => e.Kind == "lair_sought" && e.Detail.Contains($"creature={hero.Id} "));
        Assert.Contains("to=(8,2)", heroSought.Detail);
        Assert.Contains("preferred", heroSought.Detail);
        Assert.Contains("candidates=2", heroSought.Detail);
    }

    [Fact]
    public void WithNoBetterLairTheOtherSidesBedIsToleratedRatherThanGoingWithout()
    {
        // The fallback half of Q151: the preference is a preference.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        Corridor(world, 2, 4, 4);
        Lair(db, world, 4, 1, 4, 2);

        CreatureState lodger = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 1));
        LairRules.PlaceBed(world, db, lodger, Index(world, 4, 1));

        CreatureState hero = world.SpawnCreature(db, HeroId, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(Second * 10);

        Assert.Equal(new TilePos(4, 2), BedOf(world, db, hero));
        SimEvent sought = Assert.Single(events.Events, e => e.Kind == "lair_sought");
        Assert.Contains("fallback", sought.Detail);
    }

    // ---- which tile: decided on arrival ------------------------------------

    [Fact]
    public void OccupancyIsDecidedOnArrivalAndATileTakenMidWalkSendsItNextDoor()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();
        CorridorToA3x3Lair(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Walking(db, world, events);
        sim.Run(1);
        Assert.Equal(CreatureActivity.SeekingLair, goblin.Activity);

        // While it walks, somebody else's bed lands on the very tile it is
        // heading for.
        CreatureState lodger = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(8, 3));
        LairRules.PlaceBed(world, db, lodger, Index(world, 7, 2));

        sim.Run(Second * 15);

        // Underfoot failed; the first neighbour in the original's order is
        // (−1,−1), the north-west corner, and it was free.
        Assert.Equal(new TilePos(6, 1), BedOf(world, db, goblin));
        SimEvent found = Assert.Single(events.Events, e => e.Kind == "lair_tile_found");
        Assert.Contains("taken=(7,2)", found.Detail);
        Assert.Contains("to=(6,1)", found.Detail);
        Assert.Contains("how=neighbour", found.Detail);
        Assert.Single(events.Events, e => e.Kind == "lair_sought");
    }

    [Fact]
    public void TheNeighbourRingIsWalkedInTheOriginalsOrder()
    {
        // (−1,−1), (−1,0), (−1,+1), (0,+1), (+1,+1), (+1,0), (+1,−1), (0,−1):
        // from the north-west corner down the west side, along the south, up
        // the east, ending due north. Read from the binary's own offset table.
        GameDatabase db = Db;
        WorldState world = World(db);
        Lair(db, world, 6, 1, 8, 3);
        RoomInstance room = RoomIndex.RoomAt(world, db, Index(world, 7, 2))!;
        var rng = new SimRandom(1);

        TilePos[] expected =
        [
            new(6, 1), new(6, 2), new(6, 3), new(7, 3),
            new(8, 3), new(8, 2), new(8, 1), new(7, 1),
        ];

        // Take the centre, then each tile as it is found, and watch the order
        // the ring yields them in.
        world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, Index(world, 7, 2));
        foreach (TilePos next in expected)
        {
            Assert.True(LairRules.TryFindFreeTile(
                world, db, room, new TilePos(7, 2), Keeper, rng,
                out int found, out LairRules.TileFound how));
            Assert.Equal(LairRules.TileFound.Neighbour, how);
            Assert.Equal(next, new TilePos(found % world.Width, found / world.Width));
            world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, found);
        }

        // Nine beds on nine tiles: nothing left, and the walk says so.
        Assert.False(LairRules.TryFindFreeTile(
            world, db, room, new TilePos(7, 2), Keeper, rng, out int none, out LairRules.TileFound how2));
        Assert.Equal(-1, none);
        Assert.Equal(LairRules.TileFound.None, how2);
    }

    [Fact]
    public void WhenTheRingIsFullTheRoomWalkStartsAtARandomTileAndTheSameSeedGivesTheSameTile()
    {
        // A 1×7 lair with the middle three taken: nothing underfoot, nothing
        // next door, so the search walks the room from a random start.
        GameDatabase db = Db;
        WorldState world = World(db);
        Lair(db, world, 4, 2, 10, 2);
        RoomInstance room = RoomIndex.RoomAt(world, db, Index(world, 7, 2))!;
        foreach (int x in new[] { 6, 7, 8 })
            world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, Index(world, x, 2));

        Assert.True(LairRules.TryFindFreeTile(
            world, db, room, new TilePos(7, 2), Keeper, new SimRandom(42),
            out int first, out LairRules.TileFound how));
        Assert.True(LairRules.TryFindFreeTile(
            world, db, room, new TilePos(7, 2), Keeper, new SimRandom(42),
            out int again, out _));

        Assert.Equal(LairRules.TileFound.Walk, how);
        Assert.Equal(first, again);
        Assert.Contains(first, new[] { 4, 5, 9, 10 }.Select(x => Index(world, x, 2)));
    }

    [Fact]
    public void TwoCreaturesMayHeadForTheSameRoomAndBothGetBedsOnDifferentTiles()
    {
        // No reservation. Both aim at (7,2); the second to arrive finds it
        // taken and looks next door.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();
        CorridorToA3x3Lair(db, world);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 2));
        Walking(db, world, events).Run(Second * 20);

        Assert.Equal(new TilePos(7, 2), BedOf(world, db, first));
        Assert.Equal(new TilePos(6, 1), BedOf(world, db, second));
        Assert.Equal(2, events.Events.Count(e => e.Kind == "lair_sought"));
        Assert.Single(events.Events, e => e.Kind == "lair_tile_found");

        // Two beds, two owners, no duplicates and nothing dangling.
        Assert.Equal(2, world.Objects.Count);
        foreach (ObjectState bed in world.Objects)
        {
            Assert.True(LairRules.IsBed(db, bed));
            Assert.NotNull(world.CreatureById(bed.CreatureId));
        }
        Assert.Equal(1, world.Objects.Count(o => o.CreatureId == first.Id));
        Assert.Equal(1, world.Objects.Count(o => o.CreatureId == second.Id));
    }

    // ---- failing on arrival, and what comes after ---------------------------

    [Fact]
    public void ARoomFoundFullOnArrivalFallsThroughAndTheFreedTileIsTakenAtALaterDecision()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        Corridor(world, 2, 4, 6);
        Lair(db, world, 7, 2);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Walking(db, world, events);
        sim.Run(1);
        Assert.Equal(CreatureActivity.SeekingLair, goblin.Activity);

        // The only tile is taken while it walks.
        CreatureState lodger = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(7, 2));
        LairRules.PlaceBed(world, db, lodger, Index(world, 7, 2));

        sim.Run(Second * 15);

        // Arrived, found nothing, gave up — idle, not stuck in the state and
        // not spinning on it.
        Assert.False(LairRules.HasBed(world, db, goblin));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Contains(events.Events, e =>
            e.Kind == "lair_attempt_failed" && e.Detail.Contains("reason=lair_full"));
        int attemptsSoFar = events.Events.Count(e => e.Kind == "lair_attempt_failed");
        Assert.InRange(attemptsSoFar, 2, 30);

        // The lodger goes; the tile is free again; the next idle decision
        // takes it.
        LairRules.ReleaseBed(world, db, lodger);
        world.RemoveCreature(lodger.Id);
        sim.Run(Second * 5);

        Assert.Equal(new TilePos(7, 2), BedOf(world, db, goblin));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
    }

    [Fact]
    public void SellingTheDestinationMidWalkEndsTheAttemptCleanly()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        Corridor(world, 2, 4, 6);
        Lair(db, world, 7, 2);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Walking(db, world, events);
        sim.Run(1);
        Assert.Equal(CreatureActivity.SeekingLair, goblin.Activity);

        // The lair is sold back to floor while the creature is on its way.
        RoomRules.Unmake(db, ref world.Tile(7, 2));
        sim.Run(Second * 15);

        Assert.False(LairRules.HasBed(world, db, goblin));
        Assert.Empty(world.Objects);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Contains(events.Events, e =>
            e.Kind == "lair_attempt_failed" && e.Detail.Contains("reason=not_in_lair"));
        Assert.Contains(events.Events, e =>
            e.Kind == "lair_attempt_failed" && e.Detail.Contains("reason=no_lair"));
    }

    [Fact]
    public void ABedLostIsReplacedByTheSameJourneyToASurvivingTile()
    {
        // OWNER (2026-09-04): a creature whose bed tile is sold "needs to find
        // a new free lair tile to place its bed". Same journey, same test.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        Lair(db, world, 6, 2, 7, 2);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        GameSimulation sim = Walking(db, world, events);
        sim.Run(2);
        Assert.Equal(new TilePos(6, 2), BedOf(world, db, goblin));

        RoomRules.Unmake(db, ref world.Tile(6, 2));
        sim.Run(Second * 5);

        Assert.Equal(new TilePos(7, 2), BedOf(world, db, goblin));
        Assert.Single(world.Objects);

        int lost = IndexOf(events.Events, e => e.Kind == "bed_lost");
        int sought = LastIndexOf(events.Events, e => e.Kind == "lair_sought");
        int claimed = LastIndexOf(events.Events, e => e.Kind == "bed_claimed");
        Assert.True(lost >= 0 && lost < sought && sought < claimed,
            $"order was lost={lost} sought={sought} claimed={claimed}");
    }

    // ---- after the bed is down ----------------------------------------------

    [Fact]
    public void AHealthyCreatureIsIdleOnceItsBedIsDownAndDoesNotSleep()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();
        CorridorToA3x3Lair(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(Second * 30);

        Assert.True(LairRules.HasBed(world, db, goblin));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.DoesNotContain(events.Events, e => e.Kind == "fell_asleep");
        Assert.DoesNotContain(events.Events, e => e.Kind == "going_to_sleep");
    }

    [Fact]
    public void AWoundedCreatureSleepsTheMomentItsBedIsDown()
    {
        // CreatureSleepsWhenBelowPercentHealth (50) is the one thing consulted
        // after the bed is placed; tiredness has its own way in.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();
        CorridorToA3x3Lair(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 700;    // under 750
        GameSimulation sim = Walking(db, world, events);

        int waited = 0;
        while (!LairRules.HasBed(world, db, goblin) && waited++ < Second * 20) sim.Run(1);

        Assert.True(LairRules.HasBed(world, db, goblin));
        Assert.Equal(CreatureActivity.Sleeping, goblin.Activity);
        Assert.Equal(0, goblin.SleepTicks);

        int claimed = IndexOf(events.Events, e => e.Kind == "bed_claimed");
        int asleep = IndexOf(events.Events, e => e.Kind == "fell_asleep");
        Assert.True(claimed >= 0 && asleep == claimed + 1, "slept on the tick the bed went down");
        Assert.Equal(events.Events[claimed].Tick, events.Events[asleep].Tick);
        Assert.DoesNotContain(events.Events, e => e.Kind == "going_to_sleep");
    }

    // ---- the gate -----------------------------------------------------------

    [Fact]
    public void ACreatureWithABedIdButNoSleepNeverGoesForABed()
    {
        // Q104: the Dwarf and the Stone Knight carry the hero bed with a
        // TimeSleep of 0. The gate has two halves and this is the second.
        CreatureDef sleepless = Dk2TerrainFixture.Goblin() with
        {
            Name = "Sleepless", CreatureId = 41, TimeSleep = 0, TimeAwake = 32767,
        };
        Assert.NotEqual(0, sleepless.LairObjectId);
        Assert.False(LairRules.NeedsBed(sleepless));
        Assert.True(LairRules.NeedsBed(Dk2TerrainFixture.Goblin()));

        GameDatabase db = Dk2TerrainFixture.Database(sleepless);
        WorldState world = World(db);
        var events = new ListEventSink();
        Lair(db, world, 2, 2);

        // Standing on a free lair tile, and it still wants nothing.
        world.SpawnCreature(db, 41, Keeper, new TilePos(2, 2));
        Walking(db, world, events).Run(Second * 5);

        Assert.Empty(world.Objects);
        Assert.DoesNotContain(events.Events, e => e.System == "CreatureSleep");
    }

    // ---- determinism ---------------------------------------------------------

    [Fact]
    public void ASnapshotTakenMidJourneyContinuesToTheSameBedAndTheSameHash()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CorridorToA3x3Lair(db, world);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 2));
        GameSimulation live = Walking(db, world);

        live.Run(30);
        Assert.Equal(CreatureActivity.SeekingLair, first.Activity);
        Assert.Empty(world.Objects);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(live), db, [
            new CreatureMovementSystem(), new CreatureSleepSystem(),
        ]);

        live.Run(Second * 20);
        restored.Run(Second * 20);

        Assert.Equal(2, live.World.Objects.Count);
        Assert.Equal(live.StateHash(), restored.StateHash());
        foreach (CreatureState creature in live.World.Creatures)
        {
            CreatureState twin = restored.World.CreatureById(creature.Id)!;
            Assert.Equal(
                LairRules.BedTileIndex(live.World, db, creature),
                LairRules.BedTileIndex(restored.World, db, twin));
        }
    }

    [Fact]
    public void TheSameSeedPlacesTheSameBeds()
    {
        static ulong Run(ulong seed)
        {
            GameDatabase db = Db;
            WorldState world = World(db);
            CorridorToA3x3Lair(db, world);
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 2));
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 2));
            GameSimulation sim = Walking(db, world, seed: seed);
            sim.Run(Second * 25);
            Assert.Equal(3, world.Objects.Count);
            return sim.StateHash();
        }

        Assert.Equal(Run(42), Run(42));
    }
}
