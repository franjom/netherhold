// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The Hatchery — coops, and the egg that appears on one. Every number
/// here is read from <c>DKII.EXE</c>, which is why they are odd: six tiles per
/// coop, a cap of 36, and a spacing rule that reads as "not adjacent, not even
/// diagonally" (docs/research/hatchery.md). These tests drive the room by hand;
/// its clock has its own file, <see cref="HatcheryClockTests"/>.</summary>
public class HatcheryTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    /// <summary>Paints a w×h block of hatchery from (x, y) and returns the room.</summary>
    private static RoomInstance Hatchery(
        GameDatabase db, WorldState world, int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                ref TileState tile = ref world.Tile(x + dx, y + dy);
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), Keeper);
            }
        }

        foreach (RoomInstance room in RoomIndex.Build(world, db))
            if (room.Definition.RoomId == (byte)RoomTypeId.Hatchery)
                return room;

        throw new InvalidOperationException("the painted hatchery was not found");
    }

    // ---- how many coops -----------------------------------------------------

    [Theory]
    [InlineData(3, 3, 1)]     // 9 tiles, the smallest room there is
    [InlineData(4, 3, 2)]     // 12
    [InlineData(4, 4, 2)]     // 16
    [InlineData(5, 5, 4)]     // 25
    [InlineData(6, 6, 6)]     // 36
    public void ACoopForEverySixTiles(int w, int h, int expected)
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Integer division, and the remainder is simply lost: 16 tiles buys two
        // coops, not two and a bit.
        Assert.Equal(expected, HatcheryRules.CoopCount(Hatchery(db, world, 2, 2, w, h)));
    }

    [Fact]
    public void TheCoopCapIsThirtySix()
    {
        // Unreachable on any shipped map -- 216 tiles -- so this pins the
        // constant rather than a behaviour. Worth pinning anyway: a cap nobody
        // asserts is a cap somebody later "simplifies" away.
        Assert.Equal(36, HatcheryRules.MaxCoops);
        Assert.Equal(6, HatcheryRules.TilesPerCoop);
    }

    // ---- where they stand ---------------------------------------------------

    [Fact]
    public void CoopsAreNeverAdjacent()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);

        // Owed six, but only the 4×4 interior holds them, and it fits four.
        Assert.Equal(4, tiles.Count);

        // The whole spacing rule, stated as the geometry rather than as the
        // fixed-point comparison it comes from: no two coops within a squared
        // tile distance of 3, so not the same tile, not orthogonally adjacent,
        // and not diagonally adjacent either.
        for (int i = 0; i < tiles.Count; i++)
        {
            for (int j = i + 1; j < tiles.Count; j++)
            {
                int dx = (tiles[i] % world.Width) - (tiles[j] % world.Width);
                int dy = (tiles[i] / world.Width) - (tiles[j] / world.Width);
                Assert.True(
                    (dx * dx) + (dy * dy) >= HatcheryRules.MinCoopSeparationSquared,
                    $"coops at {tiles[i]} and {tiles[j]} are too close");
            }
        }
    }

    /// <summary>12 tiles in one row are owed two coops, but a coop stands only on an
    /// interior tile (<c>0x004DE220</c> asks <c>Room_IsFloorSlot</c>), and a row has none.</summary>
    [Fact]
    public void ASingleFileHatcheryHasNoInteriorAndSoNoCoops()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 12, 1);
        Assert.Equal(2, HatcheryRules.CoopCount(room));

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);
        Assert.Empty(tiles);
    }

    /// <summary>A coop the room did not place (a level's own) is passed over: its tile is not
    /// taken and it does not count in the spacing (<c>0x004DE220</c> checks only this pass's
    /// coops), so the walk takes the next interior tile, right beside it. Chain order here is
    /// row-major, the order of a painted room; a dragged room's wave order is not pinned.</summary>
    [Fact]
    public void ACoopTheRoomDidNotPlaceIsSkippedAndNotSpacedFrom()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 5, 5);
        world.SpawnObject(db, HatcheryRules.CoopObjectId, Keeper, (3 * world.Width) + 3);

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);
        Assert.Equal((3 * world.Width) + 4, tiles[0]);
    }

    /// <summary>The owner's observation (2026-10-09): a 5×5 hatchery's four coops stand on a
    /// 2×2 grid inside the rim, the interior walked in chain order with the √3 spacing.</summary>
    [Fact]
    public void AFiveByFiveHatcherysCoopsStandOnATwoByTwoGridInsideTheRim()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 5, 5);

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);
        Assert.Equal(
            new[] { (3, 3), (5, 3), (3, 5), (5, 5) },
            tiles.Select(t => (t % world.Width, t / world.Width)));
    }

    [Fact]
    public void CoopPlacementIsDeterministicAndTakesTheEarliestTilesItCan()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);

        var first = new List<int>();
        var second = new List<int>();
        HatcheryRules.CoopTiles(world, room, first);
        HatcheryRules.CoopTiles(world, room, second);

        // No RNG in this, and the original has none either -- it walks the
        // room's tiles. Asserted because a room instance is rebuilt from the
        // tiles every time it is asked for, so anything unstable here would
        // move coops around under a running game.
        Assert.Equal(first, second);

        // And the first tile taken is the first interior tile, since nothing can
        // be too close to an empty set.
        Assert.Equal((3 * world.Width) + 3, first[0]);
    }

    // ---- placing them as objects -------------------------------------------

    [Fact]
    public void PlacingCoopsPutsRealObjectsOnTheseTiles()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);

        Assert.Equal(4, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(4, world.Objects.Count);

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);
        foreach (int index in tiles)
            Assert.True(HatcheryRules.HasCoop(world, index), $"no coop on tile {index}");

        foreach (ObjectState coop in world.Objects)
        {
            Assert.Equal(HatcheryRules.CoopObjectId, coop.ObjectTypeId);
            Assert.Equal(Keeper, coop.OwnerPlayerId);

            // A coop is nobody's bed, so the union's Lair field stays zero --
            // the discipline that makes hashing every field unconditionally
            // safe.
            Assert.Equal(0, coop.CreatureId);
        }
    }

    [Fact]
    public void PlacingCoopsTwiceAddsNothingTheSecondTime()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);

        Assert.Equal(4, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(0, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(4, world.Objects.Count);
    }

    [Fact]
    public void ARoomTooSmallForACoopGetsNone()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Five tiles is under the six a coop costs. DK2 still lets such a room
        // lay -- on the floor -- which the egg test below covers.
        RoomInstance room = Hatchery(db, world, 2, 2, 5, 1);
        Assert.Equal(0, HatcheryRules.CoopCount(room));
        Assert.Equal(0, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Empty(world.Objects);
    }

    // ---- the egg ------------------------------------------------------------

    [Fact]
    public void AHatcheryLaysAnEggAndNotAChicken()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
        HatcheryRules.PlaceCoops(world, db, room);

        ObjectState? egg = HatcheryRules.TryLayEgg(world, db, room, new SimRandom(42));

        // The correction this whole file exists to record: the room produces
        // object 47, the Egg, and the Egg is NOT food. Only the Chicken carries
        // TypeFood and only the Chicken has HpFromChicken against it, so
        // something turns one into the other and we have not found what (Q133).
        Assert.NotNull(egg);
        Assert.Equal(HatcheryRules.EggObjectId, egg.ObjectTypeId);
        Assert.NotEqual(HatcheryRules.ChickenObjectId, egg.ObjectTypeId);

        ObjectDef definition = Assert.IsType<ObjectDef>(db.ObjectById(egg.ObjectTypeId));
        Assert.False(definition.IsFood, "an egg must not be edible, or Q133 is not a question");
        Assert.True(Assert.IsType<ObjectDef>(
            db.ObjectById(HatcheryRules.ChickenObjectId)).IsFood);
    }

    [Fact]
    public void EggsUseTheRoomFloorAndSubTileOffsetsInsteadOfStackingAtTheCoop()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
        HatcheryRules.PlaceCoops(world, db, room);

        var coops = new List<ObjectState>();
        HatcheryRules.Coops(world, room, coops);
        var coopTiles = coops.Select(c => c.TileIndex).ToHashSet();

        var rng = new SimRandom(42);
        var positions = new HashSet<(int X, int Y)>();
        var tiles = new HashSet<int>();
        for (int i = 0; i < 20; i++)
        {
            ObjectState? egg = HatcheryRules.TryLayEgg(world, db, room, rng);
            Assert.NotNull(egg);
            tiles.Add(egg.TileIndex);
            int x = CreatureState.CentreOf(egg.TileIndex % world.Width);
            int y = CreatureState.CentreOf(egg.TileIndex / world.Width);
            Assert.InRange(egg.PosX, x - 0x400, x + 0x3FF);
            Assert.InRange(egg.PosY, y - 0x400, y + 0x3FF);
            Assert.True(positions.Add((egg.PosX, egg.PosY)), "eggs must retain their individual drawn positions");
            ObjectState chicken = HatcheryRules.Hatch(world, db, egg);
            Assert.Equal((egg.PosX, egg.PosY), (chicken.PosX, chicken.PosY));
        }
        Assert.True(tiles.Except(coopTiles).Any(), "the entire flock stayed beneath coops");
    }

    [Fact]
    public void WithNoCoopsTheEggGoesOnTheFloorInstead()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Under six tiles, so the room earns no coop at all -- and DK2 still
        // lays, picking an empty tile of the room. A hatchery that produced
        // nothing until it was six tiles wide would be a different game.
        RoomInstance room = Hatchery(db, world, 2, 2, 5, 1);
        Assert.Empty(world.Objects);

        ObjectState? egg = HatcheryRules.TryLayEgg(world, db, room, new SimRandom(7));

        Assert.NotNull(egg);
        Assert.Equal(HatcheryRules.EggObjectId, egg.ObjectTypeId);
        Assert.Contains(egg.TileIndex, room.TileIndices);
    }

    [Fact]
    public void TheFloorFillsUpAndThenTheRoomStops()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 5, 1);

        // Five tiles, no coops, and the floor path only picks EMPTY tiles -- so
        // the fifth egg is the last and the sixth attempt has nowhere to go.
        var rng = new SimRandom(7);
        for (int i = 0; i < 5; i++)
            Assert.NotNull(HatcheryRules.TryLayEgg(world, db, room, rng));

        Assert.Null(HatcheryRules.TryLayEgg(world, db, room, rng));
        Assert.Equal(5, world.Objects.Count);
    }

    [Fact]
    public void TwoRunsFromOneSeedLayInTheSamePlaces()
    {
        // The draw is behaviour: two hatcheries of one size lay in the same
        // rhythm but not the same places, which is the first mechanic in this
        // simulation where a generator's position is observable (Q3). Ours is
        // not DK2's generator and is not trying to be -- what the determinism
        // gate needs is that the same seed gives the same answer.
        static List<int> Run()
        {
            GameDatabase db = Db;
            WorldState world = World(db);
            RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
            HatcheryRules.PlaceCoops(world, db, room);

            var rng = new SimRandom(1234);
            var where = new List<int>();
            for (int i = 0; i < 10; i++)
                where.Add(HatcheryRules.TryLayEgg(world, db, room, rng)!.TileIndex);
            return where;
        }

        List<int> first = Run();
        Assert.Equal(first, Run());

        // And it really is choosing, rather than always taking the same coop.
        Assert.True(first.Distinct().Count() > 1, "every egg went to the same coop");
    }

    // ---- the egg becomes a chicken ------------------------------------------

    [Fact]
    public void AnEggHatchesIntoAChickenOnTheSameTile()
    {
        // CONFIRMED by the owner on 2026-09-05, watching one: "it produces eggs
        // that hatch... after that a chicken appears and roams around the
        // hatchery". The data had only implied the step -- the room lays 47 and
        // a creature eats 9 -- and now it is a fact.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
        HatcheryRules.PlaceCoops(world, db, room);

        ObjectState egg = HatcheryRules.TryLayEgg(world, db, room, new SimRandom(42))!;
        int tile = egg.TileIndex;
        int eggId = egg.Id;

        ObjectState chicken = HatcheryRules.Hatch(world, db, egg);

        Assert.Equal(HatcheryRules.ChickenObjectId, chicken.ObjectTypeId);
        Assert.Equal(tile, chicken.TileIndex);
        Assert.Equal(Keeper, chicken.OwnerPlayerId);

        // The egg is gone, and gone properly: not left standing under the
        // chicken, and not reusing its id.
        Assert.Null(world.ObjectById(eggId));
        Assert.NotEqual(eggId, chicken.Id);

        // And the thing that appears IS food, which the egg was not.
        Assert.True(Assert.IsType<ObjectDef>(db.ObjectById(chicken.ObjectTypeId)).IsFood);
    }

    [Fact]
    public void HatchingRefusesAnythingThatIsNotAnEgg()
    {
        // A coop is the nearest thing to hand and hatching one would be absurd,
        // which is the point: this throws rather than quietly turning furniture
        // into poultry.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
        HatcheryRules.PlaceCoops(world, db, room);

        ObjectState coop = world.Objects[0];
        Assert.Throws<ArgumentException>(() => HatcheryRules.Hatch(world, db, coop));
        Assert.Equal(HatcheryRules.CoopObjectId, world.Objects[0].ObjectTypeId);
    }

    [Fact]
    public void TheHatchDelayIsTwoSeconds()
    {
        // Timed from an animation rather than from a counter, and recorded as
        // such. Nothing advances a clock towards it yet -- laying has no clock
        // either (Q132) -- so this pins the number, not a mechanic.
        Assert.Equal(2, HatcheryRules.EggHatchSeconds);
    }

    // ---- how big the flock gets ---------------------------------------------

    [Theory]
    [InlineData(3, 3, 9)]     // the owner counted this one
    [InlineData(5, 5, 25)]    // and reported "more than 20" for this one
    [InlineData(4, 4, 16)]
    public void AHatcheryHoldsOneChickenPerTile(int w, int h, int expected)
    {
        // MEASURED BY THE OWNER (2026-09-05): a 3x3 settles at nine and a 5x5 at
        // more than twenty — one per tile on both counts. The Chicken's
        // RoomCapacity is 1, a field meaning "how many of it a room tile holds",
        // so the nine is not a constant in our code: it comes from the definition.
        GameDatabase db = Db;
        WorldState world = World(db);

        Assert.Equal(expected, HatcheryRules.FlockCapacity(db, Hatchery(db, world, 2, 2, w, h)));
    }

    [Fact]
    public void AThreeByThreeHatcherySettlesAtExactlyNineChickens()
    {
        // The owner's measurement, run as a simulation: lay, hatch, repeat, far
        // more times than nine. It has to STOP at nine.
        //
        // This is the test that would have caught the wrong reading. Had the
        // free-range cap of 12 been the limit, this would settle at twelve; had
        // there been no limit at all it would run to a hundred.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 3, 3);
        HatcheryRules.PlaceCoops(world, db, room);

        var rng = new SimRandom(99);
        for (int i = 0; i < 100; i++)
        {
            ObjectState? egg = HatcheryRules.TryLayEgg(world, db, room, rng);
            if (egg is not null) HatcheryRules.Hatch(world, db, egg);
        }

        Assert.Equal(9, HatcheryRules.Flock(world, room));
        Assert.True(HatcheryRules.FlockIsFull(world, db, room));

        // The coop is still there and was never counted as a bird.
        Assert.Equal(1, world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.CoopObjectId));
    }

    [Fact]
    public void AnEggInTheAirCountsAgainstTheCap()
    {
        // An egg is a chicken two seconds from now. A room that laid while full
        // of eggs would overshoot its own limit by however many were in flight,
        // which is why the cap counts both.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 3, 3);
        HatcheryRules.PlaceCoops(world, db, room);

        var rng = new SimRandom(5);
        for (int i = 0; i < 9; i++)
            Assert.NotNull(HatcheryRules.TryLayEgg(world, db, room, rng));

        // Nine eggs, no chickens yet, and the room is already full.
        Assert.Equal(0, HatcheryRules.Flock(world, room));
        Assert.Equal(9, HatcheryRules.Birds(world, room));
        Assert.Null(HatcheryRules.TryLayEgg(world, db, room, rng));
    }

    [Fact]
    public void EatingOneMakesRoomForAnother()
    {
        // The other half of an equilibrium: the room resumes the moment
        // something takes a bird out of it. Nothing eats yet, so this stands in
        // for it by removing the object -- which is what eating will do.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 3, 3);
        HatcheryRules.PlaceCoops(world, db, room);

        var rng = new SimRandom(11);
        for (int i = 0; i < 20; i++)
        {
            ObjectState? laid = HatcheryRules.TryLayEgg(world, db, room, rng);
            if (laid is not null) HatcheryRules.Hatch(world, db, laid);
        }
        Assert.Equal(9, HatcheryRules.Flock(world, room));
        Assert.Null(HatcheryRules.TryLayEgg(world, db, room, rng));

        ObjectState eaten = world.Objects.First(
            o => o.ObjectTypeId == HatcheryRules.ChickenObjectId);
        Assert.True(world.RemoveObject(eaten.Id));

        Assert.NotNull(HatcheryRules.TryLayEgg(world, db, room, rng));
    }

    // ---- the cap we know about and do not apply -----------------------------

    [Fact]
    public void TheFreeRangeCapIsReadableAndIsTwelve()
    {
        // DK2 checks a per-player counter against this before laying at a coop.
        // We expose the number and do NOT enforce it, because what that counter
        // counts -- loose chickens only, or everything a hatchery ever made --
        // is UNKNOWN, and a wrong guess silently throttles a room. The value is
        // asserted so that when the answer arrives, the number is already right.
        Assert.Equal(12, HatcheryRules.FreeRangeCap(Db));
    }
}
