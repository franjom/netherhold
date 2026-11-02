// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Workshop handler's four effect timers and the room constructor that seeds them
/// (<c>0x004F4B20</c>, <c>0x004F4A60</c>; workshop-timers.md). DK2's draws are the point: every reload
/// and every count draws the world stream whether or not an effect follows.</summary>
public sealed class WorkshopTimerTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Ticks = SimulationTiming.OriginalTicksPerGameSecond;

    // The shipped Workshop's effects (Rooms.kwd 0x386, 0x388, 0x38A) and the Anvil's and Steel
    // Bucket's idle effects (Objects.kwd 0x358).
    private const ushort Steam = 50, Smoke = 51, LavaBubbling = 93, AnvilSparks = 42, BucketSpill = 69;
    private const byte Anvil = 15, Bucket = 16, WorkBench = 46;

    /// <summary>The generator as dk2-random.md section 1 states it, written out here so that the
    /// expectations do not lean on <see cref="Dk2Lcg"/>.</summary>
    private sealed class Oracle(uint seed)
    {
        public uint Seed { get; private set; } = seed;

        public int Next(int n)
        {
            Seed = unchecked((Seed * 0x0019660Du) + 0x3C6EF35Fu);
            return (int)(((Seed >> 13) | (Seed << 19)) % (uint)n);
        }
    }

    private static GameDatabase Db(ushort steam = Steam, ushort smoke = Smoke, ushort bubbling = LavaBubbling)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        TerrainDef rock = basis.TerrainById(Dk2TerrainFixture.Rock);
        return new GameDatabase(
            basis.Terrain.Values.Select(t => t.TerrainId == rock.TerrainId ? rock with { Flags = rock.Flags | TerrainFlags.AllowRoomWalls } : t),
            basis.Rooms.Values.Select(r => r.TypeId == RoomTypeId.Workshop
                ? r with { RoomEffect425Id = steam, RoomEffect427Id = smoke, RoomEffect429Id = bubbling } : r),
            basis.Creatures.Values, basis.Variables, basis.KeeperSpells.Values,
            objects: basis.Objects.Values.Select(o => o.ObjectId switch
            {
                Anvil => o with { IdleEffectId = AnvilSparks },
                Bucket => o with { IdleEffectId = BucketSpill },
                _ => o,
            }),
            shots: basis.Shots.Values);
    }

    private static WorldState World(GameDatabase db, int x0 = 3, int y0 = 3, int size = 5, bool furnish = true)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(24, 24), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        if (furnish) Dk2TerrainFixture.FurnishRooms(db, world);
        return world;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] systems) =>
        new(db, world, new SimRandom(new Dk2Lcg(1)), systems);

    private static GameSimulation TimerSim(GameDatabase db, WorldState world, out ListEventSink sink)
    {
        GameSimulation sim = Sim(db, world, new WorkshopTimerSystem());
        sink = new ListEventSink();
        sim.ObserveWith(sink);
        return sim;
    }

    private static uint SeedOf(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static RoomInstance Workshop(GameDatabase db, WorldState world, int tileIndex) =>
        RoomIndex.RoomAt(world, db, tileIndex)!;

    private static ref TileState Lowest(WorldState world, RoomInstance room) => ref world.TileAtIndex(room.TileIndices[0]);

    private static List<ViewEffectRequest> Requests(ListEventSink sink) =>
        [.. sink.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value)];

    private static int FloorSlots(WorldState world, RoomInstance room) => AutoFurnitureRules.FloorSlotCount(world, room);

    private static List<int> WallSlots(WorldState world, GameDatabase db, RoomInstance room) =>
        [.. SlabChain.Order(world, room).Where(t => AutoFurnitureRules.WallSide(world, db, room, t) >= 0)];

    private static List<ObjectState> Furniture(WorldState world, RoomInstance room) =>
        [.. world.Objects.Where(o => o.AutoPlaced && room.Contains(o.TileIndex)).OrderByDescending(o => o.Id)];

    [Fact]
    public void AZeroTimerReloadsSixteenSecondsDrawnPlusEightAndTheFourRunInDk2sOrderWithTheirSlotAndCountDraws()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        int floors = FloorSlots(world, room), walls = WallSlots(world, db, room).Count;
        int buckets = Furniture(world, room).Count(o => o.ObjectTypeId == Bucket);
        Assert.True(floors > 0 && walls > 0 && buckets > 0, $"the fixture room must offer a slot of each kind: floors {floors} walls {walls} buckets {buckets} objects {string.Join(",", Furniture(world, room).Select(o => o.ObjectTypeId))}");
        GameSimulation sim = TimerSim(db, world, out _);

        sim.Tick();

        // Steam (room+0x49) reloads, then draws its floor slot; lava spill (+0x55) reloads and counts the
        // Steel Buckets; smoke (+0x4D) reloads and draws its wall slot; lava bubbling (+0x51) reloads and
        // counts the buckets again. Source lines 0x39, F42, 0x5D, 0x6C, 0xA0, F42, 0xD0, 0xE3.
        var oracle = new Oracle(1);
        int steam = oracle.Next(16 * Ticks) + (8 * Ticks);
        oracle.Next(floors);
        int spill = oracle.Next(16 * Ticks) + (8 * Ticks);
        oracle.Next(buckets);
        int smoke = oracle.Next(16 * Ticks) + (8 * Ticks);
        oracle.Next(walls);
        int bubbling = oracle.Next(16 * Ticks) + (8 * Ticks);
        oracle.Next(buckets);

        TileState tile = Lowest(world, room);
        Assert.Equal((steam, spill, smoke, bubbling),
            (tile.WorkshopSteamTimer, tile.WorkshopLavaSpillTimer, tile.WorkshopSmokeTimer, tile.WorkshopLavaBubblingTimer));
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void ATimerAboveZeroCountsDownAndDrawsNothingAndOneAtZeroReloadsOnTheNextTurn()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        ref TileState tile = ref Lowest(world, room);
        (tile.WorkshopSteamTimer, tile.WorkshopLavaSpillTimer, tile.WorkshopSmokeTimer, tile.WorkshopLavaBubblingTimer) = (5, 6, 7, 1);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);

        sim.Tick();

        tile = ref Lowest(world, room);
        Assert.Equal((4, 5, 6, 0),
            (tile.WorkshopSteamTimer, tile.WorkshopLavaSpillTimer, tile.WorkshopSmokeTimer, tile.WorkshopLavaBubblingTimer));
        Assert.Equal(1u, SeedOf(sim));
        Assert.Empty(Requests(sink));

        sim.Tick();

        tile = ref Lowest(world, room);
        Assert.NotEqual(1u, SeedOf(sim));
        Assert.True(tile.WorkshopLavaBubblingTimer >= 8 * Ticks, "reloaded from the stream plus eight seconds");
        Assert.Equal((3, 4, 5), (tile.WorkshopSteamTimer, tile.WorkshopLavaSpillTimer, tile.WorkshopSmokeTimer));
    }

    [Fact]
    public void TheReloadsAndTheBucketCountsDrawWhetherOrNotAnEffectFollows()
    {
        GameDatabase db = Db(steam: 0, smoke: 0, bubbling: 0);
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        int buckets = Furniture(world, room).Count(o => o.ObjectTypeId == Bucket);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);

        sim.Tick();

        // Four reloads plus the two bucket counts (lines 0x6C and 0xE3); no slot is drawn without an effect.
        var oracle = new Oracle(1);
        oracle.Next(16 * Ticks);
        oracle.Next(16 * Ticks);
        oracle.Next(buckets);
        oracle.Next(16 * Ticks);
        oracle.Next(16 * Ticks);
        oracle.Next(buckets);
        Assert.Equal(oracle.Seed, SeedOf(sim));
        Assert.DoesNotContain(Requests(sink), r => r.EffectId.Value is Steam or Smoke or LavaBubbling);
    }

    [Fact]
    public void ARoomWithNoSlotsAndNoBucketsStillDrawsItsFourReloads()
    {
        GameDatabase db = Db();
        WorldState world = World(db, x0: 3, y0: 3, size: 1, furnish: false);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);

        sim.Tick();

        var oracle = new Oracle(1);
        for (int i = 0; i < 4; i++) oracle.Next(16 * Ticks);
        Assert.Equal(oracle.Seed, SeedOf(sim));
        Assert.Empty(Requests(sink));
    }

    [Fact]
    public void TheSteamTimerAsksForEffect50AtItsDrawnFloorSlotOneTileHighWithNoHeading()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);
        List<int> slots = [.. SlabChain.Order(world, room).Where(t => AutoFurnitureRules.IsFloorSlot(world, room, t))];

        sim.Tick();

        var oracle = new Oracle(1);
        oracle.Next(16 * Ticks);
        int at = slots[oracle.Next(slots.Count)];
        ViewEffectRequest request = Assert.Single(Requests(sink), r => r.EffectId.Value == Steam);
        Assert.Equal(new ViewEffectRequest(new EffectId(Steam), Keeper,
            new FpPosition3(new Fp32(CreatureState.CentreOf(at % world.Width)), new Fp32(CreatureState.CentreOf(at / world.Width)),
                new Fp32(0x1000))), request);
    }

    [Fact]
    public void TheSmokeTimerAsksForEffect51AtItsDrawnWallSlotSetInto0x700TowardTheWallFacingIt()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);
        List<int> slots = WallSlots(world, db, room);
        int floors = FloorSlots(world, room), buckets = Furniture(world, room).Count(o => o.ObjectTypeId == Bucket);

        sim.Tick();

        var oracle = new Oracle(1);
        oracle.Next(16 * Ticks);
        oracle.Next(floors);
        oracle.Next(16 * Ticks);
        oracle.Next(buckets);
        oracle.Next(16 * Ticks);
        int at = slots[oracle.Next(slots.Count)];
        int side = AutoFurnitureRules.WallSide(world, db, room, at);
        int x = CreatureState.CentreOf(at % world.Width), y = CreatureState.CentreOf(at / world.Width);
        (int dx, int dy) = side switch { 0 => (0, -0x700), 1 => (0x700, 0), 2 => (0, 0x700), _ => (-0x700, 0) };
        ViewEffectRequest request = Assert.Single(Requests(sink), r => r.EffectId.Value == Smoke);
        Assert.Equal(new ViewEffectRequest(new EffectId(Smoke), Keeper,
            new FpPosition3(new Fp32(x + dx), new Fp32(y + dy), new Fp32(0x1000)), new Dk2Angle((ushort)(side << 9))), request);
    }

    [Fact]
    public void TheLavaBubblingTimerAsksForEffect93AtTheDrawnSteelBucketOfTheNewestFirstBuckets()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        RoomInstance room = Workshop(db, world, (3 * world.Width) + 3);
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);
        List<ObjectState> buckets = [.. Furniture(world, room).Where(o => o.ObjectTypeId == Bucket)];
        int floors = FloorSlots(world, room), walls = WallSlots(world, db, room).Count;

        sim.Tick();

        var oracle = new Oracle(1);
        oracle.Next(16 * Ticks);
        oracle.Next(floors);
        oracle.Next(16 * Ticks);
        oracle.Next(buckets.Count);
        oracle.Next(16 * Ticks);
        oracle.Next(walls);
        oracle.Next(16 * Ticks);
        ObjectState picked = buckets[oracle.Next(buckets.Count)];
        (int x, int y) = AutoFurnitureRules.FurniturePosition(world, db, room, picked);
        ViewEffectRequest request = Assert.Single(Requests(sink), r => r.EffectId.Value == LavaBubbling);
        Assert.Equal((Keeper, x, y, new Dk2Angle(0)), (request.Owner.Value, request.Position.X.Raw, request.Position.Y.Raw, request.Heading));
    }

    [Fact]
    public void AssumingTheMeshPointExistsTheLavaSpillTimerWalksTheWholeListSoItCanLandOnAnotherObjectNotTheBucketDrawn_Q731a()
    {
        // 0x004F4C9E..0x004F4CEF walks from the list's head by the drawn index over every object, where
        // the last timer (0x004F4FC0) skips the objects that are not Steel Buckets. One bucket among
        // three objects draws random(1) = 0, which is the newest object whatever it is.
        foreach ((byte newest, ushort expected) in new (byte, ushort)[] { (WorkBench, 0), (Bucket, BucketSpill), (Anvil, AnvilSparks) })
        {
            GameDatabase db = Db();
            WorldState world = World(db, furnish: false);
            byte[] oldestFirst = [.. new[] { Bucket, Anvil, WorkBench }.Where(t => t != newest), newest];
            int tile = (4 * world.Width) + 4;
            foreach (byte type in oldestFirst) world.SpawnObject(db, type, Keeper, tile++).AutoPlaced = true;
            GameSimulation sim = TimerSim(db, world, out ListEventSink sink);

            sim.Tick();

            List<ViewEffectRequest> spills = [.. Requests(sink).Where(r => r.EffectId.Value is BucketSpill or AnvilSparks)];
            Assert.Equal(expected == 0 ? 0 : 1, spills.Count);
            if (expected != 0) Assert.Equal(expected, spills[0].EffectId.Value);
        }
    }

    [Fact]
    public void TheHandlersTypeTestsAndRangesAreTheShippedConstants()
    {
        // 0x004F4C82 compares the object type byte with 0x10; the constructor's range is 4 seconds of ticks and
        // a reload draws 16 and adds 8.
        Assert.Equal(Bucket, WorkshopTimerRules.SteelBucketObjectId);
        Assert.Equal((4, 16, 8, 0x700), (WorkshopTimerRules.StartRangeSeconds, WorkshopTimerRules.ReloadRangeSeconds,
            WorkshopTimerRules.ReloadFloorSeconds, WorkshopTimerRules.WallInset));
    }

    [Fact]
    public void AssumingTheMeshPointExistsTheLavaSpillEffectIsOwnedByTheObjectNotTheRoom_Q731a()
    {
        // 0x004F4D7B reads the owner word of the object reached (+0x28).
        GameDatabase db = Db();
        WorldState world = World(db, furnish: false);
        world.SpawnObject(db, Bucket, PlayerIds.Neutral, (4 * world.Width) + 4).AutoPlaced = true;
        GameSimulation sim = TimerSim(db, world, out ListEventSink sink);

        sim.Tick();

        ViewEffectRequest spill = Assert.Single(Requests(sink), r => r.EffectId.Value == BucketSpill);
        Assert.Equal(PlayerIds.Neutral, spill.Owner.Value);
    }

    [Fact]
    public void RoomsTickNewestFirstAsTheRoomManagersListRunsThem()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 1, furnish: false);
        // The fixture's first room is (3,3); a second, newer Workshop is built through the room path.
        GameSimulation sim = Sim(db, world, new WorkshopTimerSystem());
        BuildTile(db, world, new TilePos(10, 10));
        RoomInstance older = Workshop(db, world, (3 * world.Width) + 3), newer = Workshop(db, world, (10 * world.Width) + 10);
        foreach (RoomInstance room in new[] { older, newer })
            (Lowest(world, room).WorkshopSteamTimer, Lowest(world, room).WorkshopLavaSpillTimer,
                Lowest(world, room).WorkshopSmokeTimer, Lowest(world, room).WorkshopLavaBubblingTimer) = (0, 0, 0, 0);
        var oracle = new Oracle(SeedOf(sim));
        int first = oracle.Next(16 * Ticks) + (8 * Ticks);
        for (int i = 0; i < 3; i++) oracle.Next(16 * Ticks);   // the newer room's other three reloads
        int second = oracle.Next(16 * Ticks) + (8 * Ticks);

        sim.Tick();

        Assert.Equal(first, Lowest(world, newer).WorkshopSteamTimer);
        Assert.Equal(second, Lowest(world, older).WorkshopSteamTimer);
    }

    private static void BuildTile(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Workshop), Keeper);
        SlabChain.Changed(world, db, world.TileIndex(at), terrainBefore, ownerBefore);
    }

    [Fact]
    public void ABuiltTileThatStartsARoomDrawsFourStartingTimersOfFourSecondsInTheConstructorsOrder()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 1, furnish: false);
        GameSimulation sim = Sim(db, world);
        uint before = SeedOf(sim);

        BuildTile(db, world, new TilePos(10, 10));

        // 0x004F4A60: lines 0x22, 0x23, 0x24, 0x25 into +0x49, +0x4D, +0x51, +0x55, each random(4 x ticks).
        var oracle = new Oracle(before);
        (int steam, int smoke, int bubbling, int spill) = (oracle.Next(4 * Ticks), oracle.Next(4 * Ticks), oracle.Next(4 * Ticks), oracle.Next(4 * Ticks));
        TileState tile = world.Tile(new TilePos(10, 10));
        Assert.Equal((steam, smoke, bubbling, spill),
            (tile.WorkshopSteamTimer, tile.WorkshopSmokeTimer, tile.WorkshopLavaBubblingTimer, tile.WorkshopLavaSpillTimer));
        Assert.Equal(oracle.Seed, SeedOf(sim));
    }

    [Fact]
    public void ATileJoiningARoomDrawsNothingAndTheRoomKeepsItsTimersOnItsNewLowestTile()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 1, furnish: false);
        GameSimulation sim = Sim(db, world);
        BuildTile(db, world, new TilePos(10, 10));
        TileState seeded = world.Tile(new TilePos(10, 10));
        uint after = SeedOf(sim);

        BuildTile(db, world, new TilePos(10, 9));   // north of it: the room's lowest tile is now (10, 9)

        Assert.Equal(after, SeedOf(sim));
        TileState head = world.Tile(new TilePos(10, 9)), old = world.Tile(new TilePos(10, 10));
        Assert.Equal((seeded.WorkshopSteamTimer, seeded.WorkshopSmokeTimer, seeded.WorkshopLavaBubblingTimer, seeded.WorkshopLavaSpillTimer),
            (head.WorkshopSteamTimer, head.WorkshopSmokeTimer, head.WorkshopLavaBubblingTimer, head.WorkshopLavaSpillTimer));
        Assert.Equal((0, 0, 0, 0),
            (old.WorkshopSteamTimer, old.WorkshopSmokeTimer, old.WorkshopLavaBubblingTimer, old.WorkshopLavaSpillTimer));
    }

    [Fact]
    public void ATileJoiningTwoRoomsTakesItsFirstNeighboursTimersAndDropsTheOthersWithoutDrawing()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 1, furnish: false);
        GameSimulation sim = Sim(db, world);
        BuildTile(db, world, new TilePos(10, 10));
        BuildTile(db, world, new TilePos(12, 10));
        TileState east = world.Tile(new TilePos(12, 10));
        uint after = SeedOf(sim);

        BuildTile(db, world, new TilePos(11, 10));   // neighbours in order N, E, S, W: the east room is first

        Assert.Equal(after, SeedOf(sim));
        TileState head = world.Tile(new TilePos(10, 10));
        Assert.Equal((east.WorkshopSteamTimer, east.WorkshopSmokeTimer, east.WorkshopLavaBubblingTimer, east.WorkshopLavaSpillTimer),
            (head.WorkshopSteamTimer, head.WorkshopSmokeTimer, head.WorkshopLavaBubblingTimer, head.WorkshopLavaSpillTimer));
        Assert.Equal(0, world.Tile(new TilePos(12, 10)).WorkshopSteamTimer + world.Tile(new TilePos(11, 10)).WorkshopSteamTimer);
    }

    [Fact]
    public void SellingTheMiddleOfARowConstructsBothPiecesAndDrawsFourTimersForEach()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 1, furnish: false);
        GameSimulation sim = Sim(db, world);
        BuildTile(db, world, new TilePos(10, 10));
        BuildTile(db, world, new TilePos(11, 10));
        BuildTile(db, world, new TilePos(12, 10));
        uint before = SeedOf(sim);

        ref TileState middle = ref world.Tile(new TilePos(11, 10));
        byte terrainBefore = middle.TerrainId, ownerBefore = middle.OwnerPlayerId;
        middle.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(world, db, world.TileIndex(new TilePos(11, 10)), terrainBefore, ownerBefore);

        var oracle = new Oracle(before);
        int[][] expected = [[0, 0, 0, 0], [0, 0, 0, 0]];
        for (int piece = 0; piece < 2; piece++)
            (expected[piece][0], expected[piece][1], expected[piece][2], expected[piece][3]) =
                (oracle.Next(4 * Ticks), oracle.Next(4 * Ticks), oracle.Next(4 * Ticks), oracle.Next(4 * Ticks));
        TileState left = world.Tile(new TilePos(10, 10)), right = world.Tile(new TilePos(12, 10));
        Assert.Equal<IEnumerable<int>>(expected[0], [left.WorkshopSteamTimer, left.WorkshopSmokeTimer, left.WorkshopLavaBubblingTimer, left.WorkshopLavaSpillTimer]);
        Assert.Equal<IEnumerable<int>>(expected[1], [right.WorkshopSteamTimer, right.WorkshopSmokeTimer, right.WorkshopLavaBubblingTimer, right.WorkshopLavaSpillTimer]);
        Assert.Equal(oracle.Seed, SeedOf(sim));
        Assert.Equal(0, world.Tile(new TilePos(11, 10)).WorkshopSteamTimer);
    }

    [Fact]
    public void AssumingRoomsAreMadeBeforeCreaturesAWorkshopTheMapStartsWithIsConstructedAtSimulationStartAndAnAbsorbedRoomStillDrew_Q731b()
    {
        // A U loaded row by row: (10,10) and (12,10) each start a room; (10,11) and (11,11) join the first,
        // and (12,11) joins the room to its north, the second, which absorbs the first behind it. Two
        // rooms were constructed (eight draws), and the survivor is the second, as the first neighbour
        // north wins: its timers sit on the merged room's lowest tile.
        GameDatabase db = Db();
        LevelMap basis = Dk2TerrainFixture.Map(24, 24);
        MapTile[] tiles = [.. basis.Tiles];
        foreach ((int x, int y) in new[] { (10, 10), (12, 10), (10, 11), (11, 11), (12, 11) })
            tiles[(y * 24) + x] = new MapTile(Dk2TerrainFixture.WorkshopTerrain, Keeper, 0, 0);
        WorldState world = WorldState.FromMap(db, new LevelMap(24, 24, tiles), [(byte)0, PlayerIds.Neutral, Keeper]);

        GameSimulation sim = Sim(db, world);

        var oracle = new Oracle(1);
        int[][] draws = [[.. Enumerable.Range(0, 4).Select(_ => oracle.Next(4 * Ticks))], [.. Enumerable.Range(0, 4).Select(_ => oracle.Next(4 * Ticks))]];
        Assert.Equal(oracle.Seed, SeedOf(sim));
        TileState head = world.Tile(new TilePos(10, 10));
        Assert.Equal<IEnumerable<int>>(draws[1], [head.WorkshopSteamTimer, head.WorkshopSmokeTimer, head.WorkshopLavaBubblingTimer, head.WorkshopLavaSpillTimer]);
        Assert.Equal(0, world.Tile(new TilePos(12, 10)).WorkshopSteamTimer);
    }

    [Fact]
    public void TheTimersAreSavedAndHashed()
    {
        GameDatabase db = Db();
        WorldState world = World(db, size: 3, furnish: false);
        GameSimulation original = Sim(db, world, new WorkshopTimerSystem());
        BuildTile(db, world, new TilePos(15, 15));
        original.Run(40);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new WorkshopTimerSystem()]);

        Assert.Equal(original.StateHash(), restored.StateHash());
        original.Run(200);
        restored.Run(200);
        Assert.Equal(original.StateHash(), restored.StateHash());
        Assert.Equal(SeedOf(original), SeedOf(restored));

        ulong hash = original.StateHash();
        original.World.Tile(new TilePos(15, 15)).WorkshopSmokeTimer += 1;
        Assert.NotEqual(hash, original.StateHash());
    }
}
