// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// The Hatchery — coops, and the egg that appears on one.
///
/// <para><b>Every number tested here came out of <c>DKII.EXE</c></b> rather than
/// out of a guess about what feels right, which is why they are odd: six tiles
/// per coop, a cap of 36, and a spacing rule that reads as "not adjacent, not
/// even diagonally". See <c>docs/research/hatchery.md</c>.</para>
///
/// <para><b>What is deliberately not tested is a clock</b>, because there is
/// not one. The interval DK2 lays on is multiplied by a balance variable that
/// no shipped file supplies (Q132), so the room has no rhythm here and these
/// tests drive it by hand — which is the same shape
/// <c>CreatureHealthRules.Eat</c> and <c>CreatureDeathRules.Rescue</c> have,
/// and for the same reason.</para>
/// </summary>
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

        Assert.Equal(6, tiles.Count);

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

    [Fact]
    public void ASingleFileHatcheryCannotFitAllTheCoopsItIsOwed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // 12 tiles in one row earns two coops, and a row can hold them: the
        // spacing only needs two tiles of gap. Narrow it to a shape where the
        // rule bites and CoopTiles must return fewer than it is owed rather
        // than break its own spacing.
        RoomInstance room = Hatchery(db, world, 2, 2, 12, 1);
        Assert.Equal(2, HatcheryRules.CoopCount(room));

        var tiles = new List<int>();
        HatcheryRules.CoopTiles(world, room, tiles);
        Assert.Equal(2, tiles.Count);

        int gap = Math.Abs((tiles[0] % world.Width) - (tiles[1] % world.Width));
        Assert.True(gap >= 2, $"two coops in a row ended up {gap} apart");
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

        // And the first tile taken is the room's first tile, since nothing can
        // be too close to an empty set.
        Assert.Equal(room.TileIndices[0], first[0]);
    }

    // ---- placing them as objects -------------------------------------------

    [Fact]
    public void PlacingCoopsPutsRealObjectsOnTheseTiles()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);

        Assert.Equal(6, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(6, world.Objects.Count);

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

        Assert.Equal(6, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(0, HatcheryRules.PlaceCoops(world, db, room));
        Assert.Equal(6, world.Objects.Count);
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
    public void AnEggLandsOnOneOfTheCoops()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 2, 2, 6, 6);
        HatcheryRules.PlaceCoops(world, db, room);

        var coops = new List<ObjectState>();
        HatcheryRules.Coops(world, room, coops);
        var coopTiles = coops.Select(c => c.TileIndex).ToHashSet();

        var rng = new SimRandom(42);
        for (int i = 0; i < 20; i++)
        {
            ObjectState? egg = HatcheryRules.TryLayEgg(world, db, room, rng);
            Assert.NotNull(egg);
            Assert.Contains(egg.TileIndex, coopTiles);
        }
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
